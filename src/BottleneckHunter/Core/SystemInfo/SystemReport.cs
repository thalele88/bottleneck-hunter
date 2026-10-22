using System.Text.Json.Serialization;

namespace BottleneckHunter.Core.SystemInfo;

/// <summary>Gravidade de um ponto de atenção ou de um valor (vira cor na tela: verde, amarelo, vermelho).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Level { Info, Good, Warn, Bad }

/// <summary>Tudo o que o app sabe sobre o PC. É o que vira a tela "Meu PC" e o relatório exportado.</summary>
public sealed class SystemReport
{
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string AppVersion { get; set; } = "";
    public string ComputerName { get; set; } = "";

    public OsInfo Os { get; set; } = new();
    public BoardInfo Board { get; set; } = new();
    public CpuInfo Cpu { get; set; } = new();
    public MemoryInfo Memory { get; set; } = new();
    public List<GpuInfo> Gpus { get; set; } = new();
    public List<DisplayInfo> Displays { get; set; } = new();
    public List<DiskInfo> Disks { get; set; } = new();

    /// <summary>Pontos de atenção, do mais grave para o mais leve.</summary>
    public List<Finding> Findings { get; set; } = new();

    /// <summary>Partes que não puderam ser lidas (ex.: WMI desligado). A tela avisa sem travar.</summary>
    public List<string> Errors { get; set; } = new();
}

public sealed record Finding(Level Level, string Area, string Title, string Detail, string? HowToFix = null);

public sealed class OsInfo
{
    public string Name { get; set; } = "";
    public string DisplayVersion { get; set; } = "";
    public string Build { get; set; } = "";
    public string Architecture { get; set; } = "";
    public DateTime? InstallDate { get; set; }
    public DateTime? LastBoot { get; set; }
    public string? PowerPlan { get; set; }
    public bool IsWindows11 { get; set; }
}

public sealed class BoardInfo
{
    public string SystemMaker { get; set; } = "";
    public string SystemModel { get; set; } = "";
    public string BoardMaker { get; set; } = "";
    public string BoardModel { get; set; } = "";
    public string BiosMaker { get; set; } = "";
    public string BiosVersion { get; set; } = "";
    public DateTime? BiosDate { get; set; }
    /// <summary>"UEFI", "Legado (BIOS)" ou vazio.</summary>
    public string FirmwareMode { get; set; } = "";
    public bool? SecureBoot { get; set; }
    public string? TpmVersion { get; set; }
}

public sealed class CpuInfo
{
    public string Name { get; set; } = "";
    public string Maker { get; set; } = "";
    public int Cores { get; set; }
    public int Threads { get; set; }
    public int BaseClockMhz { get; set; }
    public int L2CacheKb { get; set; }
    public int L3CacheKb { get; set; }
    public string Socket { get; set; } = "";
    public bool? VirtualizationEnabled { get; set; }
    public int Sockets { get; set; } = 1;
}

public sealed class MemoryInfo
{
    public double TotalGb { get; set; }
    /// <summary>Quantos slots a placa-mãe tem (0 = desconhecido).</summary>
    public int Slots { get; set; }
    public double MaxCapacityGb { get; set; }
    public List<RamModule> Modules { get; set; } = new();
    /// <summary>"Dual channel", "Single channel", "Quad channel" ou "Desconhecido".</summary>
    public string ChannelMode { get; set; } = "Desconhecido";
    /// <summary>Velocidade de fábrica estimada pelo código do pente, quando dá para descobrir.</summary>
    public int? RatedSpeedFromPartNumber { get; set; }
}

public sealed class RamModule
{
    public string Slot { get; set; } = "";
    public string Bank { get; set; } = "";
    public double SizeGb { get; set; }
    public string Type { get; set; } = "";
    public int SpeedMts { get; set; }
    public int ConfiguredSpeedMts { get; set; }
    public string Maker { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public string FormFactor { get; set; } = "";
    public double? Voltage { get; set; }
    /// <summary>Canal de memória deduzido do nome do slot (A, B...), se possível.</summary>
    public string? Channel { get; set; }
}

public sealed class GpuInfo
{
    public string Name { get; set; } = "";
    public string Maker { get; set; } = "";
    public double? VramGb { get; set; }
    public string DriverVersion { get; set; } = "";
    public DateTime? DriverDate { get; set; }
    public bool IsIntegrated { get; set; }
    public bool IsBasicDriver { get; set; }
}

public sealed class DisplayInfo
{
    public string Name { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public int RefreshHz { get; set; }
    /// <summary>Maior taxa que o monitor aceita na resolução atual.</summary>
    public int MaxRefreshHz { get; set; }
    public bool IsPrimary { get; set; }
}

public sealed class DiskInfo
{
    public int Number { get; set; }
    public string Model { get; set; } = "";
    /// <summary>"SSD", "HD", "SCM" ou "Desconhecido".</summary>
    public string Media { get; set; } = "";
    /// <summary>"NVMe", "SATA", "USB" etc.</summary>
    public string Bus { get; set; } = "";
    public double SizeGb { get; set; }
    public string Firmware { get; set; } = "";
    public int? SpindleRpm { get; set; }
    /// <summary>Saúde segundo o Windows: "Saudável", "Atenção", "Com defeito" ou "Desconhecida".</summary>
    public string WindowsHealth { get; set; } = "";
    public bool? SmartPredictsFailure { get; set; }

    public int? TemperatureC { get; set; }
    public int? TemperatureMaxC { get; set; }
    /// <summary>Desgaste do SSD em % (0 = novo, 100 = fim da vida estimada).</summary>
    public int? WearPercent { get; set; }
    public long? PowerOnHours { get; set; }
    public long? StartStopCycles { get; set; }
    public long? ReadErrorsUncorrected { get; set; }
    public long? WriteErrorsUncorrected { get; set; }

    public bool HoldsWindows { get; set; }
    public List<VolumeInfo> Volumes { get; set; } = new();
    public List<SmartAttribute> Smart { get; set; } = new();

    /// <summary>Serial usado só para casar dados internamente; nunca vai para o relatório.</summary>
    [JsonIgnore] public string Serial { get; set; } = "";
    [JsonIgnore] public string PnpId { get; set; } = "";
}

public sealed class VolumeInfo
{
    public string Letter { get; set; } = "";
    public string Label { get; set; } = "";
    public string FileSystem { get; set; } = "";
    public double TotalGb { get; set; }
    public double FreeGb { get; set; }
    /// <summary>É a unidade onde o Windows está instalado.</summary>
    public bool IsSystem { get; set; }
    public double UsedPercent => TotalGb > 0 ? (TotalGb - FreeGb) / TotalGb * 100 : 0;
}

/// <summary>Um atributo S.M.A.R.T. clássico (discos SATA).</summary>
public sealed class SmartAttribute
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Current { get; set; }
    public int Worst { get; set; }
    public int? Threshold { get; set; }
    public long Raw { get; set; }
    public Level Level { get; set; } = Level.Info;
    public string Explain { get; set; } = "";
}
