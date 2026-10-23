using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using System.Windows.Threading;
using BottleneckHunter.Views.Overlay;

namespace BottleneckHunter.Core.Overlay;

/// <summary>Tudo que o overlay mostra num instante.</summary>
/// <param name="Game">Nome do programa em primeiro plano (o jogo), se houver.</param>
/// <param name="FrameError">Por que o FPS não está disponível (null se está tudo certo).</param>
public sealed record OverlayData(string? Game, FrameStats Frames, HwSnapshot Hw, string? FrameError, DateTime Time);

/// <summary>
/// Liga e desliga o overlay de jogos, registra o atalho global Ctrl+Shift+O e,
/// 4 vezes por segundo, junta FPS (ETW) com os sensores (App.Sensors) para a janela do overlay
/// e para a página de configurações. A contagem de quadros só roda enquanto alguém precisa dela.
/// </summary>
public static class OverlayService
{
    public const string HotkeyText = "Ctrl + Shift + O";

    private const int HotkeyId = 0xB0B1;
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;
    private const uint VK_O = 0x4F;

    private static readonly FrameMonitor Frames = new();
    private static readonly int OwnPid = Environment.ProcessId;
    private static readonly Dictionary<int, string> NameCache = new();

    private static DispatcherTimer? _timer;
    private static HwndSource? _hotkeyWindow;
    private static OverlayWindow? _window;
    private static int _watchers;
    private static int _targetPid;
    private static IntPtr _targetHwnd;
    private static string? _targetName;
    private static int _ticks;

    public static OverlaySettings Settings { get; private set; } = new();

    public static bool IsVisible { get; private set; }

    /// <summary>Mensagem quando o atalho não pôde ser registrado (outro programa já usa).</summary>
    public static string? HotkeyError { get; private set; }

    public static OverlayData? Latest { get; private set; }

    /// <summary>Novos números, 4 vezes por segundo, já na thread da interface.</summary>
    public static event Action<OverlayData>? Tick;

    /// <summary>O overlay foi ligado/desligado ou as preferências mudaram.</summary>
    public static event Action? StateChanged;

    public static void Initialize()
    {
        Settings = OverlaySettings.Load();
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => OnTick();
        RegisterHotkey();
        if (Settings.StartWithApp) Show();
    }

    public static void Shutdown()
    {
        if (_hotkeyWindow != null)
        {
            UnregisterHotKey(_hotkeyWindow.Handle, HotkeyId);
            _hotkeyWindow.Dispose();
            _hotkeyWindow = null;
        }
        _timer?.Stop();
        try { _window?.Close(); } catch { }
        _window = null;
        Frames.Dispose();
    }

    public static void Toggle()
    {
        if (IsVisible) Hide();
        else Show();
    }

    public static void Show()
    {
        if (IsVisible) return;
        _window ??= new OverlayWindow();
        _window.ApplySettings(Settings);
        IsVisible = true;
        UpdateRunning();
        OnTick();
        _window.FadeIn();
        StateChanged?.Invoke();
    }

    public static void Hide()
    {
        if (!IsVisible) return;
        IsVisible = false;
        _window?.FadeOut();
        UpdateRunning();
        StateChanged?.Invoke();
    }

    /// <summary>
    /// A página de configurações chama isto no Loaded para receber <see cref="Tick"/>
    /// mesmo com o overlay desligado, e descarta no Unloaded.
    /// </summary>
    public static IDisposable Watch()
    {
        _watchers++;
        UpdateRunning();
        return new Watcher();
    }

    /// <summary>Salva as preferências e aplica na hora.</summary>
    public static void SaveSettings()
    {
        Settings.Save();
        _window?.ApplySettings(Settings);
        StateChanged?.Invoke();
    }

    /// <summary>Volta todas as preferências do overlay ao padrão.</summary>
    public static void ResetSettings()
    {
        Settings = new OverlaySettings();
        SaveSettings();
    }

    private sealed class Watcher : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _watchers--;
            UpdateRunning();
        }
    }

    private static void UpdateRunning()
    {
        if (_timer == null) return;
        if (IsVisible || _watchers > 0)
        {
            Frames.Start();
            _timer.Start();
        }
        else
        {
            _timer.Stop();
            Frames.Stop();
        }
    }

    private static void OnTick()
    {
        UpdateTarget();

        var frames = _targetPid != 0 ? Frames.GetStats(_targetPid) : FrameStats.Empty;
        var data = new OverlayData(_targetName, frames, App.Sensors.Latest, Frames.Error, DateTime.Now);
        Latest = data;

        if (IsVisible && _window != null)
        {
            _window.Update(data);
            // Alguns jogos tomam o "sempre no topo" ao abrir; reafirmamos a cada 2 s
            // e acompanhamos o monitor onde o jogo está.
            if (_ticks++ % 8 == 0)
            {
                _window.Follow(_targetHwnd);
                _window.KeepOnTop();
            }
        }
        Tick?.Invoke(data);
    }

    /// <summary>O "jogo" é a janela em primeiro plano, ignorando o próprio Bottleneck Hunter.</summary>
    private static void UpdateTarget()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || pid == OwnPid) return;

        _targetHwnd = hwnd;
        if (pid == _targetPid) return;
        _targetPid = (int)pid;
        _targetName = NameOf(hwnd, _targetPid);
    }

    private static string? NameOf(IntPtr hwnd, int pid)
    {
        var sb = new StringBuilder(128);
        if (GetWindowText(hwnd, sb, sb.Capacity) > 0)
        {
            var title = sb.ToString().Trim();
            if (title.Length > 0) return title.Length > 40 ? title[..40] + "…" : title;
        }

        if (NameCache.TryGetValue(pid, out var cached)) return cached;
        try
        {
            using var p = Process.GetProcessById(pid);
            return NameCache[pid] = p.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static void RegisterHotkey()
    {
        try
        {
            // Janela invisível só para receber mensagens (HWND_MESSAGE).
            var p = new HwndSourceParameters("BottleneckHunterOverlayHotkey") { ParentWindow = new IntPtr(-3), WindowStyle = 0 };
            _hotkeyWindow = new HwndSource(p);
            _hotkeyWindow.AddHook(HotkeyHook);
            if (!RegisterHotKey(_hotkeyWindow.Handle, HotkeyId, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_O))
                HotkeyError = $"O atalho {HotkeyText} já está em uso por outro programa. Use o botão nesta página para ligar o overlay.";
        }
        catch (Exception ex)
        {
            HotkeyError = "Não foi possível registrar o atalho: " + ex.Message;
        }
    }

    private static IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Toggle();
            handled = true;
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
}
