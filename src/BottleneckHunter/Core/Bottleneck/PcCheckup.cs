using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace BottleneckHunter.Core.Bottleneck;

public enum CheckStatus { Ok, Info, Warn, Bad, Unknown }

/// <summary>Um item da checagem: o que foi visto, por que importa e como resolver.</summary>
/// <param name="FixPage">Página do app que resolve o problema (ex.: "optimize"), se houver.</param>
public sealed record CheckResult(string Title, CheckStatus Status, string Finding, string WhyItMatters, string? HowToFix = null, string? FixPage = null);

/// <summary>
/// Checagem de configuração: coisas que seguram o desempenho mesmo sem nenhum jogo aberto
/// (memória em canal único, XMP desligado, monitor em 60 Hz, plano de economia de energia...).
/// Só lê; nada aqui altera o sistema.
/// </summary>
public static class PcCheckup
{
    public static List<CheckResult> Run()
    {
        var results = new List<CheckResult>();
        void Try(Func<IEnumerable<CheckResult>> check)
        {
            try { results.AddRange(check()); }
            catch { /* uma checagem com problema não derruba as outras */ }
        }

        Try(CheckMemory);
        Try(CheckRefreshRate);
        Try(CheckPowerPlan);
        Try(CheckGameFeatures);
        Try(CheckSystemDisk);
        Try(CheckGpuDriver);
        return results;
    }

    // ---------- Memória ----------
    private static IEnumerable<CheckResult> CheckMemory()
    {
        var modules = Wmi("SELECT Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType FROM Win32_PhysicalMemory");
        if (modules.Count == 0) yield break;

        double totalGb = modules.Sum(m => ToDouble(m["Capacity"])) / 1073741824.0;
        yield return totalGb switch
        {
            < 12 => new("Quantidade de RAM", CheckStatus.Bad, $"{totalGb:0} GB instalados.",
                "Jogos atuais usam 10 GB ou mais só para eles. Com pouca RAM o Windows usa o disco como memória e tudo engasga.",
                "Passe para 16 GB (o mínimo confortável hoje) ou 32 GB."),
            < 24 => new("Quantidade de RAM", CheckStatus.Ok, $"{totalGb:0} GB instalados.",
                "16 GB atende a maioria dos jogos. Com navegador e Discord abertos junto, pode ficar apertado.",
                "Se a página de gargalos mostrar RAM cheia com frequência, 32 GB resolve."),
            _ => new("Quantidade de RAM", CheckStatus.Ok, $"{totalGb:0} GB instalados.", "Memória de sobra para jogos e programas juntos."),
        };

        if (modules.Count == 1)
            yield return new("Canal da memória", CheckStatus.Warn, "Só 1 pente de memória instalado (canal único).",
                "Com dois pentes, o processador lê a memória por dois caminhos ao mesmo tempo (dual channel). Em jogos, isso costuma dar de 10% a 30% a mais de FPS quando o gargalo é a CPU.",
                "Instale um segundo pente igual ao atual (mesma capacidade e velocidade), nos slots indicados no manual da placa-mãe (geralmente o 2º e o 4º).");
        else if (modules.Count % 2 == 1)
            yield return new("Canal da memória", CheckStatus.Info, $"{modules.Count} pentes de memória (número ímpar).",
                "Com número ímpar de pentes, parte da memória pode funcionar em canal único.",
                "O ideal é 2 ou 4 pentes iguais.");
        else
            yield return new("Canal da memória", CheckStatus.Ok, $"{modules.Count} pentes de memória (dual channel).",
                "A memória está usando dois canais ao mesmo tempo, como deveria.");

        // XMP/EXPO: compara a velocidade configurada com a nominal e com o padrão JEDEC do tipo de memória.
        var first = modules[0];
        int configured = (int)ToDouble(first["ConfiguredClockSpeed"]);
        int rated = (int)ToDouble(first["Speed"]);
        int type = (int)ToDouble(first["SMBIOSMemoryType"]);
        string ddr = type switch { 26 => "DDR4", 34 => "DDR5", 24 => "DDR3", _ => "" };
        bool looksJedec = (type == 26 && configured <= 2666) || (type == 34 && configured <= 4800);

        if (configured > 0 && (rated > configured || looksJedec))
            yield return new("XMP / EXPO", CheckStatus.Warn,
                $"Memória {ddr} rodando a {configured} MT/s" + (rated > configured ? $" (o pente aguenta {rated} MT/s)." : " (velocidade padrão de fábrica)."),
                "Quase toda memória vendida para jogos vem com um perfil mais rápido (XMP na Intel, EXPO na AMD), mas ele vem DESLIGADO de fábrica. Sem ele, a memória roda na velocidade mínima e o processador perde desempenho em jogos.",
                "Entre na BIOS (aperte Del ou F2 ao ligar o PC), procure \"XMP\", \"EXPO\" ou \"DOCP\" e ative o Perfil 1. Salve e reinicie. Se o PC não ligar depois, a BIOS volta ao padrão sozinha após algumas tentativas.");
        else if (configured > 0)
            yield return new("XMP / EXPO", CheckStatus.Ok, $"Memória {ddr} rodando a {configured} MT/s.",
                "A memória está numa velocidade acima do padrão de fábrica: o perfil XMP/EXPO parece estar ativo.");
    }

    // ---------- Monitor ----------
    private static IEnumerable<CheckResult> CheckRefreshRate()
    {
        var current = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref current)) yield break;

        int max = current.dmDisplayFrequency;
        var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        for (int i = 0; EnumDisplaySettings(null, i, ref mode); i++)
            if (mode.dmPelsWidth == current.dmPelsWidth && mode.dmPelsHeight == current.dmPelsHeight)
                max = Math.Max(max, mode.dmDisplayFrequency);

        if (max > current.dmDisplayFrequency + 1)
            yield return new("Taxa de atualização do monitor", CheckStatus.Bad,
                $"O monitor está em {current.dmDisplayFrequency} Hz, mas aceita até {max} Hz.",
                "A taxa de atualização é quantas imagens por segundo o monitor consegue mostrar. Em 60 Hz, mesmo que o jogo rode a 200 FPS, você só VÊ 60. É um dos erros mais comuns: o monitor vem configurado no mínimo.",
                $"Clique com o botão direito na área de trabalho → Configurações de vídeo → Vídeo avançado → Taxa de atualização → escolha {max} Hz. Use o cabo DisplayPort ou HDMI que veio com o monitor.");
        else
            yield return new("Taxa de atualização do monitor", CheckStatus.Ok,
                $"{current.dmDisplayFrequency} Hz em {current.dmPelsWidth}×{current.dmPelsHeight} (o máximo nessa resolução).",
                "O monitor já está mostrando o máximo de imagens por segundo que ele aguenta.");
    }

    // ---------- Energia ----------
    private static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private static readonly Guid Ultimate = new("e9a42b02-d5df-448d-aa00-03f14749eb61");
    private static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");

    private static IEnumerable<CheckResult> CheckPowerPlan()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0) yield break;
        Guid active;
        try { active = Marshal.PtrToStructure<Guid>(ptr); }
        finally { LocalFree(ptr); }

        if (active == PowerSaver)
            yield return new("Plano de energia", CheckStatus.Bad, "O Windows está no plano \"Economia de energia\".",
                "Nesse plano o processador fica preso em clocks baixos para gastar menos. Em jogos, isso derruba o FPS.",
                "Mude para \"Alto desempenho\" na página Otimizar o PC.", "optimize");
        else if (active == HighPerformance || active == Ultimate)
            yield return new("Plano de energia", CheckStatus.Ok, "Plano de alto desempenho ativo.",
                "O processador pode subir o clock rapidamente quando o jogo pede.");
        else
            yield return new("Plano de energia", CheckStatus.Info, "Plano \"Equilibrado\" (padrão do Windows).",
                "Funciona bem na maioria dos PCs modernos. O \"Alto desempenho\" pode ajudar um pouco em PCs de mesa, principalmente no 1% low.",
                "Se quiser testar, ative \"Alto desempenho\" na página Otimizar o PC (dá para desfazer).", "optimize");
    }

    // ---------- Recursos de jogo do Windows ----------
    private static IEnumerable<CheckResult> CheckGameFeatures()
    {
        var bgRecording = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled", 0);
        if (bgRecording is int r && r == 1)
            yield return new("Gravação em segundo plano", CheckStatus.Warn, "O Xbox Game Bar está gravando os últimos minutos o tempo todo.",
                "Gravar vídeo continuamente usa a placa de vídeo e o disco enquanto você joga, mesmo que você nunca salve o clipe.",
                "Desative em Otimizar o PC, ou em Configurações → Jogos → Capturas.", "optimize");
        else
            yield return new("Gravação em segundo plano", CheckStatus.Ok, "Desligada.", "Nada gravando escondido enquanto você joga.");

        var gameMode = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\GameBar", "AutoGameModeEnabled", 1);
        if (gameMode is int g && g == 0)
            yield return new("Modo de Jogo", CheckStatus.Info, "Desligado.",
                "O Modo de Jogo do Windows dá prioridade ao jogo e evita que o Windows Update instale coisas durante a partida.",
                "Ative em Otimizar o PC ou em Configurações → Jogos → Modo de Jogo.", "optimize");
        else
            yield return new("Modo de Jogo", CheckStatus.Ok, "Ligado.", "O Windows prioriza o jogo que estiver aberto.");

        var hags = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", null);
        if (hags is int h)
            yield return h == 2
                ? new("Agendamento de GPU acelerado por hardware", CheckStatus.Ok, "Ligado.",
                    "A própria placa de vídeo organiza o trabalho dela, o que reduz um pouco a latência. É obrigatório para a geração de quadros do DLSS 3.")
                : new("Agendamento de GPU acelerado por hardware", CheckStatus.Info, "Desligado.",
                    "Com ele ligado, a placa de vídeo organiza o próprio trabalho (menos latência) e o DLSS Frame Generation funciona.",
                    "Ative em Otimizar o PC ou em Configurações → Sistema → Tela → Elementos gráficos. Precisa reiniciar.", "optimize");
    }

    // ---------- Disco do sistema ----------
    private static IEnumerable<CheckResult> CheckSystemDisk()
    {
        string sysRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var drive = new DriveInfo(sysRoot);
        double freeGb = drive.AvailableFreeSpace / 1073741824.0;
        double freePct = drive.TotalSize > 0 ? drive.AvailableFreeSpace * 100.0 / drive.TotalSize : 100;

        if (freeGb < 15 || freePct < 8)
            yield return new("Espaço livre no disco do Windows", CheckStatus.Bad, $"Só {freeGb:0} GB livres em {sysRoot.TrimEnd('\\')} ({freePct:0}%).",
                "O Windows precisa de espaço livre para a memória virtual, atualizações e arquivos temporários. Com o disco quase cheio, tudo fica lento e os jogos podem fechar sozinhos.",
                "Apague downloads antigos, desinstale jogos que não usa e rode a Limpeza de Disco do Windows.");
        else
            yield return new("Espaço livre no disco do Windows", freePct < 15 ? CheckStatus.Info : CheckStatus.Ok,
                $"{freeGb:0} GB livres em {sysRoot.TrimEnd('\\')} ({freePct:0}%).",
                "Espaço suficiente para o Windows trabalhar sem aperto.");

        // SSD ou HD? Liga a letra da unidade ao disco físico pelo WMI de armazenamento.
        char letter = sysRoot[0];
        var part = Wmi($"SELECT DiskNumber FROM MSFT_Partition WHERE DriveLetter = '{letter}'", @"root\Microsoft\Windows\Storage").FirstOrDefault();
        if (part == null) yield break;
        var disk = Wmi($"SELECT MediaType, FriendlyName FROM MSFT_PhysicalDisk WHERE DeviceId = '{part["DiskNumber"]}'", @"root\Microsoft\Windows\Storage").FirstOrDefault();
        if (disk == null) yield break;

        int media = (int)ToDouble(disk["MediaType"]);
        string name = disk["FriendlyName"]?.ToString() ?? "disco";
        if (media == 3)
            yield return new("Tipo de disco do Windows", CheckStatus.Bad, $"O Windows está num HD mecânico ({name}).",
                "HDs mecânicos são de 5 a 50 vezes mais lentos que SSDs. O Windows demora para abrir, os jogos carregam devagar e dão travadas ao carregar áreas novas.",
                "Um SSD (mesmo um SATA barato) é o upgrade que mais muda a sensação de velocidade do PC. Instale o Windows e os jogos nele.");
        else if (media == 4)
            yield return new("Tipo de disco do Windows", CheckStatus.Ok, $"SSD ({name}).", "O Windows e os programas abrem rápido.");
    }

    // ---------- Driver de vídeo ----------
    private static IEnumerable<CheckResult> CheckGpuDriver()
    {
        foreach (var gpu in Wmi("SELECT Name, DriverVersion, DriverDate FROM Win32_VideoController"))
        {
            string name = gpu["Name"]?.ToString() ?? "";
            if (name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase))
            {
                yield return new("Driver de vídeo", CheckStatus.Bad, "A placa de vídeo está sem driver (usando o \"Microsoft Basic Display\").",
                    "Sem o driver do fabricante, a placa de vídeo roda sem aceleração 3D: os jogos ficam lentíssimos ou nem abrem.",
                    "Baixe o driver no site da NVIDIA, AMD ou Intel, conforme a sua placa.");
                continue;
            }
            if (gpu["DriverDate"]?.ToString() is not { Length: >= 8 } raw) continue;
            var date = ManagementDateTimeConverter.ToDateTime(raw);
            int months = (int)((DateTime.Now - date).TotalDays / 30);
            yield return months >= 6
                ? new($"Driver de vídeo · {name}", CheckStatus.Info, $"Driver de {date:MM/yyyy} (há {months} meses).",
                    "Drivers novos costumam trazer melhorias de desempenho para jogos lançados recentemente e correções de bugs.",
                    "Atualize pelo app do fabricante (NVIDIA App, AMD Software ou Intel Arc Control).")
                : new($"Driver de vídeo · {name}", CheckStatus.Ok, $"Driver de {date:MM/yyyy}.", "Driver recente.");
        }
    }

    // ---------- Utilidades ----------
    private static List<ManagementBaseObject> Wmi(string query, string scope = @"root\cimv2")
    {
        using var searcher = new ManagementObjectSearcher(scope, query);
        return searcher.Get().Cast<ManagementBaseObject>().ToList();
    }

    private static double ToDouble(object? o) => o == null ? 0 : Convert.ToDouble(o);

    private const int ENUM_CURRENT_SETTINGS = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
