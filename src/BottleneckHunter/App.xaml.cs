using System.Windows;
using System.Windows.Threading;
using BottleneckHunter.Core;
using BottleneckHunter.Core.Overlay;

namespace BottleneckHunter;

public partial class App : Application
{
    public static SensorHub Sensors { get; } = new();
    public static AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        Settings = AppSettings.Load();
        ThemeManager.Apply(Settings.DarkTheme);

        // Sensores iniciam em segundo plano para a janela abrir na hora.
        Sensors.Start();

        new MainWindow().Show();
        OverlayService.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        OverlayService.Shutdown();
        Sensors.Dispose();
        Settings.Save();
        base.OnExit(e);
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show("Ocorreu um erro inesperado:\n\n" + e.Exception.Message,
            "Bottleneck Hunter", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
