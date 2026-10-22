using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace BottleneckHunter.Views.Sensors;

/// <summary>
/// Liga uma cor do tema a um elemento pela chave (ex.: "Warn"), mantendo a troca de tema ao vivo.
/// TextBlock/Control recebem Foreground, Shape recebe Fill e Border recebe Background.
/// </summary>
public static class ThemeBrush
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(ThemeBrush), new PropertyMetadata(null, OnKeyChanged));

    public static string? GetKey(DependencyObject d) => (string?)d.GetValue(KeyProperty);
    public static void SetKey(DependencyObject d, string? value) => d.SetValue(KeyProperty, value);

    private static void OnKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not string key || key.Length == 0 || d is not FrameworkElement fe) return;
        DependencyProperty? target = d switch
        {
            TextBlock => TextBlock.ForegroundProperty,
            Shape => Shape.FillProperty,
            Border => Border.BackgroundProperty,
            Control => Control.ForegroundProperty,
            _ => null,
        };
        if (target != null) fe.SetResourceReference(target, key);
    }
}

/// <summary>Barrinha horizontal que desliza suavemente até o novo valor.</summary>
public sealed class LevelBar : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(LevelBar), new PropertyMetadata(0.0, OnFractionChanged));

    private static readonly DependencyProperty ShownProperty = DependencyProperty.Register(
        "Shown", typeof(double), typeof(LevelBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BrushKeyProperty = DependencyProperty.Register(
        nameof(BrushKey), typeof(string), typeof(LevelBar),
        new PropertyMetadata("Accent", (d, e) => ((LevelBar)d).SetResourceReference(FillProperty, e.NewValue)));

    private static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        "Fill", typeof(Brush), typeof(LevelBar), new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        "Track", typeof(Brush), typeof(LevelBar), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public string BrushKey { get => (string)GetValue(BrushKeyProperty); set => SetValue(BrushKeyProperty, value); }

    public LevelBar()
    {
        SetResourceReference(TrackProperty, "Track");
        SetResourceReference(FillProperty, "Accent");
    }

    private static void OnFractionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var anim = new DoubleAnimation((double)e.NewValue, TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        ((LevelBar)d).BeginAnimation(ShownProperty, anim, HandoffBehavior.SnapshotAndReplace);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double r = h / 2;
        dc.DrawRoundedRectangle((Brush)GetValue(TrackProperty), null, new Rect(0, 0, w, h), r, r);
        double fw = Math.Clamp((double)GetValue(ShownProperty), 0, 1) * w;
        if (fw > 0.5)
            dc.DrawRoundedRectangle((Brush)GetValue(FillProperty), null, new Rect(0, 0, Math.Max(fw, h), h), r, r);
    }
}

/// <summary>Mini gráfico de linha, sem eixos, para os cartões fixados.</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(Sparkline),
        new FrameworkPropertyMetadata(Array.Empty<double>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BrushKeyProperty = DependencyProperty.Register(
        nameof(BrushKey), typeof(string), typeof(Sparkline),
        new PropertyMetadata("Accent", (d, e) => ((Sparkline)d).SetResourceReference(StrokeProperty, e.NewValue)));

    private static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        "Stroke", typeof(Brush), typeof(Sparkline), new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double> Values { get => (IReadOnlyList<double>)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public string BrushKey { get => (string)GetValue(BrushKeyProperty); set => SetValue(BrushKeyProperty, value); }

    /// <summary>Quantos pontos cabem na largura (os mais novos ficam à direita).</summary>
    public int Capacity { get; set; } = 60;

    public Sparkline() => SetResourceReference(StrokeProperty, "Accent");

    protected override void OnRender(DrawingContext dc)
    {
        var vals = Values;
        double w = ActualWidth, h = ActualHeight;
        if (vals == null || vals.Count < 2 || w <= 0 || h <= 0) return;

        var finite = vals.Where(double.IsFinite).ToList();
        if (finite.Count < 2) return;
        double min = finite.Min(), max = finite.Max();
        if (max - min < 1e-6) { min -= 1; max += 1; }

        double step = w / Math.Max(1, Capacity - 1);
        double x0 = w - (vals.Count - 1) * step;
        var pts = new List<Point>();
        for (int i = 0; i < vals.Count; i++)
        {
            if (!double.IsFinite(vals[i])) continue;
            double y = h - 2 - (vals[i] - min) / (max - min) * (h - 4);
            pts.Add(new Point(x0 + i * step, y));
        }
        if (pts.Count < 2) return;

        var stroke = (Brush)GetValue(StrokeProperty);
        var fill = stroke.Clone();
        fill.Opacity = 0.14;

        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(pts[0].X, h), true, true);
            ctx.LineTo(pts[0], false, false);
            ctx.PolyLineTo(pts.Skip(1).ToList(), false, false);
            ctx.LineTo(new Point(pts[^1].X, h), false, false);
        }
        area.Freeze();
        dc.DrawGeometry(fill, null, area);

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(pts[0], false, false);
            ctx.PolyLineTo(pts.Skip(1).ToList(), true, true);
        }
        line.Freeze();
        dc.DrawGeometry(null, new Pen(stroke, 1.6) { LineJoin = PenLineJoin.Round }, line);
        dc.DrawEllipse(stroke, null, pts[^1], 2.6, 2.6);
    }
}
