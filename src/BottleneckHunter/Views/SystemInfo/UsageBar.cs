using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace BottleneckHunter.Views.SystemInfo;

/// <summary>Barra de uso (0 a 100) que enche suavemente ao aparecer. A cor segue o nível: Good, Warn, Bad ou Accent.</summary>
public sealed class UsageBar : Grid
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(UsageBar), new PropertyMetadata(0.0, (d, _) => ((UsageBar)d).Animate()));

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(string), typeof(UsageBar), new PropertyMetadata("Accent", (d, _) => ((UsageBar)d).ApplyBrush()));

    private static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        "Fraction", typeof(double), typeof(UsageBar), new PropertyMetadata(0.0, (d, _) => ((UsageBar)d).Resize()));

    private readonly Border _track = new() { CornerRadius = new CornerRadius(4) };
    private readonly Border _fill = new() { CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    private bool _loaded;

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Level { get => (string)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }

    /// <summary>Atraso antes de começar a encher, para barras em sequência entrarem uma após a outra.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(150);

    public UsageBar()
    {
        Height = 8;
        _track.SetResourceReference(Border.BackgroundProperty, "Track");
        Children.Add(_track);
        Children.Add(_fill);
        ApplyBrush();
        SizeChanged += (_, _) => Resize();
        Loaded += (_, _) =>
        {
            _loaded = true;
            Animate();
        };
    }

    private void ApplyBrush() => _fill.SetResourceReference(Border.BackgroundProperty, Level is "Good" or "Warn" or "Bad" ? Level : "Accent");

    private void Animate()
    {
        if (!_loaded) return;
        var target = Math.Clamp(Value / 100, 0, 1);
        BeginAnimation(FractionProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(700))
        {
            BeginTime = Delay,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private void Resize()
    {
        double f = (double)GetValue(FractionProperty);
        // Uma pontinha mínima para "quase vazio" não sumir.
        _fill.Width = f <= 0 ? 0 : Math.Max(ActualHeight, ActualWidth * f);
    }
}
