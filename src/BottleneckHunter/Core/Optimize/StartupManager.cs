using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using static BottleneckHunter.Core.Optimize.NativeMethods;

namespace BottleneckHunter.Core.Optimize;

/// <summary>O que o app sugere fazer com um programa de inicialização.</summary>
public enum StartupAdvice { PodeDesativar, Decida, MelhorManter }

/// <summary>Um programa que abre sozinho quando o Windows liga.</summary>
public sealed class StartupItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Command { get; init; }
    public required string SourceLabel { get; init; }
    public string? ExePath { get; init; }
    public string? Publisher { get; init; }
    public bool Enabled { get; set; }
    public StartupAdvice Advice { get; init; }
    public required string AdviceText { get; init; }

    internal StartupSource Source { get; init; } = null!;
    /// <summary>Nome do valor no registro (Run) ou do arquivo (pasta Inicializar).</summary>
    internal string ValueName { get; init; } = "";
}

/// <summary>Um dos lugares de onde o Windows abre programas no login, e onde ele anota se estão ligados.</summary>
internal sealed record StartupSource(string Key, string Label, RegistryHive ApprovedHive, string ApprovedPath);

/// <summary>
/// Lê e liga/desliga os programas de inicialização do mesmo jeito que o Gerenciador de Tarefas:
/// nada é apagado, o Windows só anota "desativado" na chave StartupApproved.
/// </summary>
public static class StartupManager
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunWow = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    private static readonly StartupSource UserRun = new("hkcu-run", "Só para você", RegistryHive.CurrentUser, Approved + "Run");
    private static readonly StartupSource MachineRun = new("hklm-run", "Todos os usuários", RegistryHive.LocalMachine, Approved + "Run");
    private static readonly StartupSource MachineRun32 = new("hklm-run32", "Todos os usuários (32 bits)", RegistryHive.LocalMachine, Approved + "Run32");
    private static readonly StartupSource UserFolder = new("user-folder", "Pasta Inicializar (você)", RegistryHive.CurrentUser, Approved + "StartupFolder");
    private static readonly StartupSource CommonFolder = new("common-folder", "Pasta Inicializar (todos)", RegistryHive.LocalMachine, Approved + "StartupFolder");

    private static RegistryKey Root(RegistryHive hive) => RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);

    public static List<StartupItem> List()
    {
        var items = new List<StartupItem>();
        ReadRunKey(items, UserRun, RegistryHive.CurrentUser, Run);
        ReadRunKey(items, MachineRun, RegistryHive.LocalMachine, Run);
        ReadRunKey(items, MachineRun32, RegistryHive.LocalMachine, RunWow);
        ReadFolder(items, UserFolder, Environment.GetFolderPath(Environment.SpecialFolder.Startup));
        ReadFolder(items, CommonFolder, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup));
        return items
            .OrderBy(i => i.Advice switch { StartupAdvice.PodeDesativar => 0, StartupAdvice.Decida => 1, _ => 2 })
            .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void ReadRunKey(List<StartupItem> items, StartupSource src, RegistryHive hive, string path)
    {
        try
        {
            using var key = Root(hive).OpenSubKey(path);
            if (key == null) return;
            foreach (var name in key.GetValueNames().Take(200))
            {
                if (string.IsNullOrWhiteSpace(name) || key.GetValue(name) is not string cmd || cmd.Length == 0) continue;
                var exe = ExeFromCommand(cmd);
                items.Add(Make(src, name, name, cmd, exe));
            }
        }
        catch
        {
            // Sem permissão para ler essa chave: segue com as outras.
        }
    }

    private static void ReadFolder(List<StartupItem> items, StartupSource src, string folder)
    {
        try
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles().Take(200))
            {
                if (file.Attributes.HasFlag(FileAttributes.Hidden) || file.Attributes.HasFlag(FileAttributes.System)) continue;
                if (file.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                string? target = file.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? ShortcutTarget(file.FullName) : file.FullName;
                var display = Path.GetFileNameWithoutExtension(file.Name);
                items.Add(Make(src, file.Name, display, target ?? file.FullName, target));
            }
        }
        catch
        {
            // Pasta inacessível: ignora.
        }
    }

    private static StartupItem Make(StartupSource src, string valueName, string display, string command, string? exe)
    {
        string? publisher = null;
        string? friendly = null;
        if (exe != null)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exe);
                publisher = string.IsNullOrWhiteSpace(info.CompanyName) ? null : info.CompanyName.Trim();
                friendly = string.IsNullOrWhiteSpace(info.FileDescription) ? null : info.FileDescription.Trim();
            }
            catch
            {
                // Arquivo sem informações de versão.
            }
        }

        var (advice, text) = Advise(display + " " + command + " " + publisher);
        return new StartupItem
        {
            Id = src.Key + "|" + valueName,
            Name = friendly != null && friendly.Length <= 60 && !friendly.Equals(display, StringComparison.OrdinalIgnoreCase) ? $"{display} ({friendly})" : display,
            Command = command,
            SourceLabel = src.Label,
            ExePath = exe,
            Publisher = publisher,
            Enabled = ReadEnabled(src, valueName),
            Advice = advice,
            AdviceText = text,
            Source = src,
            ValueName = valueName,
        };
    }

    /// <summary>Extrai o caminho do .exe de uma linha de comando como "C:\App\app.exe" --minimized.</summary>
    public static string? ExeFromCommand(string command)
    {
        var cmd = Environment.ExpandEnvironmentVariables(command.Trim());
        string? candidate = null;
        if (cmd.StartsWith('"'))
        {
            int end = cmd.IndexOf('"', 1);
            if (end > 1) candidate = cmd[1..end];
        }
        else
        {
            int exe = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            candidate = exe > 0 ? cmd[..(exe + 4)] : cmd.Split(' ')[0];
        }
        try
        {
            return candidate != null && Path.IsPathFullyQualified(candidate) && File.Exists(candidate) ? candidate : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ShortcutTarget(string lnk)
    {
        object? link = null;
        try
        {
            link = new ShellLink();
            ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Load(lnk, 0);
            var sb = new StringBuilder(1024);
            ((IShellLinkW)link).GetPath(sb, sb.Capacity, IntPtr.Zero, SLGP_RAWPATH);
            var path = Environment.ExpandEnvironmentVariables(sb.ToString());
            return path.Length > 0 && File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (link != null) Marshal.FinalReleaseComObject(link);
        }
    }

    // ---------- Ligado / desligado ----------
    // O valor em StartupApproved tem 12 bytes. O 1º byte par (02, 06) = ligado; ímpar (03, 07) = desligado.
    // Os 8 bytes finais guardam a data em que foi desligado (o Gerenciador de Tarefas mostra isso).

    private static byte[]? ReadApproved(StartupSource src, string valueName)
    {
        using var key = Root(src.ApprovedHive).OpenSubKey(src.ApprovedPath);
        return key?.GetValue(valueName) as byte[];
    }

    private static bool ReadEnabled(StartupSource src, string valueName)
    {
        try
        {
            var b = ReadApproved(src, valueName);
            return b == null || b.Length == 0 || (b[0] & 1) == 0;
        }
        catch
        {
            return true;
        }
    }

    private static void WriteApproved(StartupSource src, string valueName, byte[]? value)
    {
        using var key = Root(src.ApprovedHive).CreateSubKey(src.ApprovedPath, writable: true);
        if (value == null) key.DeleteValue(valueName, throwOnMissingValue: false);
        else key.SetValue(valueName, value, RegistryValueKind.Binary);
    }

    public static void SetEnabled(StartupItem item, bool enabled)
    {
        var data = OptimizeStore.Data;
        if (!data.StartupBackups.ContainsKey(item.Id))
        {
            var original = ReadApproved(item.Source, item.ValueName);
            data.StartupBackups[item.Id] = original == null ? null : Convert.ToHexString(original);
            OptimizeStore.Save();
        }

        var bytes = new byte[12];
        if (enabled)
        {
            bytes[0] = 0x02;
        }
        else
        {
            bytes[0] = 0x03;
            BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(bytes, 4);
        }
        WriteApproved(item.Source, item.ValueName, bytes);
        item.Enabled = ReadEnabled(item.Source, item.ValueName);
    }

    public static bool HasBackup(StartupItem item) => OptimizeStore.Data.StartupBackups.ContainsKey(item.Id);

    /// <summary>Volta ao estado anterior à primeira mudança feita pelo app.</summary>
    public static void Restore(StartupItem item)
    {
        var data = OptimizeStore.Data;
        if (!data.StartupBackups.TryGetValue(item.Id, out var hex)) return;

        byte[]? original = null;
        if (hex != null)
        {
            try { original = Convert.FromHexString(hex); }
            catch (FormatException) { original = null; }
            if (original is { Length: > 32 }) original = null;
        }
        WriteApproved(item.Source, item.ValueName, original);
        data.StartupBackups.Remove(item.Id);
        OptimizeStore.Save();
        item.Enabled = ReadEnabled(item.Source, item.ValueName);
    }

    // ---------- Conselho ----------
    // Palavras-chave simples. É só uma sugestão: a decisão é sempre de quem usa o PC.

    private static readonly string[] Keep =
    {
        "securityhealth", "windows defender", "windows security", "msmpeng", "antivirus", "avast", "avgui", "kaspersky", "eset security", "egui", "norton", "bitdefender", "mcafee",
        "realtek", "rtkaud", "audio", "nahimic", "waves", "dolby", "synaptics", "elan smart", "etdctrl", "touchpad", "precision touch",
        "igfx", "intel graphics", "amd software", "radeon", "nvidia", "bluetooth", "wacom", "ctfmon",
    };

    private static readonly string[] Disable =
    {
        "spotify", "discord", "steam", "epicgames", "epic games", "battle.net", "eadesktop", "ea desktop", "origin", "ubisoft", "uplay",
        "riotclient", "riot client", "gog galaxy", "overwolf", "teams", "skype", "zoom", "whatsapp", "telegram", "slack",
        "utorrent", "bittorrent", "qbittorrent", "ccleaner", "msedge", "microsoftedgeautolaunch", "opera", "brave", "chrome",
        "adobe creative cloud", "acrobat", "itunes", "icloud", "dropbox", "google drive", "cortana", "copilot", "xbox",
    };

    private static readonly string[] Optional = { "onedrive", "logi", "razer", "corsair", "icue", "steelseries", "armoury", "msi", "afterburner", "rivatuner" };

    internal static (StartupAdvice, string) Advise(string text)
    {
        var t = text.ToLowerInvariant();
        if (Optional.Any(t.Contains))
            return (StartupAdvice.Decida, "Útil se você usa: sincroniza arquivos ou guarda perfis de periféricos (luzes, DPI, ventoinhas). Se não usa, pode desativar.");
        if (Keep.Any(t.Contains))
            return (StartupAdvice.MelhorManter, "Melhor manter: parece ser proteção (antivírus), driver de som, vídeo, touchpad ou teclado. Desativar pode tirar funções do PC.");
        if (Disable.Any(t.Contains))
            return (StartupAdvice.PodeDesativar, "Pode desativar: é um app que você mesmo abre quando quiser. O Windows liga mais rápido e sobra memória.");
        return (StartupAdvice.Decida, "Não conhecemos este programa. Se você não sabe o que ele faz, pesquise o nome antes de desativar.");
    }
}
