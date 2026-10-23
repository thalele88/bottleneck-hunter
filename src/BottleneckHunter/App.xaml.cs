using System.IO;
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

        try
        {
            Settings = AppSettings.Load();
            ThemeManager.Apply(Settings.DarkTheme);

            // Sensores iniciam em segundo plano para a janela abrir na hora.
            Sensors.Start();

            new MainWindow().Show();
            OverlayService.Initialize();
        }
        catch (Exception ex)
        {
            // Sem isso, um erro aqui deixava o processo rodando sem janela nenhuma.
            var log = WriteCrashLog(ex);
            MessageBox.Show("O Bottleneck Hunter não conseguiu abrir:\n\n" + ex.Message +
                (log != null ? "\n\nDetalhes salvos em:\n" + log : ""),
                "Bottleneck Hunter", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
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
        var log = WriteCrashLog(e.Exception);
        MessageBox.Show("Ocorreu um erro inesperado:\n\n" + e.Exception.Message +
            (log != null ? "\n\nDetalhes salvos em:\n" + log : ""),
            "Bottleneck Hunter", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;

        // Se a janela principal não está visível, não há como continuar: fecha em vez de ficar invisível.
        if (Current?.MainWindow is not { IsVisible: true })
            Current?.Shutdown(1);
    }

    /// <summary>Acrescenta o erro completo em %AppData%\BottleneckHunter\crash.log. Devolve o caminho, ou null.</summary>
    private static string? WriteCrashLog(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(JsonStore.Folder);
            var path = Path.Combine(JsonStore.Folder, "crash.log");
            var info = new FileInfo(path);
            // Roda como administrador: nunca escreve através de um link plantado.
            if (info.Exists && (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.Length > 1024 * 1024))
                return null;
            File.AppendAllText(path, $"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====\n{ex}\n\n");
            return path;
        }
        catch
        {
            return null;
        }
    }
}
