using System.Windows;

namespace BottleneckHunter.Core;

/// <summary>Troca a paleta (escuro/claro) em tempo de execução.</summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; } = true;

    public static void Apply(bool dark)
    {
        IsDark = dark;
        var dicts = Application.Current.Resources.MergedDictionaries;
        var uri = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        dicts[0] = new ResourceDictionary { Source = uri };
    }

    public static void Toggle()
    {
        Apply(!IsDark);
        App.Settings.DarkTheme = IsDark;
        App.Settings.Save();
    }
}
