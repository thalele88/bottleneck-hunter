using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using BottleneckHunter.Core.Overlay;

namespace BottleneckHunter.Views.Overlay;

public partial class OverlayView : UserControl
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    private IDisposable? _watch;
    private bool _syncing;
    private bool _firstPlacement = true;

    public OverlayView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        PreviewHost.SizeChanged += (_, _) => PlacePreview(animate: false);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _watch = OverlayService.Watch();
        OverlayService.Tick += OnTick;
        OverlayService.StateChanged += SyncState;
        SyncState();
        if (OverlayService.Latest is { } d) OnTick(d);
        AnimateIn();
        StartScene();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        OverlayService.Tick -= OnTick;
        OverlayService.StateChanged -= SyncState;
        _watch?.Dispose();
        _watch = null;
        StopScene();
    }

    /// <summary>Copia o estado do serviço para a tela (sem disparar os eventos dos controles).</summary>
    private void SyncState()
    {
        _syncing = true;
        var s = OverlayService.Settings;
        DataContext = null;
        DataContext = s;

        EnabledSwitch.IsChecked = OverlayService.IsVisible;
        Check(CornerButtons, s.Corner.ToString());
        Check(SizeButtons, s.Size.ToString(CultureInfo.InvariantCulture));
        Check(StyleButtons, s.Compact ? "compact" : "list");
        Check(BgButtons, NearestBg(s.BackgroundOpacity));

        bool on = OverlayService.IsVisible;
        StatusText.Text = on ? "Ligado" : "Desligado";
        StatusDot.SetResourceReference(Shape.FillProperty, on ? "Good" : "TextDim");
        StatusDot.BeginAnimation(OpacityProperty, on
            ? new DoubleAnimation(1, 0.3, TimeSpan.FromSeconds(0.9)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }
            : null);

        HotkeyWarning.Text = OverlayService.HotkeyError ?? "";
        HotkeyWarning.Visibility = OverlayService.HotkeyError != null ? Visibility.Visible : Visibility.Collapsed;

        Preview.Apply(s);
        if (OverlayService.Latest is { } d) Preview.Update(d);
        _syncing = false;

        // O tamanho do painel muda depois do layout; posiciona quando estiver pronto.
        Dispatcher.BeginInvoke(() => PlacePreview(animate: !_firstPlacement), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static void Check(Panel group, string tag)
    {
        foreach (var rb in group.Children.OfType<RadioButton>())
            rb.IsChecked = (string)rb.Tag == tag;
    }

    private string NearestBg(double opacity) =>
        BgButtons.Children.OfType<RadioButton>()
            .Select(rb => (string)rb.Tag)
            .MinBy(t => Math.Abs(double.Parse(t, CultureInfo.InvariantCulture) - opacity))!;

    private void OnTick(OverlayData d)
    {
        var f = d.Frames;
        FpsValue.Text = Fmt(f.Fps, "0");
        LowValue.Text = Fmt(f.Low1, "0");
        FtValue.Text = Fmt(f.FrametimeMs, "0.0");
        LiveGraph.SetValues(f.Recent);
        GameText.Text = d.Game is { } g ? "Medindo: " + g : "";
        Preview.Update(d);

        if (d.FrameError is { } err)
        {
            LiveHint.Text = err;
            LiveHint.SetResourceReference(TextBlock.ForegroundProperty, "Warn");
        }
        else
        {
            LiveHint.Text = f.Fps is > 0
                ? LowHint(f)
                : "Abra um jogo e volte aqui com Alt+Tab, ou ligue o overlay para ver tudo dentro do jogo.";
            LiveHint.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
        }
    }

    /// <summary>Uma frase curta explicando o que os números de agora querem dizer.</summary>
    private static string LowHint(FrameStats f)
    {
        if (f.Fps is not { } fps || f.Low1 is not { } low) return "Medindo... o 1% low aparece depois de alguns segundos de jogo.";
        double ratio = low / Math.Max(fps, 1);
        if (ratio >= 0.7) return "Jogo liso: os piores quadros ficam perto da média.";
        if (ratio >= 0.45) return "Algumas travadinhas: os piores quadros caem bem abaixo da média.";
        return "Muitos engasgos: veja os picos vermelhos no gráfico e use \"Caçar gargalos\" para achar a causa.";
    }

    private static string Fmt(double? v, string format) =>
        v is { } x && double.IsFinite(x) ? x.ToString(format, PtBr) : "—";

    // ===== Controles =====

    private void OnEnabledClick(object sender, RoutedEventArgs e)
    {
        if (EnabledSwitch.IsChecked == true) OverlayService.Show();
        else OverlayService.Hide();
    }

    /// <summary>Qualquer interruptor ligado por Binding às preferências.</summary>
    private void OnOptionClick(object sender, RoutedEventArgs e)
    {
        if (!_syncing) OverlayService.SaveSettings();
    }

    private void OnCornerChecked(object sender, RoutedEventArgs e)
    {
        if (_syncing || sender is not RadioButton { Tag: string tag } || !Enum.TryParse<OverlayCorner>(tag, out var c)) return;
        OverlayService.Settings.Corner = c;
        OverlayService.SaveSettings();
    }

    private void OnSizeChecked(object sender, RoutedEventArgs e)
    {
        if (_syncing || sender is not RadioButton { Tag: string tag }) return;
        OverlayService.Settings.Size = int.Parse(tag, CultureInfo.InvariantCulture);
        OverlayService.SaveSettings();
    }

    private void OnStyleChecked(object sender, RoutedEventArgs e)
    {
        if (_syncing || sender is not RadioButton { Tag: string tag }) return;
        OverlayService.Settings.Compact = tag == "compact";
        OverlayService.SaveSettings();
    }

    private void OnBgChecked(object sender, RoutedEventArgs e)
    {
        if (_syncing || sender is not RadioButton { Tag: string tag }) return;
        OverlayService.Settings.BackgroundOpacity = double.Parse(tag, CultureInfo.InvariantCulture);
        OverlayService.SaveSettings();
    }

    private void OnReset(object sender, RoutedEventArgs e) => OverlayService.ResetSettings();

    private void OnOpenBottleneck(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.Navigate("bottleneck");

    // ===== Animações =====

    /// <summary>Leva o painel da pré-visualização até o canto escolhido, deslizando.</summary>
    private void PlacePreview(bool animate)
    {
        double w = PreviewCanvas.ActualWidth, h = PreviewCanvas.ActualHeight;
        double pw = PreviewHost.ActualWidth, ph = PreviewHost.ActualHeight;
        if (w < 1 || pw < 1) return;

        const double m = 10;
        var corner = OverlayService.Settings.Corner;
        bool right = corner is OverlayCorner.TopRight or OverlayCorner.BottomRight;
        bool bottom = corner is OverlayCorner.BottomLeft or OverlayCorner.BottomRight;
        double x = right ? Math.Max(m, w - pw - m) : m;
        double y = bottom ? Math.Max(m, h - ph - m) : m;

        Move(Canvas.LeftProperty, x, animate);
        Move(Canvas.TopProperty, y, animate);
        _firstPlacement = false;
    }

    private void Move(DependencyProperty prop, double to, bool animate)
    {
        if (!animate)
        {
            PreviewHost.BeginAnimation(prop, null);
            PreviewHost.SetValue(prop, to);
            return;
        }
        double from = (double)PreviewHost.GetValue(prop);
        if (double.IsNaN(from) || Math.Abs(from - to) < 0.5) { PreviewHost.SetValue(prop, to); return; }
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop,
        };
        anim.Completed += (_, _) => PreviewHost.SetValue(prop, to);
        PreviewHost.SetValue(prop, to);
        PreviewHost.BeginAnimation(prop, anim);
    }

    /// <summary>Os cartões entram um após o outro, subindo de leve.</summary>
    private void AnimateIn()
    {
        var cards = TopCards.Children.OfType<UIElement>().Concat(new UIElement[] { LookCard, ItemsCard, LearnCard });
        int i = 0;
        foreach (var card in cards)
        {
            var delay = TimeSpan.FromMilliseconds(70 * i++);
            card.Opacity = 0;
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)) { BeginTime = delay });
        }
    }

    /// <summary>As manchas de luz da "cena de jogo" flutuam devagar enquanto a página está aberta.</summary>
    private void StartScene()
    {
        Drift(Blob1, 60, 25, 7);
        Drift(Blob2, -50, -30, 9);
        Drift(Blob3, -40, 20, 6);
    }

    private static void Drift(FrameworkElement blob, double dx, double dy, double seconds)
    {
        var t = (TranslateTransform)blob.RenderTransform;
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, dx, TimeSpan.FromSeconds(seconds)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
        t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, dy, TimeSpan.FromSeconds(seconds * 0.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
    }

    private void StopScene()
    {
        foreach (var blob in new FrameworkElement[] { Blob1, Blob2, Blob3 })
        {
            var t = (TranslateTransform)blob.RenderTransform;
            t.BeginAnimation(TranslateTransform.XProperty, null);
            t.BeginAnimation(TranslateTransform.YProperty, null);
        }
        StatusDot.BeginAnimation(OpacityProperty, null);
    }
}
