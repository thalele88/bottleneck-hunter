using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BottleneckHunter.Core.Bench;

namespace BottleneckHunter.Views.Bench;

/// <summary>Resultado do benchmark de GPU (modo com tempo fixo).</summary>
public sealed record GpuBenchOutcome(double AvgFps, float? MaxTemp, string? Error, bool DeviceLost, bool Cancelled);

/// <summary>
/// Janela do teste de placa de vídeo. No modo benchmark roda por um tempo fixo e calcula o FPS médio;
/// no modo stress fica rodando até o teste de estabilidade terminar.
/// </summary>
public partial class GpuStressWindow : Window
{
    /// <summary>Passos de ray marching por pixel: define o peso de cada quadro.</summary>
    public const int Steps = 160;
    private static readonly TimeSpan Warmup = TimeSpan.FromSeconds(3);

    private readonly int _width, _height;
    private readonly TimeSpan? _benchTime;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private D3DHost? _host;
    private long _warmFrames = -1;
    private double _warmSeconds;
    private float? _maxTemp;
    private bool _finished;

    public GpuRenderer? Renderer { get; private set; }

    /// <summary>A GPU começou a desenhar.</summary>
    public event Action<GpuRenderer>? RendererReady;
    /// <summary>Fim do modo benchmark (ou janela fechada antes da hora).</summary>
    public event Action<GpuBenchOutcome>? BenchFinished;

    /// <param name="benchTime">Com valor, é benchmark com tempo fixo; nulo, é stress sem fim.</param>
    public GpuStressWindow(int width, int height, TimeSpan? benchTime)
    {
        InitializeComponent();
        _width = width;
        _height = height;
        _benchTime = benchTime;
        _timer.Tick += (_, _) => Tick();
        Loaded += OnLoaded;
        Closing += (_, _) => Shutdown();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _host = new D3DHost();
        _host.Loaded += (_, _) => StartRenderer();
        HostArea.Children.Add(_host);
        if (_benchTime != null) ProgressTrack.Visibility = Visibility.Visible;
        _timer.Start();
    }

    private void StartRenderer()
    {
        if (Renderer != null || _host == null || _host.Handle == IntPtr.Zero) return;
        Renderer = new GpuRenderer(_host.Handle, _width, _height, Steps);
        Renderer.Started += () => Dispatcher.BeginInvoke(() =>
        {
            StatusText.Text = _benchTime != null
                ? $"Medindo {Renderer.AdapterName} em {_width} × {_height}. Não mexa no PC até terminar."
                : $"Stress em {Renderer.AdapterName} ({_width} × {_height}). Fique de olho na temperatura; feche esta janela para parar.";
            if (Renderer != null) RendererReady?.Invoke(Renderer);
        });
        Renderer.Failed += msg => Dispatcher.BeginInvoke(() =>
        {
            StatusText.Text = "✖ " + msg;
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Bad");
            if (_benchTime != null) Finish();
        });
        Renderer.Start();
    }

    private void Tick()
    {
        var s = App.Sensors.Latest;
        TempText.Text = s.GpuTemp is { } t ? $"{t:0} °C" : "—";
        LoadText.Text = s.GpuLoad is { } l ? $"{l:0}%" : "—";
        PowerText.Text = s.GpuPower is { } p ? $"{p:0} W" : "—";
        if (s.GpuTemp is { } temp) _maxTemp = Math.Max(_maxTemp ?? 0, temp);

        if (Renderer == null) StartRenderer();
        var r = Renderer;
        if (r == null || !r.Running) return;

        FpsText.Text = r.Fps > 0 ? r.Fps.ToString("0") : "—";
        var el = r.Elapsed;
        TimeText.Text = $"{(int)el.TotalMinutes}:{el.Seconds:00}";

        if (_benchTime is { } total)
        {
            if (_warmFrames < 0 && el >= Warmup)
            {
                _warmFrames = r.Frames;
                _warmSeconds = el.TotalSeconds;
            }
            double frac = Math.Min(1, el.TotalSeconds / total.TotalSeconds);
            ((ScaleTransform)ProgressFill.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(frac, TimeSpan.FromMilliseconds(240)));
            if (el >= total) Finish();
        }
    }

    /// <summary>Termina o benchmark, calcula o FPS médio (sem os primeiros segundos de aquecimento) e fecha.</summary>
    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        var r = Renderer;
        double avg = 0;
        if (r != null && _warmFrames >= 0)
        {
            double secs = r.Elapsed.TotalSeconds - _warmSeconds;
            if (secs > 1) avg = (r.Frames - _warmFrames) / secs;
        }
        bool cancelled = _benchTime is { } total && (r == null || r.Elapsed < total) && r?.Error == null;
        Shutdown();
        BenchFinished?.Invoke(new GpuBenchOutcome(avg, _maxTemp, r?.Error, r?.DeviceLost == true, cancelled));
        if (IsLoaded) Close();
    }

    private void Shutdown()
    {
        _timer.Stop();
        Renderer?.Stop();
        if (_benchTime != null && !_finished)
        {
            _finished = true;
            BenchFinished?.Invoke(new GpuBenchOutcome(0, _maxTemp, Renderer?.Error, Renderer?.DeviceLost == true, Cancelled: true));
        }
    }

    private void OnStop(object sender, RoutedEventArgs e) => Close();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}
