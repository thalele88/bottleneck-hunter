using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using BottleneckHunter.Core.Overlay;

namespace BottleneckHunter.Views.Overlay;

/// <summary>
/// O "quadradinho" do overlay com as linhas GPU/CPU/RAM/FPS e o gráfico de frametime.
/// Usado pela janela do overlay e pela pré-visualização da página de configurações.
/// Monte com <see cref="Apply"/> (quando as preferências mudam) e atualize com <see cref="Update"/>.
/// </summary>
public sealed class OverlayPanel : Border
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    private readonly Dictionary<string, TextBlock> _values = new();
    private FrametimeGraph? _graph;
    private double _font = 13;

    public OverlayPanel()
    {
        // Paleta própria do overlay: ele fica por cima do jogo, sempre sobre fundo escuro,
        // então não segue o tema claro/escuro do app. CPU e GPU usam as cores do tema.
        Resources["OvText"] = new SolidColorBrush(Color.FromRgb(0xF2, 0xF3, 0xF5));
        Resources["OvDim"] = new SolidColorBrush(Color.FromRgb(0xA4, 0xAB, 0xB8));
        Resources["OvFps"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x3B));
        Resources["OvRam"] = new SolidColorBrush(Color.FromRgb(0xC7, 0x92, 0xEA));
        Resources["OvVram"] = new SolidColorBrush(Color.FromRgb(0xFF, 0x8F, 0xB1));
        Resources["OvGrid"] = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));

        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Effect = new DropShadowEffect { Color = Colors.Black, ShadowDepth = 1, BlurRadius = 3, Opacity = 0.9 };
        Apply(new OverlaySettings());
    }

    /// <summary>Monta as linhas de acordo com o que o usuário escolheu mostrar.</summary>
    public void Apply(OverlaySettings s)
    {
        _values.Clear();
        _graph = null;
        _font = 13 * s.Scale;

        Background = new SolidColorBrush(Color.FromArgb((byte)(s.BackgroundOpacity * 255), 0x0B, 0x0C, 0x10));
        CornerRadius = new CornerRadius(7 * s.Scale);
        Padding = new Thickness(_font * 0.8, _font * 0.5, _font * 0.8, _font * 0.5);

        var groups = new List<(string Label, string Brush, string[] Keys)>();
        if (s.ShowGame) groups.Add(("JOGO", "OvDim", new[] { "game" }));
        AddGroup(groups, "GPU", "Gpu", (s.ShowGpu, "gpu"), (s.ShowGpuTemp, "gpuT"), (s.ShowGpuPower, "gpuW"));
        AddGroup(groups, "CPU", "Cpu", (s.ShowCpu, "cpu"), (s.ShowCpuTemp, "cpuT"));
        AddGroup(groups, "RAM", "OvRam", (s.ShowRam, "ram"));
        AddGroup(groups, "VRAM", "OvVram", (s.ShowVram, "vram"));
        AddGroup(groups, "FPS", "OvFps", (s.ShowFps, "fps"), (s.ShowFrametime, "ft"));
        AddGroup(groups, "LOWS", "OvFps", (s.ShowLows, "low1"), (s.ShowLows, "low01"));
        AddGroup(groups, "HORA", "OvDim", (s.ShowClock, "clock"));

        if (groups.Count == 0 && !s.ShowGraph)
            groups.Add(("", "OvDim", new[] { "empty" }));

        Child = s.Compact ? BuildCompact(groups, s.ShowGraph) : BuildList(groups, s.ShowGraph);
        if (_values.TryGetValue("empty", out var e)) e.Text = "Nada selecionado";
    }

    private static void AddGroup(List<(string, string, string[])> groups, string label, string brush, params (bool On, string Key)[] items)
    {
        var keys = items.Where(i => i.On).Select(i => i.Key).ToArray();
        if (keys.Length > 0) groups.Add((label, brush, keys));
    }

    private UIElement BuildList(List<(string Label, string Brush, string[] Keys)> groups, bool graph)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        int row = 0;
        foreach (var (label, brush, keys) in groups)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var lbl = Label(label, brush);
            Grid.SetRow(lbl, row);
            grid.Children.Add(lbl);

            var vals = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(_font * 0.9, 0, 0, 0) };
            foreach (var k in keys) vals.Children.Add(Value(k));
            Grid.SetRow(vals, row);
            Grid.SetColumn(vals, 1);
            grid.Children.Add(vals);
            row++;
        }

        if (graph)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var g = Graph(_font * 13, _font * 2.8);
            g.Margin = new Thickness(0, row > 0 ? _font * 0.4 : 0, 0, _font * 0.15);
            Grid.SetRow(g, row);
            Grid.SetColumnSpan(g, 2);
            grid.Children.Add(g);
        }
        return grid;
    }

    private UIElement BuildCompact(List<(string Label, string Brush, string[] Keys)> groups, bool graph)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        bool first = true;
        foreach (var (label, brush, keys) in groups)
        {
            var lbl = Label(label, brush);
            lbl.Margin = new Thickness(first ? 0 : _font * 1.1, 0, _font * 0.35, 0);
            panel.Children.Add(lbl);
            foreach (var k in keys) panel.Children.Add(Value(k));
            first = false;
        }
        if (graph)
        {
            var g = Graph(_font * 7, _font * 1.4);
            g.Margin = new Thickness(first ? 0 : _font * 1.1, 0, 0, 0);
            g.VerticalAlignment = VerticalAlignment.Center;
            panel.Children.Add(g);
        }
        return panel;
    }

    private TextBlock Label(string text, string brush)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = _font * 0.82,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return tb;
    }

    private TextBlock Value(string key)
    {
        // Largura mínima para os números não "pularem" quando mudam de 99 para 100.
        double em = key switch
        {
            "gpu" or "cpu" => 2.4,
            "gpuT" or "cpuT" => 2.9,
            "gpuW" => 3.1,
            "ram" or "vram" => 3.7,
            "fps" => 2.1,
            "ft" => 3.6,
            "low1" or "low01" => 3.6,
            "clock" => 2.8,
            _ => 0,
        };
        var tb = new TextBlock
        {
            FontSize = key == "fps" ? _font * 1.2 : _font,
            FontWeight = key == "fps" ? FontWeights.Bold : FontWeights.SemiBold,
            MinWidth = em * _font,
            TextAlignment = em > 0 ? TextAlignment.Right : TextAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(_font * 0.45, 0, 0, 0),
            Text = "—",
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "OvText");
        Typography.SetNumeralAlignment(tb, FontNumeralAlignment.Tabular);
        _values[key] = tb;
        return tb;
    }

    private FrametimeGraph Graph(double w, double h)
    {
        _graph = new FrametimeGraph { Width = w, Height = h };
        _graph.SetResourceReference(FrametimeGraph.StrokeProperty, "OvFps");
        _graph.SetResourceReference(FrametimeGraph.SpikeProperty, "Bad");
        _graph.SetResourceReference(FrametimeGraph.GuideProperty, "OvGrid");
        return _graph;
    }

    /// <summary>Atualiza os números (chamado 4 vezes por segundo).</summary>
    public void Update(OverlayData d)
    {
        var hw = d.Hw;
        var f = d.Frames;

        Set("gpu", hw.GpuLoad, "0", "%");
        Set("gpuT", hw.GpuTemp, "0", "°C", TempBrush(hw.GpuTemp, 80, 87));
        Set("gpuW", hw.GpuPower, "0", "W");
        Set("cpu", hw.CpuLoad, "0", "%");
        Set("cpuT", hw.CpuTemp, "0", "°C", TempBrush(hw.CpuTemp, 80, 90));
        Set("ram", hw.RamTotalGb > 0 ? hw.RamUsedGb : null, "0.0", "GB");
        Set("vram", hw.VramUsedMb / 1024, "0.0", "GB");
        Set("fps", f.Fps, "0", "");
        Set("ft", f.FrametimeMs, "0.0", "ms");
        Set("low1", f.Low1, "0", "", prefix: "1% ");
        Set("low01", f.Low01, "0", "", prefix: "0,1% ");

        if (_values.TryGetValue("clock", out var clock)) clock.Text = d.Time.ToString("HH:mm", PtBr);
        if (_values.TryGetValue("game", out var game)) game.Text = d.Game ?? "—";
        _graph?.SetValues(f.Recent);
    }

    private static string? TempBrush(float? t, float warn, float bad) =>
        t is { } v ? v >= bad ? "Bad" : v >= warn ? "Warn" : null : null;

    private void Set(string key, double? value, string format, string unit, string? brush = null, string prefix = "")
    {
        if (!_values.TryGetValue(key, out var tb)) return;
        tb.Inlines.Clear();
        if (prefix.Length > 0)
        {
            var p = new Run(prefix) { FontSize = _font * 0.72 };
            p.SetResourceReference(TextElement.ForegroundProperty, "OvDim");
            tb.Inlines.Add(p);
        }
        tb.Inlines.Add(new Run(value is { } v && double.IsFinite(v) ? v.ToString(format, PtBr) : "—"));
        if (unit.Length > 0)
        {
            var u = new Run(" " + unit) { FontSize = _font * 0.72 };
            u.SetResourceReference(TextElement.ForegroundProperty, "OvDim");
            tb.Inlines.Add(u);
        }
        tb.SetResourceReference(TextBlock.ForegroundProperty, brush ?? "OvText");
    }

    private void Set(string key, float? value, string format, string unit, string? brush = null) =>
        Set(key, (double?)value, format, unit, brush);
}
