using System.Management;

namespace BottleneckHunter.Core.Bench;

/// <summary>
/// O que o Windows sabe dos pentes de memória: quantos são, a velocidade configurada e a velocidade nominal.
/// Serve para estimar a banda teórica e para dar dicas (single channel, XMP/EXPO desligado).
/// </summary>
public sealed class RamModules
{
    public int Count { get; private init; }
    /// <summary>Velocidade em uso agora, em MT/s (ex.: 3200 numa DDR4-3200).</summary>
    public int ConfiguredMts { get; private init; }
    /// <summary>Velocidade máxima que o pente informa.</summary>
    public int RatedMts { get; private init; }
    public string Type { get; private init; } = "";

    /// <summary>Banda teórica em GB/s: MT/s × 8 bytes × canais (PCs comuns têm no máximo 2 canais).</summary>
    public double TheoreticalGBs => ConfiguredMts * 8.0 * Math.Clamp(Count, 1, 2) / 1000;

    public static RamModules? Read()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Speed, ConfiguredClockSpeed, SMBIOSMemoryType FROM Win32_PhysicalMemory");
            int count = 0, configured = 0, rated = 0, type = 0;
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                {
                    count++;
                    configured = Math.Max(configured, ToInt(mo["ConfiguredClockSpeed"]));
                    rated = Math.Max(rated, ToInt(mo["Speed"]));
                    type = ToInt(mo["SMBIOSMemoryType"]);
                }
            }
            if (count == 0) return null;
            return new RamModules
            {
                Count = count,
                ConfiguredMts = configured > 0 ? configured : rated,
                RatedMts = rated,
                Type = type switch { 26 => "DDR4", 34 => "DDR5", 24 => "DDR3", 30 => "LPDDR4", 35 => "LPDDR5", _ => "" },
            };
        }
        catch
        {
            return null;
        }
    }

    private static int ToInt(object? o)
    {
        try { return o == null ? 0 : Convert.ToInt32(o); }
        catch { return 0; }
    }
}
