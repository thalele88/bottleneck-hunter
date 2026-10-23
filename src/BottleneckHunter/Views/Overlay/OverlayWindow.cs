using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BottleneckHunter.Core.Overlay;

namespace BottleneckHunter.Views.Overlay;

/// <summary>
/// Janela do overlay: transparente, sempre no topo, sem foco e "click-through"
/// (os cliques passam direto para o jogo). Fica num canto do monitor onde o jogo está.
/// Não aparece na barra de tarefas nem no Alt+Tab.
/// </summary>
public sealed class OverlayWindow : Window
{
    private readonly OverlayPanel _panel = new();
    private OverlayCorner _corner = OverlayCorner.TopLeft;
    private IntPtr _hwnd;
    private IntPtr _monitor;

    public OverlayWindow()
    {
        Title = "Bottleneck Hunter: overlay";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        FontFamily = (FontFamily)FindResource("UiFont");
        Content = _panel;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            Reposition();
        };
        SizeChanged += (_, _) => Reposition();
        DpiChanged += (_, _) => Reposition();
    }

    public void ApplySettings(OverlaySettings s)
    {
        _corner = s.Corner;
        _panel.Apply(s);
        if (OverlayService.Latest is { } d) _panel.Update(d);
        Reposition();
    }

    public void Update(OverlayData d) => _panel.Update(d);

    public void FadeIn()
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Show();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    public void FadeOut()
    {
        var anim = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(200));
        anim.Completed += (_, _) =>
        {
            // Se foi religado durante a animação, não esconde.
            if (!OverlayService.IsVisible) Hide();
        };
        BeginAnimation(OpacityProperty, anim);
    }

    /// <summary>Acompanha o monitor onde está a janela do jogo.</summary>
    public void Follow(IntPtr gameWindow)
    {
        var mon = MonitorFromWindow(gameWindow, MONITOR_DEFAULTTOPRIMARY);
        if (mon == _monitor) return;
        _monitor = mon;
        Reposition();
    }

    /// <summary>Volta a ficar por cima, caso o jogo tenha tomado o topo.</summary>
    public void KeepOnTop()
    {
        if (_hwnd != IntPtr.Zero)
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Encosta a janela no canto escolhido, em pixels reais do monitor.</summary>
    private void Reposition()
    {
        if (_hwnd == IntPtr.Zero) return;
        var mon = _monitor != IntPtr.Zero ? _monitor : MonitorFromWindow(_hwnd, MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(mon, ref info)) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        int w = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        int h = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        int margin = (int)(10 * dpi.DpiScaleX);
        var r = info.rcMonitor;

        bool right = _corner is OverlayCorner.TopRight or OverlayCorner.BottomRight;
        bool bottom = _corner is OverlayCorner.BottomLeft or OverlayCorner.BottomRight;
        int x = right ? r.Right - w - margin : r.Left + margin;
        int y = bottom ? r.Bottom - h - margin : r.Top + margin;
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
    private const uint MONITOR_DEFAULTTOPRIMARY = 1;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);
}
