using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BottleneckHunter.Controls;

/// <summary>Uma série do gráfico: nome, chave do pincel do tema e valores.</summary>
public sealed record ChartSeries(string Name, string BrushKey, IReadOnlyList<double?> Values);

/// <summary>
/// Gráfico de linhas leve (área preenchida + linha), com grade de fundo.
/// Chame <see cref="SetSeries"/> a cada atualização.
/// </summary>
public sealed class LineChart : Canvas
{
    public double MaxY { get; set; } = 100;
    public int Capacity { get; set; } = 60;
    public string YUnit { get; set; } = "%";

    private IReadOnlyList<ChartSeries> _series = Array.Empty<ChartSeries>();

    public LineChart()
    {
        ClipToBounds = true;
        MinHeight = 120;
        SizeChanged += (_, _) => Redraw();
    }

    public void SetSeries(params ChartSeries[] series)
    {
        _series = series;
        Redraw();
    }

    private void Redraw()
    {
        Children.Clear();
        double w = ActualWidth, h = ActualHeight;
        if (w < 10 || h < 10) return;

        // Grade horizontal (25/50/75/100)
        for (int i = 1; i <= 4; i++)
        {
            double y = h - h * i / 4;
            var line = new Line { X1 = 0, X2 = w, Y1 = y, Y2 = y, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 4 } };
            line.SetResourceReference(Shape.StrokeProperty, "Border");
            Children.Add(line);
            var lbl = new TextBlock { Text = $"{MaxY * i / 4:0}{YUnit}", FontSize = 10 };
            lbl.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
            SetLeft(lbl, 4);
            SetTop(lbl, y + 2);
            Children.Add(lbl);
        }

        foreach (var s in _series)
        {
            var vals = s.Values.Skip(Math.Max(0, s.Values.Count - Capacity)).ToList();
            if (vals.Count < 2) continue;
            double step = w / (Capacity - 1);
            double x0 = w - (vals.Count - 1) * step;

            var pts = new List<Point>();
            for (int i = 0; i < vals.Count; i++)
            {
                if (vals[i] is not { } v) continue;
                double y = h - Math.Clamp(v / MaxY, 0, 1) * (h - 4) - 2;
                pts.Add(new Point(x0 + i * step, y));
            }
            if (pts.Count < 2) continue;

            var brush = (Brush)(TryFindResource(s.BrushKey) ?? Brushes.Orange);
            var fillBrush = brush.Clone();
            fillBrush.Opacity = 0.15;

            var area = new Polygon { Fill = fillBrush };
            area.Points.Add(new Point(pts[0].X, h));
            foreach (var p in pts) area.Points.Add(p);
            area.Points.Add(new Point(pts[^1].X, h));
            Children.Add(area);

            var poly = new Polyline { Stroke = brush, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
            foreach (var p in pts) poly.Points.Add(p);
            Children.Add(poly);

            // Ponto pulsante no valor mais recente
            var dot = new Ellipse { Width = 8, Height = 8, Fill = brush };
            SetLeft(dot, pts[^1].X - 4);
            SetTop(dot, pts[^1].Y - 4);
            Children.Add(dot);
        }
    }
}
