using System.Diagnostics;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace BottleneckHunter.Core.Overlay;

/// <summary>Números de quadros de um processo, calculados na hora.</summary>
/// <param name="Fps">Quadros por segundo, pela média do último segundo.</param>
/// <param name="FrametimeMs">Tempo médio entre quadros no último segundo.</param>
/// <param name="Low1">"1% low": média dos 1% piores quadros dos últimos 10 s, em FPS.</param>
/// <param name="Low01">"0,1% low": o mesmo com os 0,1% piores.</param>
/// <param name="Recent">Últimos tempos entre quadros (ms), do mais antigo ao mais novo, para o gráfico.</param>
public sealed record FrameStats(double? Fps, double? FrametimeMs, double? Low1, double? Low01, double[] Recent)
{
    public static readonly FrameStats Empty = new(null, null, null, null, Array.Empty<double>());
}

/// <summary>
/// Conta os quadros que cada programa apresenta na tela usando ETW (Event Tracing for Windows),
/// o mesmo mecanismo do PresentMon. Não injeta nada no jogo: só escuta eventos que o próprio
/// Windows publica a cada Present(). Por isso é seguro com anti-cheat.
///
/// Fontes, por ordem de preferência para cada processo:
///  1. DXGI (DirectX 10/11/12) e Direct3D 9: um evento por Present() feito pelo jogo.
///  2. DxgKrnl (driver de vídeo do kernel): cobre Vulkan e OpenGL quando o jogo não passa pelo DXGI.
/// Precisa rodar como administrador (o app já roda).
/// </summary>
public sealed class FrameMonitor : IDisposable
{
    private const string SessionName = "BottleneckHunter-Frames";

    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3D9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private static readonly Guid DxgKrnlProvider = new("802EC45A-1E99-4B83-9920-87C98277BA9D");

    // IDs dos eventos (manifestos públicos dos provedores).
    private const int DxgiPresentStart = 42;
    private const int DxgiPresentMpoStart = 55;
    private const int D3D9PresentStart = 1;
    private const int KrnlPresentInfo = 184;
    private const int KrnlPresentHistoryDetailedStart = 214;
    private const int KrnlPresentHistoryStart = 215;

    // Palavras-chave: "Events" + canal analítico; no DxgKrnl, "Base" + "Present" + canal de desempenho.
    private const ulong ApiKeywords = 0x8000000000000001;
    private const ulong KrnlKeywords = 0x4000000008000001;

    /// <summary>Quanto histórico guardar por processo (para o 1% low).</summary>
    private const double KeepMs = 10_000;
    private const int MaxFramesPerQueue = 20_000;

    private readonly object _lock = new();
    private readonly Dictionary<int, ProcFrames> _procs = new();
    private readonly object _sessionLock = new();
    private TraceEventSession? _session;
    private bool _stopRequested;
    private Thread? _thread;
    private double _nowMs;
    private long _nowStamp;
    private double _lastCleanupMs;

    public bool IsRunning { get; private set; }

    /// <summary>Por que não está contando quadros (null quando está tudo certo).</summary>
    public string? Error { get; private set; }

    public void Start()
    {
        if (_thread != null) return;
        Error = null;
        lock (_sessionLock) _stopRequested = false;
        _thread = new Thread(Run) { IsBackground = true, Name = "FrameMonitor" };
        _thread.Start();
    }

    public void Stop()
    {
        var t = _thread;
        if (t == null) return;
        lock (_sessionLock)
        {
            _stopRequested = true;
            try { _session?.Stop(); } catch { }
        }
        t.Join(3000);
        _thread = null;
        lock (_lock)
        {
            _procs.Clear();
            _nowMs = _lastCleanupMs = 0;
            _nowStamp = 0;
        }
    }

    private void Run()
    {
        try
        {
            if (TraceEventSession.IsElevated() != true)
            {
                Error = "Para contar o FPS, abra o Bottleneck Hunter como administrador.";
                return;
            }

            TraceEventSession session;
            lock (_sessionLock)
            {
                if (_stopRequested) return;
                // Se sobrou uma sessão com o mesmo nome (app fechado à força), o construtor a substitui.
                session = new TraceEventSession(SessionName) { StopOnDispose = true };
                _session = session;
            }
            using var _ = session;

            // O filtro por ID faz o próprio Windows descartar o resto: custo quase zero no jogo.
            session.EnableProvider(DxgiProvider, TraceEventLevel.Informational, ApiKeywords,
                Only(DxgiPresentStart, DxgiPresentMpoStart));
            session.EnableProvider(D3D9Provider, TraceEventLevel.Informational, ApiKeywords,
                Only(D3D9PresentStart));
            session.EnableProvider(DxgKrnlProvider, TraceEventLevel.Informational, KrnlKeywords,
                Only(KrnlPresentInfo, KrnlPresentHistoryStart, KrnlPresentHistoryDetailedStart));

            // AllEvents não decodifica o conteúdo: só precisamos de provedor, ID, processo e horário.
            session.Source.AllEvents += OnEvent;
            IsRunning = true;
            session.Source.Process(); // bloqueia até Stop()
        }
        catch (UnauthorizedAccessException)
        {
            Error = "O Windows negou acesso aos eventos de vídeo. Abra o app como administrador.";
        }
        catch (Exception ex)
        {
            Error = "Não foi possível iniciar a contagem de FPS: " + ex.Message;
        }
        finally
        {
            IsRunning = false;
            lock (_sessionLock) _session = null;
        }
    }

    private static TraceEventProviderOptions Only(params int[] ids) => new() { EventIDsToEnable = ids.ToList() };

    private void OnEvent(TraceEvent e)
    {
        var provider = e.ProviderGuid;
        int id = (int)e.ID;
        bool api;

        if (provider == DxgiProvider)
        {
            if (id is not (DxgiPresentStart or DxgiPresentMpoStart)) return;
            api = true;
        }
        else if (provider == D3D9Provider)
        {
            if (id != D3D9PresentStart) return;
            api = true;
        }
        else if (provider == DxgKrnlProvider)
        {
            if (id is not (KrnlPresentInfo or KrnlPresentHistoryStart or KrnlPresentHistoryDetailedStart)) return;
            api = false;
        }
        else return;

        int pid = e.ProcessID;
        double t = e.TimeStampRelativeMSec;
        if (pid <= 0) return;

        lock (_lock)
        {
            if (t > _nowMs)
            {
                _nowMs = t;
                _nowStamp = Stopwatch.GetTimestamp();
            }
            if (!_procs.TryGetValue(pid, out var p)) _procs[pid] = p = new ProcFrames();
            p.Add(api ? -1 : id, t);

            // De vez em quando, esquece processos que pararam de desenhar.
            if (_nowMs - _lastCleanupMs > 5000)
            {
                _lastCleanupMs = _nowMs;
                foreach (var dead in _procs.Where(kv => _nowMs - kv.Value.LastMs > 15_000).Select(kv => kv.Key).ToList())
                    _procs.Remove(dead);
                foreach (var proc in _procs.Values) proc.Trim(_nowMs - KeepMs);
            }
        }
    }

    /// <summary>Calcula FPS, frametime e lows de um processo.</summary>
    public FrameStats GetStats(int pid, int graphFrames = 240)
    {
        double[] times;
        double now;
        lock (_lock)
        {
            if (!_procs.TryGetValue(pid, out var p)) return FrameStats.Empty;
            now = Now();
            times = p.BestSource(now - KeepMs);
        }
        return Compute(times, now, graphFrames);
    }

    /// <summary>
    /// "Agora" no relógio dos eventos. O Windows entrega os eventos em lotes, a cada ~1 s,
    /// então usamos o horário do último evento recebido. Se nada chega há mais de 1,5 s
    /// (jogo e tela parados), o relógio avança sozinho para o FPS cair a zero.
    /// </summary>
    private double Now()
    {
        double sinceLast = Stopwatch.GetElapsedTime(_nowStamp).TotalMilliseconds;
        return _nowStamp == 0 ? _nowMs : _nowMs + Math.Max(0, sinceLast - 1500);
    }

    /// <summary>Conta pura, sem estado: separada para ficar fácil de conferir.</summary>
    public static FrameStats Compute(double[] times, double nowMs, int graphFrames)
    {
        if (times.Length < 2) return FrameStats.Empty;

        var intervals = new double[times.Length - 1];
        for (int i = 1; i < times.Length; i++) intervals[i - 1] = times[i] - times[i - 1];

        // FPS a partir do frametime médio do último segundo. Se o jogo travou, cai para 0 de verdade.
        int inLastSecond = 0;
        double sumLastSecond = 0;
        for (int i = times.Length - 1; i >= 1 && times[i] > nowMs - 1000; i--)
        {
            inLastSecond++;
            sumLastSecond += intervals[i - 1];
        }
        double? frametime = inLastSecond > 0 ? sumLastSecond / inLastSecond : null;
        double? fps = frametime is > 0 ? 1000 / frametime : 0;

        double? low1 = null, low01 = null;
        if (intervals.Length >= 30)
        {
            var sorted = (double[])intervals.Clone();
            Array.Sort(sorted);
            Array.Reverse(sorted); // piores (mais lentos) primeiro
            low1 = 1000 / sorted.Take(Math.Max(1, sorted.Length / 100)).Average();
            low01 = 1000 / sorted.Take(Math.Max(1, sorted.Length / 1000)).Average();
        }

        var recent = intervals.Skip(Math.Max(0, intervals.Length - graphFrames)).ToArray();
        return new FrameStats(fps, frametime, low1, low01, recent);
    }

    public void Dispose() => Stop();

    /// <summary>Horários dos quadros de um processo, separados por fonte.</summary>
    private sealed class ProcFrames
    {
        // Chave -1 = DXGI/D3D9 (somados); demais chaves = ID do evento do kernel.
        private readonly Dictionary<int, Queue<double>> _bySource = new();
        public double LastMs { get; private set; }

        public void Add(int source, double t)
        {
            if (!_bySource.TryGetValue(source, out var q)) _bySource[source] = q = new Queue<double>();
            q.Enqueue(t);
            if (q.Count > MaxFramesPerQueue) q.Dequeue();
            LastMs = t;
        }

        public void Trim(double fromMs)
        {
            foreach (var q in _bySource.Values)
                while (q.Count > 0 && q.Peek() < fromMs) q.Dequeue();
        }

        /// <summary>
        /// Usa os eventos do DirectX se houver; senão, o evento do kernel mais frequente
        /// (um mesmo quadro pode gerar mais de um tipo de evento no kernel, então não somamos).
        /// </summary>
        public double[] BestSource(double fromMs)
        {
            Trim(fromMs);
            if (_bySource.TryGetValue(-1, out var api) && api.Count >= 2) return api.ToArray();
            var best = _bySource.Where(kv => kv.Key != -1).Select(kv => kv.Value).MaxBy(q => q.Count);
            return best?.ToArray() ?? Array.Empty<double>();
        }
    }
}
