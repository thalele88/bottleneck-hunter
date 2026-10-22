using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BottleneckHunter.Views.Bench;

/// <summary>
/// Linha de resultado: nome à esquerda, barra que cresce com animação e o valor à direita.
/// Usada nas notas da CPU, na velocidade da RAM e do disco.
/// </summary>
public sealed class ScoreBar : UserControl
{
    private readonly TextBlock _label = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _value = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
    private readonly Border _fill = new() { CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ScaleTransform _scale = new(0, 1);
    private double _fraction;

    public ScoreBar()
    {
        _label.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        _value.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        _fill.SetResourceReference(Border.BackgroundProperty, "Accent");
        _fill.RenderTransform = _scale;
        _fill.RenderTransformOrigin = new Point(0, 0.5);

        var track = new Border { Height = 10, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0), Child = _fill };
        track.SetResourceReference(Border.BackgroundProperty, "Track");

        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 60 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        Grid.SetColumn(track, 1);
        Grid.SetColumn(_value, 2);
        grid.Children.Add(_label);
        grid.Children.Add(track);
        grid.Children.Add(_value);
        Content = grid;
        Loaded += (_, _) => ((ColumnDefinition)grid.ColumnDefinitions[0]).Width = new GridLength(LabelWidth);
    }

    public double LabelWidth { get; set; } = 170;

    public string Label
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    /// <summary>Chave da cor no tema (Accent, Cpu, Gpu, Good...).</summary>
    public string BrushKey
    {
        set => _fill.SetResourceReference(Border.BackgroundProperty, value);
    }

    public string Value
    {
        get => _value.Text;
        set => _value.Text = value;
    }

    /// <summary>Atualiza a barra (0 a 1). Com animação, ela desliza suavemente até o novo tamanho.</summary>
    public void Set(double fraction, string value, bool animate = true)
    {
        fraction = double.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 0;
        _value.Text = value;
        if (Math.Abs(fraction - _fraction) < 0.001) return;
        _fraction = fraction;
        if (!animate)
        {
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _scale.ScaleX = fraction;
            return;
        }
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(fraction, TimeSpan.FromMilliseconds(550))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    public void Reset()
    {
        _fraction = -1;
        Set(0, "—", animate: false);
    }
}
