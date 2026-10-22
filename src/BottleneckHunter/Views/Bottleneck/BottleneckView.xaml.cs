using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using BottleneckHunter.Core;
using BottleneckHunter.Core.Bottleneck;

namespace BottleneckHunter.Views.Bottleneck;

public partial class BottleneckView : UserControl
{
    private static readonly CubicEase EaseOut = new() { EasingMode = EasingMode.EaseOut };

    private readonly Dictionary<string, (Border Fill, TextBlock Value)> _bars = new();
    private Limiter? _shownLimiter;

    // Gravação de sessão: continua mesmo se o usuário trocar de página.
    private readonly List<HwSnapshot> _recording = new();
    private TimeSpan _recordLength;
    private DateTime _recordStart;
    private bool _isRecording;

    public BottleneckView()
    {
        InitializeComponent();
        BuildBars();
        StartPulse();

        App.Sensors.Updated += OnSensors;
        Loaded += (_, _) => Render(App.Sensors.Latest);
    }

    // ================= 1. Ao vivo =================

    private void BuildBars()
    {
        AddBar("cpu", "CPU (total)", "Cpu", "Uso médio de todos os núcleos do processador.");
        AddBar("core", "Núcleo mais ocupado", "Cpu", "O núcleo mais carregado. Se ele está em 100%, o jogo espera por ele, mesmo que o total pareça baixo.");
        AddBar("gpu", "GPU", "Gpu", "Uso da placa de vídeo. Em jogo, perto de 100% é o ideal.");
        AddBar("vram", "VRAM", "Warn", "Memória da placa de vídeo em uso.");
        AddBar("ram", "RAM", "Warn", "Memória do sistema em uso.");
    }

    private void AddBar(string key, string label, string brushKey, string tooltip)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 10), ToolTip = tooltip };
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var name = new TextBlock { Text = label, FontSize = 12 };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
        var value = new TextBlock { Text = "—", FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
        value.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        row.Children.Add(name);
        row.Children.Add(value);

        var track = new Border { Style = (Style)Resources["BarTrack"], Margin = new Thickness(0, 4, 0, 0) };
        var fill = new Border { Style = (Style)Resources["BarFill"], RenderTransform = new ScaleTransform(0, 1) };
        fill.SetResourceReference(Border.BackgroundProperty, brushKey);
        track.Child = fill;
        Grid.SetRow(track, 1);
        row.Children.Add(track);

        Bars.Children.Add(row);
        _bars[key] = (fill, value);
    }

    private void SetBar(string key, float? percent)
    {
        var (fill, value) = _bars[key];
        value.Text = percent is { } p ? $"{p:0}%" : "—";
        double target = Math.Clamp((percent ?? 0) / 100.0, 0, 1);
        fill.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(target, TimeSpan.FromMilliseconds(500)) { EasingFunction = EaseOut });
    }

    private void StartPulse()
    {
        var scale = (ScaleTransform)PulseRing.RenderTransform;
        var grow = new DoubleAnimation(0.75, 1.0, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        PulseRing.BeginAnimation(OpacityProperty, new DoubleAnimation(0.9, 0, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    private void OnSensors(HwSnapshot s)
    {
        if (_isRecording)
        {
            lock (_recording) _recording.Add(s);
        }
        Dispatcher.BeginInvoke(() =>
        {
            if (IsVisible) Render(s);
            if (_isRecording) UpdateRecording();
        });
    }

    private void Render(HwSnapshot s)
    {
        SetBar("cpu", s.CpuLoad);
        SetBar("core", s.MaxCoreLoad);
        SetBar("gpu", s.GpuLoad);
        SetBar("vram", s.VramPercent);
        SetBar("ram", s.RamPercent);

        var verdict = BottleneckAnalyzer.Live(s);
        var info = BottleneckAnalyzer.Info[verdict.Kind];
        LiveDetail.Text = verdict.Detail;

        if (_shownLimiter == verdict.Kind && LiveTitle.Text == verdict.Title) return;
        _shownLimiter = verdict.Kind;

        LiveTitle.Text = verdict.Title;
        LiveExplain.Text = info.Explanation;
        LiveIcon.Text = info.Icon;
        IconBg.SetResourceReference(Shape.FillProperty, info.BrushKey);
        PulseRing.SetResourceReference(Shape.StrokeProperty, info.BrushKey);

        // Troca de veredito: o texto aparece deslizando.
        var slide = new TranslateTransform();
        LiveTitle.RenderTransform = slide;
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-14, 0, TimeSpan.FromMilliseconds(350)) { EasingFunction = EaseOut });
        LiveTitle.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)));
        LiveExplain.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)));
    }

    // ================= 2. Sessão =================

    private void OnRecord(object sender, RoutedEventArgs e)
    {
        if (_isRecording)
        {
            FinishRecording();
            return;
        }

        _recordLength = TimeSpan.FromMinutes(Dur1.IsChecked == true ? 1 : Dur5.IsChecked == true ? 5 : Dur10.IsChecked == true ? 10 : 3);
        lock (_recording) _recording.Clear();
        _recordStart = DateTime.Now;
        _isRecording = true;

        RecordBtn.Content = "■ Parar e ver resultado";
        ResultPanel.Visibility = Visibility.Collapsed;
        RecordingPanel.Visibility = Visibility.Visible;

        var scale = (ScaleTransform)RecordProgress.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, _recordLength));
        RecDot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.2, TimeSpan.FromSeconds(0.7)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        UpdateRecording();
    }

    private void UpdateRecording()
    {
        var left = _recordLength - (DateTime.Now - _recordStart);
        if (left <= TimeSpan.Zero)
        {
            FinishRecording();
            return;
        }
        int count;
        lock (_recording) count = _recording.Count;
        RecordCountdown.Text = $"faltam {left:m\\:ss} · {count} leituras";
    }

    private void FinishRecording()
    {
        _isRecording = false;
        RecordBtn.Content = "● Gravar de novo";
        RecordingPanel.Visibility = Visibility.Collapsed;
        RecDot.BeginAnimation(OpacityProperty, null);

        List<HwSnapshot> samples;
        lock (_recording) samples = _recording.ToList();
        if (samples.Count < 5)
        {
            ShowResultMessage("Gravação curta demais", "Grave pelo menos alguns segundos para ter um resultado.");
            return;
        }
        ShowReport(BottleneckAnalyzer.Summarize(samples));
    }

    private void ShowResultMessage(string title, string subtitle)
    {
        ResultPanel.Visibility = Visibility.Visible;
        ResultTitle.Text = title;
        ResultSubtitle.Text = subtitle;
        StackBar.Children.Clear();
        StackBar.ColumnDefinitions.Clear();
        Legend.Children.Clear();
        Stats.Children.Clear();
        Tips.Children.Clear();
    }

    private void ShowReport(SessionReport r)
    {
        var main = BottleneckAnalyzer.Info[r.Main];
        if (r.MostlyIdle)
            ShowResultMessage("Parece que nenhum jogo estava rodando",
                $"Em {r.Samples} leituras, o PC ficou sem carga em {100 - r.ActiveSamples * 100 / r.Samples}% do tempo. Grave de novo com o jogo aberto.");
        else
            ShowResultMessage($"Quem mais limitou: {main.Name}",
                $"Em {r.Duration:m\\:ss} de jogo, {main.Name.ToLowerInvariant()} foi o limite em {r.Share[r.Main]:0}% do tempo. Os momentos sem carga (menus, telas de carregamento) foram ignorados.");

        // Barra empilhada animada
        int col = 0;
        foreach (var (kind, pct) in r.Share.OrderByDescending(kv => kv.Value))
        {
            var info = BottleneckAnalyzer.Info[kind];
            StackBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pct, GridUnitType.Star) });
            var seg = new Border { RenderTransformOrigin = new Point(0, 0.5), RenderTransform = new ScaleTransform(0, 1), ToolTip = $"{info.Name}: {pct:0}% do tempo" };
            seg.SetResourceReference(Border.BackgroundProperty, info.BrushKey);
            Grid.SetColumn(seg, col);
            StackBar.Children.Add(seg);
            seg.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)) { BeginTime = TimeSpan.FromMilliseconds(120 * col), EasingFunction = EaseOut });

            var dot = new Ellipse { Width = 10, Height = 10, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(Shape.FillProperty, info.BrushKey);
            var label = new TextBlock { Text = $"{info.Name} · {pct:0}%", FontSize = 12.5, Margin = new Thickness(0, 0, 18, 4) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            var item = new StackPanel { Orientation = Orientation.Horizontal };
            item.Children.Add(dot);
            item.Children.Add(label);
            Legend.Children.Add(item);
            col++;
        }

        AddStat("GPU média", $"{r.AvgGpu:0}%");
        AddStat("CPU média", $"{r.AvgCpu:0}%");
        AddStat("Núcleo mais ocupado (média)", $"{r.AvgMaxCore:0}%");
        AddStat("RAM no pico", $"{r.PeakRam:0}%");
        AddStat("CPU mais quente", r.PeakCpuTemp is { } ct ? $"{ct:0} °C" : "—");
        AddStat("GPU mais quente", r.PeakGpuTemp is { } gt ? $"{gt:0} °C" : "—");
        AddStat("VRAM no pico", r.PeakVram is { } v ? $"{v:0}%" : "—");
        AddStat("Leituras", r.Samples.ToString());

        var tips = main.Tips.ToList();
        // Se um segundo limitador apareceu bastante, junta as dicas dele também.
        var second = r.Share.Where(kv => kv.Key != r.Main && kv.Value >= 25).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault(Limiter.Idle);
        if (second != Limiter.Idle) tips.AddRange(BottleneckAnalyzer.Info[second].Tips.Take(2));

        int i = 0;
        foreach (var tip in tips)
        {
            var t = new TextBlock { Text = "→  " + tip, Style = (Style)FindResource("Body"), Margin = new Thickness(0, 0, 0, 6), Opacity = 0 };
            Tips.Children.Add(t);
            t.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromMilliseconds(400 + 90 * i++) });
        }
    }

    private void AddStat(string label, string value)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 12) };
        var v = new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeights.Bold };
        v.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        var l = new TextBlock { Text = label, FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
        l.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
        panel.Children.Add(v);
        panel.Children.Add(l);
        Stats.Children.Add(panel);
    }

    // ================= 3. Checagem =================

    private async void OnCheckup(object sender, RoutedEventArgs e)
    {
        CheckBtn.IsEnabled = false;
        CheckBtn.Content = "Verificando...";
        CheckList.Children.Clear();

        var results = await Task.Run(PcCheckup.Run);

        CheckBtn.IsEnabled = true;
        CheckBtn.Content = "Verificar de novo";

        // Problemas primeiro, depois dicas, depois o que está ok.
        int i = 0;
        foreach (var r in results.OrderBy(r => r.Status switch { CheckStatus.Bad => 0, CheckStatus.Warn => 1, CheckStatus.Info => 2, _ => 3 }))
            CheckList.Children.Add(CheckCard(r, i++));

        int problems = results.Count(r => r.Status is CheckStatus.Bad or CheckStatus.Warn);
        CheckList.Children.Insert(0, new TextBlock
        {
            Text = problems == 0 ? "✅ Nenhum problema de configuração encontrado." : $"Encontramos {problems} ponto(s) para melhorar:",
            Style = (Style)FindResource("Body"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, 0, 0, 10),
        });
    }

    private UIElement CheckCard(CheckResult r, int index)
    {
        var (icon, brush) = r.Status switch
        {
            CheckStatus.Bad => ("", "Bad"),
            CheckStatus.Warn => ("", "Warn"),
            CheckStatus.Info => ("", "Cpu"),
            CheckStatus.Ok => ("", "Good"),
            _ => ("", "TextDim"),
        };

        var card = new Border { Style = (Style)FindResource("Card"), Opacity = 0 };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var iconText = new TextBlock { Text = icon, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 22, Margin = new Thickness(0, 2, 16, 0) };
        iconText.SetResourceReference(TextBlock.ForegroundProperty, brush);
        grid.Children.Add(iconText);

        var body = new StackPanel();
        Grid.SetColumn(body, 1);
        var title = new TextBlock { Text = r.Title, FontSize = 15, FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        body.Children.Add(title);
        body.Children.Add(new TextBlock { Text = r.Finding, Style = (Style)FindResource("Body"), Margin = new Thickness(0, 2, 0, 6) });

        // Explicação e solução só nos itens que precisam de atenção, para não poluir.
        if (r.Status != CheckStatus.Ok)
        {
            body.Children.Add(new TextBlock { Text = "Por que importa: " + r.WhyItMatters, Style = (Style)FindResource("Dim"), Margin = new Thickness(0, 0, 0, 4) });
            if (r.HowToFix != null)
                body.Children.Add(new TextBlock { Text = "Como resolver: " + r.HowToFix, Style = (Style)FindResource("Dim") });
            if (r.FixPage != null)
            {
                var btn = new Button { Content = "Resolver no app →", Style = (Style)FindResource("GhostButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(14, 7, 14, 7) };
                btn.Click += (_, _) => (Window.GetWindow(this) as MainWindow)?.Navigate(r.FixPage);
                body.Children.Add(btn);
            }
        }
        else
        {
            body.Children.Add(new TextBlock { Text = r.WhyItMatters, Style = (Style)FindResource("Dim") });
        }
        grid.Children.Add(body);
        card.Child = grid;

        // Entra em cascata
        var delay = TimeSpan.FromMilliseconds(70 * index);
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { BeginTime = delay });
        var shift = new TranslateTransform(0, 16);
        card.RenderTransform = shift;
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(350)) { BeginTime = delay, EasingFunction = EaseOut });
        return card;
    }
}
