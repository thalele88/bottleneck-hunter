using System.Windows;
using System.Windows.Controls;

namespace BottleneckHunter.Controls;

/// <summary>Página provisória "em construção", usada até o módulo ficar pronto.</summary>
public class PlaceholderView : UserControl
{
    public PlaceholderView(string title, string description)
    {
        var panel = new StackPanel { Margin = new Thickness(32, 28, 32, 28) };
        panel.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("PageTitle") });
        panel.Children.Add(new TextBlock { Text = description, Style = (Style)FindResource("PageSubtitle") });

        var tip = new Border { Style = (Style)FindResource("TipBox"), HorizontalAlignment = HorizontalAlignment.Left };
        tip.Child = new TextBlock { Text = "🚧 Esta parte está em construção e chega numa próxima versão.", Style = (Style)FindResource("Body") };
        panel.Children.Add(tip);
        Content = panel;
    }
}
