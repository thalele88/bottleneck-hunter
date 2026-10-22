using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace BottleneckHunter.Views.SystemInfo;

/// <summary>
/// Desenho de um slot de memória: pente colorido quando ocupado, contorno tracejado quando vazio.
/// Os pentes "encaixam" subindo um após o outro.
/// </summary>
public sealed class RamStick : Grid
{
    public static readonly DependencyProperty FilledProperty = DependencyProperty.Register(
        nameof(Filled), typeof(bool), typeof(RamStick), new PropertyMetadata(false, (d, _) => ((RamStick)d).Build()));

    public static readonly DependencyProperty IndexProperty = DependencyProperty.Register(
        nameof(Index), typeof(int), typeof(RamStick), new PropertyMetadata(0));

    public bool Filled { get => (bool)GetValue(FilledProperty); set => SetValue(FilledProperty, value); }
    public int Index { get => (int)GetValue(IndexProperty); set => SetValue(IndexProperty, value); }

    private readonly ScaleTransform _scale = new(1, 1);

    public RamStick()
    {
        Width = 30;
        Height = 86;
        RenderTransformOrigin = new Point(0.5, 1);
        RenderTransform = _scale;
        Build();
        Loaded += (_, _) => PlayIn();
    }

    private void Build()
    {
        Children.Clear();
        if (!Filled)
        {
            var empty = new Rectangle { RadiusX = 4, RadiusY = 4, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 3, 3 } };
            empty.SetResourceReference(Shape.StrokeProperty, "TextDim");
            empty.Opacity = 0.5;
            Children.Add(empty);
            return;
        }

        var body = new Border { CornerRadius = new CornerRadius(4, 4, 2, 2) };
        body.SetResourceReference(Border.BackgroundProperty, "Accent");
        Children.Add(body);

        // Chips de memória no corpo do pente.
        var chips = new StackPanel { Margin = new Thickness(0, 8, 0, 16), VerticalAlignment = VerticalAlignment.Top };
        for (int i = 0; i < 4; i++)
            chips.Children.Add(new Border { Height = 10, Width = 18, Margin = new Thickness(0, 0, 0, 5), CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)) });
        Children.Add(chips);

        // Contatos dourados embaixo.
        var pins = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) };
        for (int i = 0; i < 6; i++)
            pins.Children.Add(new Rectangle { Width = 2.5, Height = 7, Margin = new Thickness(0.8, 0, 0.8, 0), Fill = new SolidColorBrush(Color.FromRgb(0xF2, 0xC1, 0x4E)) });
        Children.Add(pins);
    }

    private void PlayIn()
    {
        if (!Filled) return;
        var delay = TimeSpan.FromMilliseconds(250 + 110 * Index);
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500))
        {
            BeginTime = delay,
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 },
        };
        _scale.ScaleY = 0;
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }
}
