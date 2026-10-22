using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace BottleneckHunter.Controls;

/// <summary>
/// Medidor em arco (270°) com animação suave do ponteiro e cor que muda
/// conforme a faixa: verde, amarelo e vermelho.
/// </summary>
public sealed class Gauge : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(Gauge), new PropertyMetadata(0.0, OnValueChanged));

    private static readonly DependencyProperty DisplayValueProperty = DependencyProperty.Register(
        "DisplayValue", typeof(double), typeof(Gauge), new PropertyMetadata(0.0, (d, _) => ((Gauge)d).Redraw()));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Gauge), new PropertyMetadata(100.0, (d, _) => ((Gauge)d).Redraw()));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(Gauge), new PropertyMetadata("", (d, e) => ((Gauge)d)._label.Text = (string)e.NewValue));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(Gauge), new PropertyMetadata("%", (d, _) => ((Gauge)d).Redraw()));

    /// <summary>Acima deste valor fica amarelo.</summary>
    public double WarnAt { get; set; } = 75;
    /// <summary>Acima deste valor fica vermelho.</summary>
    public double BadAt { get; set; } = 90;
    /// <summary>Para métricas em que "alto" é bom (ex.: uso da GPU em jogo), inverte as cores.</summary>
    public bool HighIsGood { get; set; }

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    /// <summary>Quando não há leitura (sensor ausente), mostra "—".</summary>
    public bool HasValue { get; set; } = true;

    private readonly Path _track = new() { StrokeThickness = 10, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly Path _arc = new() { StrokeThickness = 10, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly TextBlock _value = new() { FontSize = 26, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _label = new() { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.75 };

    public Gauge()
    {
        Width = 150;
        Height = 150;
        _track.SetResourceReference(Shape.StrokeProperty, "Track");
        _value.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        _label.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        texts.Children.Add(_value);
        texts.Children.Add(_label);

        var grid = new Grid();
        grid.Children.Add(_track);
        grid.Children.Add(_arc);
        grid.Children.Add(texts);
        Content = grid;

        SizeChanged += (_, _) => Redraw();
        Loaded += (_, _) => Redraw();
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var g = (Gauge)d;
        var anim = new DoubleAnimation((double)e.NewValue, TimeSpan.FromMilliseconds(600))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        g.BeginAnimation(DisplayValueProperty, anim, HandoffBehavior.SnapshotAndReplace);
    }

    private void Redraw()
    {
        double w = ActualWidth > 0 ? ActualWidth : Width, h = ActualHeight > 0 ? ActualHeight : Height;
        double size = Math.Min(w, h) - _arc.StrokeThickness;
        if (size <= 0) return;

        var center = new Point(w / 2, h / 2);
        double r = size / 2;
        const double start = 135, sweep = 270;

        _track.Data = Arc(center, r, start, sweep);

        double shown = (double)GetValue(DisplayValueProperty);
        double frac = Maximum > 0 ? Math.Clamp(shown / Maximum, 0, 1) : 0;
        _arc.Data = frac > 0.001 ? Arc(center, r, start, sweep * frac) : null;

        double pct = frac * 100;
        string key = HighIsGood
            ? (pct >= BadAt ? "Good" : pct >= WarnAt ? "Accent" : "Cpu")
            : (pct >= BadAt ? "Bad" : pct >= WarnAt ? "Warn" : "Good");
        _arc.SetResourceReference(Shape.StrokeProperty, key);

        _value.Text = HasValue ? $"{shown:0}{Unit}" : "—";
    }

    private static Geometry Arc(Point c, double r, double startDeg, double sweepDeg)
    {
        sweepDeg = Math.Min(sweepDeg, 359.9);
        Point P(double deg) => new(c.X + r * Math.Cos(deg * Math.PI / 180), c.Y + r * Math.Sin(deg * Math.PI / 180));
        var fig = new PathFigure { StartPoint = P(startDeg), IsClosed = false };
        fig.Segments.Add(new ArcSegment(P(startDeg + sweepDeg), new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise, true));
        var g = new PathGeometry();
        g.Figures.Add(fig);
        g.Freeze();
        return g;
    }
}
