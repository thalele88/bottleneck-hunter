using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BottleneckHunter.Controls;
using BottleneckHunter.Core;

namespace BottleneckHunter.Views.Overview;

public partial class OverviewView : UserControl
{
    private readonly Queue<double?> _cpu = new();
    private readonly Queue<double?> _gpu = new();

    public OverviewView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            App.Sensors.Updated += OnUpdated;
            AnimateIn();
        };
        Unloaded += (_, _) => App.Sensors.Updated -= OnUpdated;
    }

    /// <summary>Os cartões dos medidores entram um após o outro.</summary>
    private void AnimateIn()
    {
        int i = 0;
        foreach (UIElement card in Gauges.Children)
        {
            var delay = TimeSpan.FromMilliseconds(80 * i++);
            card.Opacity = 0;
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)) { BeginTime = delay });
        }
    }

    private void OnUpdated(HwSnapshot s) => Dispatcher.BeginInvoke(() => Render(s));

    private void Render(HwSnapshot s)
    {
        Set(CpuGauge, s.CpuLoad);
        Set(GpuGauge, s.GpuLoad);
        Set(RamGauge, s.RamPercent);
        Set(VramGauge, s.VramPercent);

        CpuInfo.Text = Join(s.CpuTemp is { } ct ? $"{ct:0} °C" : null, s.CpuClockAvg is { } cc ? $"{cc / 1000:0.0} GHz" : null);
        GpuInfo.Text = Join(s.GpuTemp is { } gt ? $"{gt:0} °C" : null, s.GpuPower is { } gp ? $"{gp:0} W" : null);
        RamInfo.Text = $"{s.RamUsedGb:0.0} de {s.RamTotalGb:0} GB";
        VramInfo.Text = s.VramUsedMb is { } vu && s.VramTotalMb is { } vt ? $"{vu / 1024:0.0} de {vt / 1024:0} GB" : "—";

        Push(_cpu, s.CpuLoad);
        Push(_gpu, s.GpuLoad);
        Chart.SetSeries(
            new ChartSeries("CPU", "Cpu", _cpu.ToList()),
            new ChartSeries("GPU", "Gpu", _gpu.ToList()));

        var (title, hint, brush) = QuickVerdict(s);
        if (Verdict.Text != title)
        {
            Verdict.Text = title;
            Verdict.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(400)));
        }
        Verdict.SetResourceReference(TextBlock.ForegroundProperty, brush);
        VerdictHint.Text = hint;

        SensorWarning.Visibility = App.Sensors.IsReady && s.CpuTemp == null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Leitura instantânea e simplificada. A análise completa (com sessão gravada)
    /// fica na página "Caçar gargalos".
    /// </summary>
    private static (string Title, string Hint, string Brush) QuickVerdict(HwSnapshot s)
    {
        if (s.GpuLoad is not { } gpu || s.CpuLoad is not { } cpu)
            return ("Lendo sensores...", "Aguarde alguns segundos.", "Text");

        float maxCore = s.MaxCoreLoad ?? cpu;
        if (gpu < 40 && cpu < 40)
            return ("PC tranquilo", "Nada pesado rodando agora. Abra um jogo para ver quem limita o desempenho.", "Good");
        if (gpu >= 93)
            return ("A GPU está dando tudo", "Isso é o ideal em jogos: a placa de vídeo é quem define o FPS.", "Good");
        if (maxCore >= 90 || cpu >= 90)
            return ("Possível gargalo na CPU", $"Um núcleo do processador chegou a {maxCore:0}% enquanto a GPU está em {gpu:0}%.", "Warn");
        if (s.VramPercent is >= 95)
            return ("Memória de vídeo cheia", "As texturas não cabem na VRAM. Baixe a qualidade de texturas no jogo.", "Warn");
        if (s.RamPercent >= 90)
            return ("Memória RAM quase cheia", "Feche programas em segundo plano ou considere mais RAM.", "Warn");
        return ("Sem gargalo claro", "Pode ser limite de FPS, V-Sync ou o jogo esperando o disco/rede.", "TextDim");
    }

    private static void Set(Gauge g, float? v)
    {
        g.HasValue = v.HasValue;
        g.Value = v ?? 0;
    }

    private static void Push(Queue<double?> q, float? v)
    {
        q.Enqueue(v);
        while (q.Count > 60) q.Dequeue();
    }

    private static string Join(params string?[] parts) => string.Join("  ·  ", parts.Where(p => p != null));

    private void OnOpenBottleneck(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.Navigate("bottleneck");
}
