using System.Windows;
using System.Windows.Media;

namespace BottleneckHunter.Views.Overlay;

/// <summary>
/// Gráfico de frametime no estilo RivaTuner: cada ponto é o tempo de um quadro.
/// Linha reta = jogo liso. Picos marcados em vermelho = engasgos (stutter).
/// </summary>
public sealed class FrametimeGraph : FrameworkElement
{
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(FrametimeGraph),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SpikeProperty = DependencyProperty.Register(
        nameof(Spike), typeof(Brush), typeof(FrametimeGraph),
        new FrameworkPropertyMetadata(Brushes.Red, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GuideProperty = DependencyProperty.Register(
        nameof(Guide), typeof(Brush), typeof(FrametimeGraph),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush Spike { get => (Brush)GetValue(SpikeProperty); set => SetValue(SpikeProperty, value); }
    public Brush Guide { get => (Brush)GetValue(GuideProperty); set => SetValue(GuideProperty, value); }

    /// <summary>Quantos quadros cabem na largura do gráfico.</summary>
    public int Capacity { get; set; } = 240;

    private double[] _values = Array.Empty<double>();

    public void SetValues(double[] values)
    {
        _values = values;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 4 || h < 4) return;

        // Linha-guia pontilhada no frametime "normal" (mediana) do momento.
        var vals = _values;
        double median = 16.7;
        if (vals.Length > 0)
        {
            var sorted = (double[])vals.Clone();
            Array.Sort(sorted);
            median = sorted[sorted.Length / 2];
        }

        // Escala: o dobro da mediana, para que um engasgo apareça como pico bem visível.
        double max = Math.Max(median * 2.2, 8);
        double guide = h - Math.Min(median / max, 1) * h;
        var gridPen = new Pen(Guide, 1) { DashStyle = DashStyles.Dot };
        dc.DrawLine(gridPen, new Point(0, guide), new Point(w, guide));

        if (vals.Length < 2) return;

        double step = w / Math.Max(2, Capacity - 1);
        double x0 = w - (vals.Length - 1) * step;
        var spikePen = new Pen(Spike, 1.4);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (int i = 0; i < vals.Length; i++)
            {
                double x = x0 + i * step;
                double y = h - Math.Clamp(vals[i] / max, 0, 1) * (h - 1);
                if (i == 0) ctx.BeginFigure(new Point(x, y), false, false);
                else ctx.LineTo(new Point(x, y), true, false);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, new Pen(Stroke, 1.4) { LineJoin = PenLineJoin.Round }, geo);

        // Engasgos: quadros que levaram mais que o dobro do normal.
        for (int i = 0; i < vals.Length; i++)
        {
            if (vals[i] < median * 2 || vals[i] < 8) continue;
            double x = x0 + i * step;
            double y = h - Math.Clamp(vals[i] / max, 0, 1) * (h - 1);
            dc.DrawLine(spikePen, new Point(x, h), new Point(x, y));
        }
    }
}
