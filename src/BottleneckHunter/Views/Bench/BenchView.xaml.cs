using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using BottleneckHunter.Controls;
using BottleneckHunter.Core;
using BottleneckHunter.Core.Bench;
using Microsoft.Win32;

namespace BottleneckHunter.Views.Bench;

public partial class BenchView : UserControl
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    private CancellationTokenSource? _benchCts;
    private bool _busy;
    private readonly ScoreBar[] _single = new ScoreBar[CpuBenchmark.Tests.Length];
    private readonly ScoreBar[] _multi = new ScoreBar[CpuBenchmark.Tests.Length];
    private readonly ScoreBar[] _ram = new ScoreBar[MemoryBenchmark.Steps.Length];
    private readonly ScoreBar[] _disk = new ScoreBar[DiskBenchmark.Steps.Length];
    private RamModules? _modules;

    // Fractal desenhado pela CPU durante o benchmark.
    private readonly WriteableBitmap _fractalBmp = new(CpuBenchmark.PreviewW, CpuBenchmark.PreviewH, 96, 96, PixelFormats.Bgra32, null);
    private readonly int[] _fractalIters = new int[CpuBenchmark.PreviewW * CpuBenchmark.PreviewH];
    private readonly int[] _fractalPixels = new int[CpuBenchmark.PreviewW * CpuBenchmark.PreviewH];
    private readonly int[] _palette = BuildPalette();
    private readonly DispatcherTimer _fractalTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private int _lastFrame = -1;

    // Teste de estabilidade.
    private StressSession? _session;
    private GpuStressWindow? _gpuWindow;
    private readonly DispatcherTimer _stressTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Queue<double?> _cpuTemps = new();
    private readonly Queue<double?> _gpuTemps = new();

    private bool _animatedIn;

    public BenchView()
    {
        InitializeComponent();
        BuildBars();
        Fractal.Source = _fractalBmp;
        _fractalTimer.Tick += (_, _) => DrawFractal();
        _stressTimer.Tick += (_, _) => StressTick();
        DrawFractalIdle();

        CpuName.Text = CpuModel();
        CpuThreadsText.Text = $"{Environment.ProcessorCount} threads lógicas · conjunto de instruções do stress: {CpuStress.HeavyKind}";
        CpuMultiLabel.Text = $"Todos os núcleos ({Environment.ProcessorCount} threads)";
        RamInfo.Text = $"{TotalRamGb():0} GB instalados";
        _ = LoadRamModulesAsync();
        FillDrives();
        OnLimitChanged(this, new RoutedPropertyChangedEventArgs<double>(0, 0));
        OnRamPercentChanged(this, new RoutedPropertyChangedEventArgs<double>(0, 0));

        Loaded += (_, _) =>
        {
            GpuName.Text = App.Sensors.Latest.GpuName ?? "Placa de vídeo principal";
            ShowLastResults();
            if (!_animatedIn)
            {
                _animatedIn = true;
                StaggerIn(PerfPanel);
            }
        };
    }

    // ================================================================ Montagem

    private void BuildBars()
    {
        for (int i = 0; i < CpuBenchmark.Tests.Length; i++)
        {
            var t = CpuBenchmark.Tests[i];
            var title = new TextBlock { Text = t.Name, FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, i == 0 ? 4 : 10, 0, 2), ToolTip = t.Explain };
            title.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            var explain = new TextBlock { Text = t.Explain, Style = (Style)FindResource("Dim"), FontSize = 11.5, Margin = new Thickness(0, 0, 0, 2) };
            _single[i] = new ScoreBar { Label = "1 núcleo", BrushKey = "Cpu", LabelWidth = 120 };
            _multi[i] = new ScoreBar { Label = "todos", BrushKey = "Accent", LabelWidth = 120 };
            _single[i].Reset();
            _multi[i].Reset();
            CpuBars.Children.Add(title);
            CpuBars.Children.Add(explain);
            CpuBars.Children.Add(_single[i]);
            CpuBars.Children.Add(_multi[i]);
        }

        string[] ramTips =
        {
            "Quantos gigabytes por segundo a CPU consegue ler da RAM.",
            "Quantos gigabytes por segundo a CPU consegue gravar na RAM.",
            "Ler e gravar ao mesmo tempo, como ao mover dados de um lugar para outro.",
            "Tempo de espera por um dado que não está no cache da CPU. Aqui, menor é melhor. Jogos sentem muito isso.",
        };
        for (int i = 0; i < _ram.Length; i++)
        {
            _ram[i] = new ScoreBar { Label = MemoryBenchmark.Steps[i], BrushKey = i == 3 ? "Warn" : "Cpu", ToolTip = ramTips[i] };
            _ram[i].Reset();
            RamBars.Children.Add(_ram[i]);
        }

        string[] diskTips =
        {
            "Gravar um arquivo grande de uma vez (instalar um jogo, copiar um vídeo).",
            "Ler um arquivo grande de uma vez (abrir um vídeo, copiar para outro disco).",
            "Ler pedacinhos de 4 KB espalhados pelo disco, um de cada vez: é o que acontece ao ligar o PC e abrir programas.",
            "Gravar pedacinhos de 4 KB espalhados: atualizações do Windows, navegadores, salvamentos de jogos.",
        };
        for (int i = 0; i < _disk.Length; i++)
        {
            _disk[i] = new ScoreBar { Label = DiskBenchmark.Steps[i], BrushKey = i < 2 ? "Accent" : "Good", ToolTip = diskTips[i] };
            _disk[i].Reset();
            DiskBars.Children.Add(_disk[i]);
        }
    }

    private static string CpuModel()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key?.GetValue("ProcessorNameString") is string name && name.Length < 200)
                return name.Trim();
        }
        catch
        {
            // Sem acesso ao registro: mostramos um nome genérico.
        }
        return "Processador";
    }

    private static double TotalRamGb() => MemoryInfo.TotalMb / 1024.0;

    private async Task LoadRamModulesAsync()
    {
        _modules = await Task.Run(RamModules.Read);
        if (_modules == null) return;
        string type = _modules.Type.Length > 0 ? _modules.Type + " " : "";
        string sticks = _modules.Count == 1 ? "1 pente" : $"{_modules.Count} pentes";
        RamInfo.Text = $"{TotalRamGb():0} GB {type}a {_modules.ConfiguredMts} MT/s · {sticks} · teórico até {F(_modules.TheoreticalGBs, "0.0")} GB/s";
    }

    private void FillDrives()
    {
        DriveBox.Items.Clear();
        foreach (var d in DiskBenchmark.Drives(256L * 1024 * 1024))
        {
            string label;
            try { label = d.VolumeLabel; }
            catch { label = ""; }
            string text = $"{d.Name.TrimEnd('\\')}  {(label.Length > 0 && label.Length < 40 ? label : "Disco local")} ({F(d.AvailableFreeSpace / 1073741824.0, "0")} GB livres)";
            DriveBox.Items.Add(new ComboBoxItem { Content = text, Tag = d });
        }
        if (DriveBox.Items.Count > 0) DriveBox.SelectedIndex = 0;
    }

    private static string F(double v, string format) => v.ToString(format, Br);

    // ================================================================ Abas e animações

    private void OnTab(object sender, RoutedEventArgs e)
    {
        if (PerfPanel == null) return; // ainda montando a página
        var target = sender == TabStress ? StressPanel : sender == TabHistory ? HistoryPanel : PerfPanel;
        foreach (var p in new[] { PerfPanel, StressPanel, HistoryPanel })
            p.Visibility = p == target ? Visibility.Visible : Visibility.Collapsed;
        if (target == HistoryPanel) RefreshHistory();
        StaggerIn(target);
    }

    /// <summary>Os cartões entram um depois do outro, subindo e aparecendo.</summary>
    private static void StaggerIn(Panel panel)
    {
        int i = 0;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        foreach (UIElement child in panel.Children)
        {
            if (child.Visibility != Visibility.Visible) continue;
            var delay = TimeSpan.FromMilliseconds(60 * i++);
            child.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)) { BeginTime = delay });

            // Cartões com o estilo Card já usam o deslocamento para o efeito de passar o mouse: nesses, só o fade.
            TranslateTransform? move = null;
            var local = child.ReadLocalValue(RenderTransformProperty);
            if (local is TranslateTransform t && !t.IsFrozen)
                move = t;
            else if (local == DependencyProperty.UnsetValue && (child.RenderTransform == null || child.RenderTransform == Transform.Identity))
                child.RenderTransform = move = new TranslateTransform();
            move?.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(420)) { BeginTime = delay, EasingFunction = ease });
        }
    }

    private static void ShowHint(Border box, TextBlock text, string message, string brush = "Track")
    {
        text.Text = message;
        box.SetResourceReference(Border.BackgroundProperty, brush);
        if (box.Visibility != Visibility.Visible)
        {
            box.Visibility = Visibility.Visible;
            box.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)));
        }
    }

    /// <summary>Número que "conta" até o valor final.</summary>
    private static void CountUp(TextBlock tb, double to, string format)
    {
        tb.Tag = format;
        tb.BeginAnimation(NumberProperty, new DoubleAnimation(0, to, TimeSpan.FromMilliseconds(900))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    private static readonly DependencyProperty NumberProperty = DependencyProperty.RegisterAttached(
        "Number", typeof(double), typeof(BenchView),
        new PropertyMetadata(0.0, (d, e) =>
        {
            if (d is TextBlock tb) tb.Text = ((double)e.NewValue).ToString(tb.Tag as string ?? "N0", Br);
        }));

    private static void Animate(Border fill, double fraction)
    {
        ((ScaleTransform)fill.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(Math.Clamp(fraction, 0, 1), TimeSpan.FromMilliseconds(250)));
    }

    // ================================================================ Benchmarks

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        foreach (var b in new[] { RunAllButton, CpuButton, RamButton, DiskButton, GpuButton })
            b.IsEnabled = !busy;
        StressButton.IsEnabled = !busy || _session != null;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        RunStatus.Visibility = status != null ? Visibility.Visible : Visibility.Collapsed;
        RunStatus.Text = status ?? "";
    }

    private async Task RunGuarded(Func<CancellationToken, Task> work, string status)
    {
        if (_busy || _session != null) return;
        _benchCts = new CancellationTokenSource();
        SetBusy(true, status);
        try
        {
            await work(_benchCts.Token);
            SetBusy(false);
        }
        catch (OperationCanceledException)
        {
            SetBusy(false, "Teste cancelado.");
        }
        catch (Exception ex)
        {
            SetBusy(false, "Não foi possível terminar o teste: " + ex.Message);
        }
        finally
        {
            _benchCts.Dispose();
            _benchCts = null;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => _benchCts?.Cancel();

    private async void OnRunAll(object sender, RoutedEventArgs e) => await RunGuarded(async ct =>
    {
        RunStatus.Text = "1 de 3 · Processador";
        await RunCpu(ct);
        RunStatus.Text = "2 de 3 · Memória RAM";
        await RunRam(ct);
        RunStatus.Text = "3 de 3 · Disco";
        await RunDisk(ct);
        RunStatus.Text = "Pronto! Veja os resultados abaixo e compare depois na aba Histórico.";
    }, "Começando...");

    private async void OnRunCpu(object sender, RoutedEventArgs e) => await RunGuarded(RunCpu, "Testando o processador (cerca de 25 segundos)...");
    private async void OnRunRam(object sender, RoutedEventArgs e) => await RunGuarded(RunRam, "Testando a memória RAM (cerca de 10 segundos)...");
    private async void OnRunDisk(object sender, RoutedEventArgs e) => await RunGuarded(RunDisk, "Testando o disco...");

    // ---------------------------------------------------------------- CPU

    private async Task RunCpu(CancellationToken ct)
    {
        int n = CpuBenchmark.Tests.Length;
        foreach (var b in _single.Concat(_multi)) b.Reset();
        CpuSingle.Text = CpuMulti.Text = CpuScaling.Text = "…";
        CpuHint.Visibility = Visibility.Collapsed;
        CpuProgressTrack.Visibility = Visibility.Visible;
        FractalBox.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(400)));
        FractalCaption.Text = "Aguardando o teste de ponto flutuante...";
        _fractalTimer.Start();

        int threads = Environment.ProcessorCount;
        double singleScale = 2500, multiScale = 2500 * Math.Max(1, threads * 0.6);
        var liveSingle = new double[n];
        var liveMulti = new double[n];
        bool singleShown = false;

        var progress = new Progress<CpuBenchProgress>(p =>
        {
            Animate(CpuProgressFill, p.Overall);
            string phase = p.Multi ? $"todos os {threads} núcleos" : "1 núcleo";
            RunStatus.Text = $"Testando {CpuBenchmark.Tests[p.TestIndex].Name.ToLowerInvariant()} com {phase}...";
            FractalCaption.Text = p.TestIndex == 1
                ? $"A CPU está desenhando este fractal agora ({phase})."
                : "Durante o teste de ponto flutuante a CPU desenha este fractal ao vivo.";
            if (p.Multi)
            {
                liveMulti[p.TestIndex] = p.LiveScore;
                _multi[p.TestIndex].Set(p.LiveScore / multiScale, F(p.LiveScore, "N0"));
                if (!singleShown)
                {
                    singleShown = true;
                    CountUp(CpuSingle, Math.Exp(liveSingle.Average(Math.Log)), "N0");
                }
            }
            else
            {
                liveSingle[p.TestIndex] = p.LiveScore;
                _single[p.TestIndex].Set(p.LiveScore / singleScale, F(p.LiveScore, "N0"));
            }
        });

        try
        {
            var r = await Task.Run(() => CpuBenchmark.Run(progress, ct), ct);

            // Reescala as barras para o maior valor de cada tipo: dá para comparar os testes entre si.
            double maxS = r.Single.Max() * 1.1, maxM = r.Multi.Max() * 1.1;
            for (int i = 0; i < n; i++)
            {
                _single[i].Set(r.Single[i] / maxS, F(r.Single[i], "N0"));
                _multi[i].Set(r.Multi[i] / maxM, F(r.Multi[i], "N0"));
            }
            CountUp(CpuSingle, r.SingleScore, "N0");
            CountUp(CpuMulti, r.MultiScore, "N0");
            CountUp(CpuScaling, r.Scaling, "0.0'×'");

            var last = BenchHistory.Last("cpu");
            string compare = last != null && last.Main > 0
                ? $" Em relação ao teste de {last.When.ToString("dd/MM", Br)}: {Delta(r.MultiScore, last.Main)}."
                : "";

            string text = $"Quanto maior, melhor. A nota de 1 núcleo é a que mais pesa em jogos; a de todos os núcleos pesa em edição de vídeo, " +
                          $"compactação e programas profissionais. Com {threads} threads, seu processador rende {F(r.Scaling, "0.0")} vezes mais que um núcleo sozinho.{compare}";
            if (!r.ResultsOk)
                ShowHint(CpuHint, CpuHintText, "⚠ A CPU errou contas durante o teste! Isso indica instabilidade (overclock, undervolt, temperatura ou fonte). " +
                                               "Rode o teste de estabilidade na aba ao lado para confirmar.", "AccentSoft");
            else
                ShowHint(CpuHint, CpuHintText, text);

            BenchHistory.Add(new BenchRecord
            {
                Kind = "cpu",
                When = DateTime.Now,
                Main = Math.Round(r.MultiScore),
                MainUnit = "pts",
                Summary = $"1 núcleo {F(r.SingleScore, "N0")} · todos {F(r.MultiScore, "N0")} ({threads} threads)" + (r.ResultsOk ? "" : " · com erros"),
            });
        }
        finally
        {
            _fractalTimer.Stop();
            CpuProgressTrack.Visibility = Visibility.Collapsed;
            FractalBox.BeginAnimation(OpacityProperty, new DoubleAnimation(0.6, TimeSpan.FromMilliseconds(400)));
            FractalCaption.Text = "Este é o último quadro que a CPU desenhou no teste.";
        }
    }

    private static string Delta(double now, double before)
    {
        double pct = (now - before) / before * 100;
        return Math.Abs(pct) < 2 ? "praticamente igual (diferenças de até 2% são normais)"
            : pct > 0 ? $"{pct.ToString("0", Br)}% melhor"
            : $"{(-pct).ToString("0", Br)}% pior";
    }

    private static int[] BuildPalette()
    {
        // Do azul escuro ao laranja do app, passando pelo amarelo até quase branco.
        var stops = new (double t, Color c)[]
        {
            (0.00, Color.FromRgb(0x0E, 0x12, 0x24)),
            (0.25, Color.FromRgb(0x1C, 0x4E, 0x9A)),
            (0.55, Color.FromRgb(0xFF, 0x7A, 0x1A)),
            (0.80, Color.FromRgb(0xFF, 0xD3, 0x5C)),
            (1.00, Color.FromRgb(0xFF, 0xF6, 0xE6)),
        };
        var pal = new int[256];
        for (int i = 0; i < 256; i++)
        {
            double t = i / 255.0;
            int k = 0;
            while (k < stops.Length - 2 && t > stops[k + 1].t) k++;
            var (t0, c0) = stops[k];
            var (t1, c1) = stops[k + 1];
            double f = (t - t0) / (t1 - t0);
            byte L(byte a, byte b) => (byte)(a + (b - a) * f);
            pal[i] = unchecked((int)0xFF000000) | (L(c0.R, c1.R) << 16) | (L(c0.G, c1.G) << 8) | L(c0.B, c1.B);
        }
        return pal;
    }

    private void DrawFractal()
    {
        int frame = CpuBenchmark.PreviewFrame;
        if (frame == _lastFrame || frame == 0) return;
        _lastFrame = frame;
        CpuBenchmark.CopyPreview(_fractalIters);
        PaintFractal();
    }

    /// <summary>Antes do primeiro teste, a caixa mostra um quadro calculado na hora (bem pequeno, é instantâneo).</summary>
    private void DrawFractalIdle()
    {
        const int w = CpuBenchmark.PreviewW, h = CpuBenchmark.PreviewH;
        double scale = 3.0 / w;
        for (int py = 0; py < h; py++)
        for (int px = 0; px < w; px++)
        {
            double x0 = -0.6 + (px - w / 2) * scale, y0 = (py - h / 2) * scale;
            double x = 0, y = 0;
            int it = 0;
            while (x * x + y * y <= 4 && it < 200)
            {
                (x, y) = (x * x - y * y + x0, 2 * x * y + y0);
                it++;
            }
            _fractalIters[py * w + px] = it;
        }
        PaintFractal();
    }

    private void PaintFractal()
    {
        for (int i = 0; i < _fractalIters.Length; i++)
        {
            int it = _fractalIters[i];
            _fractalPixels[i] = it >= 200 ? unchecked((int)0xFF0B0D14) : _palette[(int)(Math.Sqrt(it / 200.0) * 255)];
        }
        _fractalBmp.WritePixels(new Int32Rect(0, 0, CpuBenchmark.PreviewW, CpuBenchmark.PreviewH), _fractalPixels, CpuBenchmark.PreviewW * 4, 0);
    }

    // ---------------------------------------------------------------- RAM

    private async Task RunRam(CancellationToken ct)
    {
        foreach (var b in _ram) b.Reset();
        RamHint.Visibility = Visibility.Collapsed;
        double theo = _modules?.TheoreticalGBs ?? 0;
        double scale = theo > 0 ? theo : 80;

        var progress = new Progress<(int Step, double Frac)>(p =>
        {
            RunStatus.Text = $"Memória: medindo {MemoryBenchmark.Steps[p.Step].ToLowerInvariant()}...";
        });
        IProgress<(int, double)> rep = progress;
        var r = await Task.Run(() => MemoryBenchmark.Run((s, f) => rep.Report((s, f)), ct), ct);

        scale = Math.Max(scale, Math.Max(r.ReadGBs, Math.Max(r.WriteGBs, r.CopyGBs)) * 1.05);
        _ram[0].Set(r.ReadGBs / scale, $"{F(r.ReadGBs, "0.0")} GB/s");
        _ram[1].Set(r.WriteGBs / scale, $"{F(r.WriteGBs, "0.0")} GB/s");
        _ram[2].Set(r.CopyGBs / scale, $"{F(r.CopyGBs, "0.0")} GB/s");
        _ram[3].BrushKey = r.LatencyNs < 85 ? "Good" : r.LatencyNs < 115 ? "Warn" : "Bad";
        _ram[3].Set(r.LatencyNs / 150, $"{F(r.LatencyNs, "0.0")} ns");

        var tips = new List<string>();
        if (theo > 0)
            tips.Add($"A leitura chegou a {F(r.ReadGBs / theo * 100, "0")}% do máximo teórico da sua memória ({F(theo, "0.0")} GB/s). Entre 60% e 90% é o normal.");
        if (_modules is { } m)
        {
            if (m.Count == 1)
                tips.Add("⚠ Só há 1 pente de memória: ela funciona em single channel, com metade da velocidade possível. " +
                         "Colocar um segundo pente igual (dual channel) costuma dar um bom ganho de FPS em jogos que dependem da CPU e, principalmente, em vídeo integrado.");
            if (m.RatedMts > m.ConfiguredMts + 100)
                tips.Add($"⚠ Seus pentes informam suportar {m.RatedMts} MT/s, mas estão rodando a {m.ConfiguredMts} MT/s. Ative o XMP (Intel) ou EXPO/DOCP (AMD) na BIOS.");
            else if ((m.Type == "DDR4" && m.ConfiguredMts <= 2666) || (m.Type == "DDR5" && m.ConfiguredMts <= 4800))
                tips.Add($"A memória está a {m.ConfiguredMts} MT/s, a velocidade padrão de fábrica. Se o seu pente foi vendido como mais rápido, ative o XMP/EXPO na BIOS para ganhar desempenho de graça.");
        }
        tips.Add("Latência é o tempo que a CPU espera por um dado que não está no cache. Aqui, quanto menor, melhor; jogos sentem muito isso.");
        ShowHint(RamHint, RamHintText, string.Join("\n\n", tips));

        BenchHistory.Add(new BenchRecord
        {
            Kind = "ram",
            When = DateTime.Now,
            Main = Math.Round(r.ReadGBs, 1),
            MainUnit = "GB/s",
            Summary = $"Leitura {F(r.ReadGBs, "0.0")} · escrita {F(r.WriteGBs, "0.0")} · cópia {F(r.CopyGBs, "0.0")} GB/s · latência {F(r.LatencyNs, "0")} ns",
        });
    }

    // ---------------------------------------------------------------- Disco

    private async Task RunDisk(CancellationToken ct)
    {
        if ((DriveBox.SelectedItem as ComboBoxItem)?.Tag is not DriveInfo drive)
        {
            ShowHint(DiskHint, DiskHintText, "Nenhuma unidade com espaço livre suficiente para o teste.");
            return;
        }
        long size = DiskSizeBox.SelectedIndex switch { 0 => 256L, 2 => 4096L, _ => 1024L } * 1024 * 1024;
        if (drive.AvailableFreeSpace < size * 2)
        {
            ShowHint(DiskHint, DiskHintText, "Pouco espaço livre nessa unidade para esse tamanho de teste. Escolha um tamanho menor.");
            return;
        }

        foreach (var b in _disk) b.Reset();
        DiskHint.Visibility = Visibility.Collapsed;

        var progress = new Progress<(int Step, double Frac)>(p =>
        {
            RunStatus.Text = $"Disco {drive.Name.TrimEnd('\\')}: {DiskBenchmark.Steps[p.Step].ToLowerInvariant()} ({F(p.Frac * 100, "0")}%)...";
            _disk[p.Step].Set(p.Frac, "…", animate: false);
        });
        IProgress<(int, double)> rep = progress;
        var r = await Task.Run(() => DiskBenchmark.Run(drive, size, (s, f) => rep.Report((s, f)), ct), ct);

        double seqScale = Math.Max(600, Math.Max(r.SeqReadMBs, r.SeqWriteMBs) * 1.1);
        double randScale = Math.Max(60, Math.Max(r.RandReadMBs, r.RandWriteMBs) * 1.1);
        _disk[0].Set(r.SeqWriteMBs / seqScale, $"{F(r.SeqWriteMBs, "N0")} MB/s");
        _disk[1].Set(r.SeqReadMBs / seqScale, $"{F(r.SeqReadMBs, "N0")} MB/s");
        _disk[2].Set(r.RandReadMBs / randScale, $"{F(r.RandReadMBs, "0.0")} MB/s");
        _disk[3].Set(r.RandWriteMBs / randScale, $"{F(r.RandWriteMBs, "0.0")} MB/s");
        _disk[2].ToolTip = $"{F(r.RandReadIops, "N0")} operações por segundo (IOPS)";
        _disk[3].ToolTip = $"{F(r.RandWriteIops, "N0")} operações por segundo (IOPS)";

        string kind;
        string verdict;
        if (r.SeqReadMBs < 300 && r.RandReadIops < 1000)
        {
            kind = "HD";
            verdict = "Isso parece um HD mecânico (disco que gira). Ele é ótimo para guardar fotos e vídeos, mas lento para o Windows e jogos: " +
                      "o PC demora para ligar e os jogos podem engasgar ao carregar mapas. Instalar o Windows e os jogos num SSD é a melhoria mais perceptível que existe.";
        }
        else if (r.SeqReadMBs < 700)
        {
            kind = "SSD SATA";
            verdict = "Velocidade de SSD SATA. Ótimo para o dia a dia e jogos; um SSD NVMe carrega um pouco mais rápido, mas a diferença em jogos é pequena.";
        }
        else
        {
            kind = "SSD NVMe";
            verdict = "Velocidade de SSD NVMe: excelente. O disco não é gargalo para jogos nem para o Windows.";
        }
        if (kind != "HD" && r.RandReadMBs < 15)
            verdict += "\n\n⚠ A leitura aleatória 4K ficou baixa para um SSD. Causas comuns: SSD quase cheio (deixe 15% livre), TRIM desligado ou modelo sem cache DRAM.";

        ShowHint(DiskHint, DiskHintText, verdict);
        BenchHistory.Add(new BenchRecord
        {
            Kind = "disk",
            When = DateTime.Now,
            Main = Math.Round(r.SeqReadMBs),
            MainUnit = "MB/s",
            Summary = $"{r.Drive.TrimEnd('\\')} ({kind}) · leitura {F(r.SeqReadMBs, "N0")} · escrita {F(r.SeqWriteMBs, "N0")} MB/s · 4K {F(r.RandReadMBs, "0.0")} MB/s",
        });
    }

    // ---------------------------------------------------------------- GPU

    private void OnRunGpu(object sender, RoutedEventArgs e)
    {
        if (_busy || _session != null) return;
        SetBusy(true, "Testando a placa de vídeo em outra janela (30 segundos)...");
        CancelButton.Visibility = Visibility.Collapsed; // para cancelar, basta fechar a janela do teste
        GpuHint.Visibility = Visibility.Collapsed;
        GpuFps.Text = GpuMaxTemp.Text = "…";

        var win = new GpuStressWindow(1920, 1080, TimeSpan.FromSeconds(30)) { Owner = Window.GetWindow(this) };
        win.BenchFinished += outcome => Dispatcher.BeginInvoke(() => ShowGpuResult(outcome, win.Renderer?.AdapterName));
        win.Show();
    }

    private void ShowGpuResult(GpuBenchOutcome o, string? adapter)
    {
        SetBusy(false);
        if (!string.IsNullOrEmpty(adapter)) GpuName.Text = adapter;
        GpuMaxTemp.Text = o.MaxTemp is { } t ? $"{t:0} °C" : "—";

        if (o.DeviceLost)
        {
            GpuFps.Text = "✖";
            ShowHint(GpuHint, GpuHintText, "⚠ O driver da placa de vídeo travou durante o teste e o Windows precisou reiniciá-lo. Causas comuns: overclock da GPU, " +
                                           "fonte fraca, superaquecimento ou driver com problema. Volte a GPU para o clock padrão e atualize o driver.", "AccentSoft");
            return;
        }
        if (o.Error != null)
        {
            GpuFps.Text = "—";
            ShowHint(GpuHint, GpuHintText, o.Error);
            return;
        }
        if (o.Cancelled || o.AvgFps <= 0)
        {
            GpuFps.Text = "—";
            ShowHint(GpuHint, GpuHintText, "Teste interrompido antes do fim. Rode de novo e deixe a janela aberta por 30 segundos.");
            return;
        }

        CountUp(GpuFps, o.AvgFps, "0.0");
        var last = BenchHistory.Last("gpu");
        string compare = last != null && last.Main > 0 ? $" Em relação ao teste de {last.When.ToString("dd/MM", Br)}: {Delta(o.AvgFps, last.Main)}." : "";
        string temp = o.MaxTemp is >= 85
            ? " A temperatura passou de 85 °C: vale limpar a poeira e conferir as ventoinhas."
            : "";
        ShowHint(GpuHint, GpuHintText,
            "Quanto mais FPS, mais forte a placa. Este fractal é bem mais pesado que um jogo, então não compare com o FPS dos seus jogos: " +
            "use o número para comparar placas de vídeo e o mesmo PC antes e depois de um ajuste (driver novo, overclock, limpeza)." + temp + compare);

        BenchHistory.Add(new BenchRecord
        {
            Kind = "gpu",
            When = DateTime.Now,
            Main = Math.Round(o.AvgFps, 1),
            MainUnit = "FPS",
            Summary = $"{adapter ?? "GPU"} · {F(o.AvgFps, "0.0")} FPS em 1920 × 1080" + (o.MaxTemp is { } mt ? $" · máx. {mt:0} °C" : ""),
        });
    }

    /// <summary>Mostra os últimos resultados salvos quando a página abre, para o usuário não começar do zero.</summary>
    private void ShowLastResults()
    {
        if (_busy || GpuFps.Text != "—") return;
        if (BenchHistory.Last("gpu") is { } g)
        {
            GpuFps.Text = F(g.Main, "0.0");
            GpuFps.ToolTip = $"Último teste: {g.When.ToString("dd/MM/yyyy HH:mm", Br)}";
        }
    }

    // ================================================================ Estabilidade

    private void OnLimitChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CpuLimitText == null || GpuLimitText == null) return;
        CpuLimitText.Text = $"{CpuLimit.Value:0} °C";
        GpuLimitText.Text = $"{GpuLimit.Value:0} °C";
    }

    private void OnRamPercentChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RamPercentText == null) return;
        long mb = RamStress.FreeMb() * (long)RamPercent.Value / 100;
        RamPercentText.Text = $"{RamPercent.Value:0}% da RAM livre agora (≈ {F(mb / 1024.0, "0.0")} GB)";
    }

    private TimeSpan? SelectedDuration()
    {
        foreach (var rb in DurationPanel.Children.OfType<RadioButton>())
            if (rb.IsChecked == true && int.TryParse(rb.Tag as string, out int min))
                return min > 0 ? TimeSpan.FromMinutes(min) : null;
        return TimeSpan.FromMinutes(5);
    }

    private void OnStressToggle(object sender, RoutedEventArgs e)
    {
        if (_session != null)
        {
            StressButton.IsEnabled = false;
            var s = _session;
            Task.Run(() => s.Stop(StressEnd.Stopped));
            return;
        }
        if (_busy) return;
        StartStress();
    }

    private void StartStress()
    {
        var opt = new StressOptions
        {
            Cpu = StressCpu.IsChecked == true,
            CpuHeavy = CpuHeavy.IsChecked == true,
            Ram = StressRam.IsChecked == true,
            RamMb = RamStress.FreeMb() * (long)RamPercent.Value / 100,
            Gpu = StressGpu.IsChecked == true,
            Duration = SelectedDuration(),
            CpuTempLimit = (float)CpuLimit.Value,
            GpuTempLimit = (float)GpuLimit.Value,
        };
        if (!opt.Cpu && !opt.Ram && !opt.Gpu)
        {
            StressState.Text = "Ligue pelo menos um teste (CPU, RAM ou GPU).";
            StressState.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(400)));
            return;
        }
        if (opt.Ram && opt.RamMb < 256)
        {
            StressState.Text = "Há pouca RAM livre para o teste de memória. Feche alguns programas.";
            return;
        }

        var session = new StressSession(opt);
        session.Sampled += s => Dispatcher.BeginInvoke(() => OnStressSample(s));
        session.Ended += r => Dispatcher.BeginInvoke(() => OnStressEnded(session, r));
        try
        {
            session.Start();
        }
        catch (Exception ex)
        {
            session.Stop(StressEnd.Failed, ex.Message);
            StressState.Text = "Não foi possível iniciar: " + ex.Message;
            return;
        }
        _session = session;

        if (opt.Gpu)
        {
            int w = Gpu720.IsChecked == true ? 1280 : Gpu1440.IsChecked == true ? 2560 : 1920;
            int h = w * 9 / 16;
            _gpuWindow = new GpuStressWindow(w, h, null) { Owner = Window.GetWindow(this) };
            _gpuWindow.RendererReady += r => session.Gpu = r;
            _gpuWindow.Closed += (_, _) =>
            {
                _gpuWindow = null;
                if (session.Running) Task.Run(() => session.Stop(StressEnd.Stopped, "A janela do teste de GPU foi fechada."));
            };
            _gpuWindow.Show();
        }

        // Interface em modo "rodando".
        _cpuTemps.Clear();
        _gpuTemps.Clear();
        TempChart.SetSeries();
        ReportCard.Visibility = Visibility.Collapsed;
        StressSetup.Visibility = Visibility.Collapsed;
        StressLive.Visibility = Visibility.Visible;
        StaggerIn(LiveGauges);
        StressButton.Content = "■  Parar teste";
        StressState.Text = "Teste em andamento";
        StressDot.SetResourceReference(Shape.FillProperty, "Accent");
        StressDot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.2, TimeSpan.FromSeconds(0.7)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        StressProgressTrack.Visibility = opt.Duration != null ? Visibility.Visible : Visibility.Collapsed;
        LiveErrors.Text = "0";
        LiveErrors.SetResourceReference(TextBlock.ForegroundProperty, "Good");
        LiveCpuRounds.Text = opt.Cpu ? "CPU: preparando..." : "";
        LiveRam.Text = opt.Ram ? "RAM: reservando memória..." : "";
        LiveFps.Text = opt.Gpu ? "GPU: abrindo a janela do teste..." : "";
        foreach (var b in new[] { RunAllButton, CpuButton, RamButton, DiskButton, GpuButton }) b.IsEnabled = false;
        _stressTimer.Start();
        StressTick();
    }

    private void StressTick()
    {
        var s = _session;
        if (s == null || !s.Running) return;
        var el = s.Elapsed;
        var dur = s.Options.Duration;
        StressTime.Text = dur is { } d
            ? $"{Clock(el)} de {Clock(d)} · faltam {Clock(d - el > TimeSpan.Zero ? d - el : TimeSpan.Zero)}"
            : $"{Clock(el)} · rodando até você parar";
        if (dur is { } total)
        {
            Animate(StressProgressFill, el.TotalSeconds / total.TotalSeconds);
            if (el >= total) Task.Run(() => s.Stop(StressEnd.Completed)); // garantia, caso os sensores atrasem
        }

        long errors = (s.Cpu?.Errors ?? 0) + (s.Ram?.Errors ?? 0);
        LiveErrors.Text = F(errors, "N0");
        if (errors > 0) LiveErrors.SetResourceReference(TextBlock.ForegroundProperty, "Bad");
        if (s.Cpu != null) LiveCpuRounds.Text = $"CPU: {F(s.Cpu.Rounds, "N0")} rodadas de contas conferidas ({s.Cpu.Threads} threads)";
        if (s.Ram != null) LiveRam.Text = $"RAM: {F(s.Ram.BytesChecked / 1e9, "N1")} GB conferidos em {F(s.Ram.AllocatedBytes / 1073741824.0, "0.0")} GB reservados";
        if (s.Gpu is { Running: true } g) LiveFps.Text = $"GPU: {F(g.Fps, "0")} FPS · {F(g.Frames, "N0")} quadros desenhados";
    }

    private static string Clock(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    private void OnStressSample(StressSample s)
    {
        CpuTempGauge.HasValue = s.CpuTemp.HasValue;
        CpuTempGauge.Value = s.CpuTemp ?? 0;
        GpuTempGauge.HasValue = s.GpuTemp.HasValue;
        GpuTempGauge.Value = s.GpuTemp ?? 0;
        CpuLiveInfo.Text = Join(s.CpuClock is { } c ? $"{F(c / 1000, "0.00")} GHz" : null, s.CpuPower is { } p ? $"{p:0} W" : null, s.CpuLoad is { } l ? $"{l:0}% de uso" : null);
        GpuLiveInfo.Text = Join(s.GpuClock is { } gc ? $"{gc:0} MHz" : null, s.GpuLoad is { } gl ? $"{gl:0}% de uso" : null);

        Push(_cpuTemps, s.CpuTemp);
        Push(_gpuTemps, s.GpuTemp);
        var series = new List<ChartSeries> { new("CPU", "Cpu", _cpuTemps.ToList()) };
        if (_session?.Options.Gpu == true || s.GpuTemp != null) series.Add(new ChartSeries("GPU", "Gpu", _gpuTemps.ToList()));
        TempChart.SetSeries(series.ToArray());
    }

    private static void Push(Queue<double?> q, float? v)
    {
        q.Enqueue(v);
        while (q.Count > 120) q.Dequeue();
    }

    private static string Join(params string?[] parts)
    {
        var p = parts.Where(x => x != null).ToList();
        return p.Count > 0 ? string.Join("  ·  ", p) : "—";
    }

    private void OnStressEnded(StressSession session, StressReport r)
    {
        if (_session != session) return;
        StressTick();
        _session = null;
        _stressTimer.Stop();
        _gpuWindow?.Close();
        _gpuWindow = null;

        StressButton.Content = "🔥  Iniciar teste";
        StressButton.IsEnabled = true;
        foreach (var b in new[] { RunAllButton, CpuButton, RamButton, DiskButton, GpuButton }) b.IsEnabled = true;
        StressDot.BeginAnimation(OpacityProperty, null);
        StressDot.SetResourceReference(Shape.FillProperty, r.VerdictBrush);
        StressState.Text = "Teste encerrado";
        StressTime.Text = $"Durou {Clock(r.Duration)}. Ajuste as opções abaixo para testar de novo.";
        StressProgressTrack.Visibility = Visibility.Collapsed;
        StressSetup.Visibility = Visibility.Visible;

        // Relatório: veredito grande e as explicações entrando uma a uma.
        ReportVerdict.Text = r.Verdict;
        ReportVerdict.SetResourceReference(TextBlock.ForegroundProperty, r.VerdictBrush);
        var parts = new List<string> { $"Duração {Clock(r.Duration)}" };
        if (r.MaxCpuTemp is { } ct) parts.Add($"CPU máx. {ct:0} °C");
        if (r.MaxGpuTemp is { } gt) parts.Add($"GPU máx. {gt:0} °C");
        parts.Add($"{F(r.CpuErrors + r.RamErrors, "N0")} erros");
        ReportSummary.Text = string.Join("  ·  ", parts);

        ReportFindings.Children.Clear();
        foreach (var f in r.Findings) ReportFindings.Children.Add(FindingRow(f));
        ReportCard.Visibility = Visibility.Visible;
        ReportCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)));
        ReportVerdict.RenderTransform = new ScaleTransform(0.85, 0.85);
        ReportVerdict.RenderTransformOrigin = new Point(0, 0.5);
        var pop = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(500)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } };
        ReportVerdict.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        ReportVerdict.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        StaggerIn(ReportFindings);
        Dispatcher.BeginInvoke(() => ReportCard.BringIntoView(), DispatcherPriority.Loaded);

        var what = new List<string>();
        if (session.Options.Cpu) what.Add("CPU");
        if (session.Options.Ram) what.Add("RAM");
        if (session.Options.Gpu) what.Add("GPU");
        BenchHistory.Add(new BenchRecord
        {
            Kind = "stress",
            When = DateTime.Now,
            Main = Math.Round(r.Duration.TotalMinutes, 1),
            MainUnit = "min",
            Summary = $"{string.Join(" + ", what)} · {PlainText(r.Verdict)} · " + string.Join(" · ", parts),
        });
    }

    /// <summary>Tira os símbolos (✔ ✖ e emojis) do veredito para guardar no histórico.</summary>
    private static string PlainText(string s) =>
        new string(s.Where(ch => !char.IsSurrogate(ch) && ch != '✔' && ch != '✖').ToArray()).Trim().TrimEnd(':').Trim();

    private UIElement FindingRow(StressFinding f)
    {
        var bar = new Border { Width = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 12, 0) };
        bar.SetResourceReference(Border.BackgroundProperty, f.Brush == "TextDim" ? "Border" : f.Brush);
        var title = new TextBlock { Text = f.Title, FontWeight = FontWeights.SemiBold, FontSize = 14 };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        var text = new TextBlock { Text = f.Text, Style = (Style)FindResource("Dim"), Margin = new Thickness(0, 2, 0, 0) };
        var body = new StackPanel();
        body.Children.Add(title);
        body.Children.Add(text);
        var dock = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
        DockPanel.SetDock(bar, Dock.Left);
        dock.Children.Add(bar);
        dock.Children.Add(body);
        return dock;
    }

    // ================================================================ Histórico

    private static readonly Dictionary<string, (string Icon, string Name)> KindInfo = new()
    {
        ["cpu"] = ("🧮", "Processador"),
        ["ram"] = ("🧠", "Memória RAM"),
        ["disk"] = ("💾", "Disco"),
        ["gpu"] = ("🎮", "Placa de vídeo"),
        ["stress"] = ("🔥", "Estabilidade"),
    };

    private void RefreshHistory()
    {
        var list = BenchHistory.Load();
        HistoryList.Children.Clear();
        HistoryEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            var older = list.Skip(i + 1).FirstOrDefault(x => x.Kind == r.Kind);
            HistoryList.Children.Add(HistoryRow(r, r.Kind != "stress" ? older : null));
        }
        StaggerIn(HistoryList);
    }

    private UIElement HistoryRow(BenchRecord r, BenchRecord? older)
    {
        var (icon, name) = KindInfo.TryGetValue(r.Kind, out var k) ? k : ("•", r.Kind);

        var card = new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 14, 8) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new TextBlock { Text = icon, FontSize = 22, VerticalAlignment = VerticalAlignment.Center });

        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 14 };
        title.Inlines.Add(name);
        title.Inlines.Add(new System.Windows.Documents.Run("   " + r.When.ToString("dd/MM/yyyy HH:mm", Br)) { FontWeight = FontWeights.Normal, FontSize = 12 });
        title.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        mid.Children.Add(title);
        mid.Children.Add(new TextBlock { Text = r.Summary, Style = (Style)FindResource("Dim"), Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(mid, 1);
        grid.Children.Add(mid);

        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var main = new TextBlock { Text = $"{F(r.Main, r.Main >= 100 ? "N0" : "0.0")} {r.MainUnit}", FontSize = 18, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right };
        main.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        right.Children.Add(main);
        if (older is { Main: > 0 })
        {
            double pct = (r.Main - older.Main) / older.Main * 100;
            var delta = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
            if (Math.Abs(pct) < 2)
            {
                delta.Text = "= igual ao anterior";
                delta.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
            }
            else
            {
                delta.Text = pct > 0 ? $"▲ {F(pct, "0")}%" : $"▼ {F(-pct, "0")}%";
                delta.SetResourceReference(TextBlock.ForegroundProperty, pct > 0 ? "Good" : "Bad");
            }
            delta.ToolTip = $"Comparado com o teste de {older.When.ToString("dd/MM/yyyy HH:mm", Br)}";
            right.Children.Add(delta);
        }
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        card.Child = grid;
        return card;
    }

    private void OnClearHistory(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Window.GetWindow(this)!, "Apagar todos os resultados salvos?", "Bottleneck Hunter",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        BenchHistory.Clear();
        RefreshHistory();
    }
}
