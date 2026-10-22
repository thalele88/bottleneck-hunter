using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BottleneckHunter.Core;

namespace BottleneckHunter;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, UserControl> _pageCache = new();
    private readonly Dictionary<string, RadioButton> _navButtons = new();

    public MainWindow()
    {
        InitializeComponent();
        BuildNav();
        StartAmbientAnimations();

        AdminBadge.Text = IsAdmin()
            ? "Modo administrador ativo: leitura completa dos sensores"
            : "⚠ Sem administrador: alguns sensores e ajustes não vão funcionar";

        App.Sensors.Updated += snap => Dispatcher.BeginInvoke(() => UpdateFooter(snap));
        StateChanged += (_, _) => RootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        Loaded += (_, _) => Navigate(App.Settings.LastPage);
    }

    private void BuildNav()
    {
        string? group = null;
        foreach (var page in Pages.All)
        {
            if (page.Group != group)
            {
                group = page.Group;
                var header = new TextBlock
                {
                    Text = group,
                    FontSize = 10.5,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(28, 16, 0, 6),
                };
                header.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");
                NavPanel.Children.Add(header);
            }

            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = page.Icon, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new TextBlock { Text = page.Title, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });

            var btn = new RadioButton { Content = content, Style = (Style)FindResource("NavItem"), GroupName = "nav", Tag = page.Key };
            btn.Checked += (_, _) => Navigate(page.Key);
            _navButtons[page.Key] = btn;
            NavPanel.Children.Add(btn);
        }
    }

    /// <summary>Abre uma página pelo nome (ex.: "bottleneck"). Também usado por outras páginas.</summary>
    public void Navigate(string key)
    {
        var page = Pages.All.FirstOrDefault(p => p.Key == key) ?? Pages.All[0];
        if (_navButtons.TryGetValue(page.Key, out var btn) && btn.IsChecked != true)
        {
            btn.IsChecked = true; // dispara Navigate de novo pelo evento Checked
            return;
        }

        if (!_pageCache.TryGetValue(page.Key, out var view))
        {
            view = page.Create();
            _pageCache[page.Key] = view;
        }
        if (PageHost.Content == view) return;

        PageHost.Content = view;
        App.Settings.LastPage = page.Key;

        // Transição: a página entra subindo e aparecendo.
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
        PageHost.RenderTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
    }

    private void StartAmbientAnimations()
    {
        // Anel do logo girando devagar
        LogoRing.RenderTransform.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(12)) { RepeatBehavior = RepeatBehavior.Forever });

        // Ponto "ao vivo" pulsando
        LiveDot.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, 0.25, TimeSpan.FromSeconds(0.9)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });

        // Logo entra deslizando
        Logo.RenderTransform = new TranslateTransform();
        Logo.RenderTransform.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(-30, 0, TimeSpan.FromMilliseconds(600)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } });
        Logo.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)));
    }

    private void UpdateFooter(HwSnapshot s)
    {
        FootCpu.Text = Format(s.CpuTemp, s.CpuLoad);
        FootGpu.Text = Format(s.GpuTemp, s.GpuLoad);
    }

    private static string Format(float? temp, float? load) =>
        $"{(temp is { } t ? $"{t:0}°C" : "—")}  ·  {(load is { } l ? $"{l:0}%" : "—")}";

    private static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private void OnToggleTheme(object sender, RoutedEventArgs e) => ThemeManager.Toggle();
    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
