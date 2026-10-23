using System.IO;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BottleneckHunter.Core.SystemInfo;

/// <summary>
/// Junta as informações do PC (WMI, registro e APIs do Windows) num <see cref="SystemReport"/>.
/// Cada parte é lida separadamente: se uma falhar, as outras continuam e o erro aparece na tela.
/// Demora alguns segundos, então chame fora da thread da interface.
/// </summary>
public static class SystemInfoCollector
{
    public static SystemReport Collect(IProgress<string>? progress = null, IReadOnlyList<SensorReading>? sensors = null)
    {
        var r = new SystemReport
        {
            ComputerName = Environment.MachineName,
            AppVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "",
        };

        Step(r, progress, "Lendo o Windows...", "Windows", () => ReadOs(r.Os));
        Step(r, progress, "Lendo a placa-mãe e a BIOS...", "Placa-mãe", () => ReadBoard(r.Board));
        Step(r, progress, "Lendo o processador...", "Processador", () => ReadCpu(r.Cpu));
        Step(r, progress, "Lendo os pentes de memória...", "Memória", () => ReadMemory(r.Memory));
        Step(r, progress, "Lendo a placa de vídeo...", "Placa de vídeo", () => r.Gpus = ReadGpus());
        Step(r, progress, "Lendo os monitores...", "Monitores", () => r.Displays = DisplayReader.Read());
        Step(r, progress, "Lendo os discos e a saúde S.M.A.R.T....", "Discos", () => r.Disks = ReadDisks(sensors));

        progress?.Report("Analisando...");
        r.Findings = HealthAdvisor.Analyze(r);
        return r;
    }

    private static void Step(SystemReport r, IProgress<string>? progress, string label, string area, Action action)
    {
        progress?.Report(label);
        try { action(); }
        catch (Exception ex) { r.Errors.Add($"{area}: {ex.Message}"); }
    }

    // ───────────────────────── Windows ─────────────────────────

    private static void ReadOs(OsInfo os)
    {
        var w = Wmi.First("SELECT Caption, Version, BuildNumber, OSArchitecture, InstallDate, LastBootUpTime FROM Win32_OperatingSystem");
        if (w != null)
        {
            os.Name = w.Str("Caption").Replace("Microsoft ", "");
            os.Build = w.Str("BuildNumber");
            os.Architecture = w.Str("OSArchitecture");
            os.InstallDate = w.Date("InstallDate");
            os.LastBoot = w.Date("LastBootUpTime");
        }

        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
        {
            os.DisplayVersion = WmiRow.Clean(key?.GetValue("DisplayVersion") as string);
            if (key?.GetValue("UBR") is int ubr && os.Build.Length > 0) os.Build += "." + ubr;
        }
        int.TryParse(os.Build.Split('.')[0], out int build);
        os.IsWindows11 = build >= 22000;
        if (os.IsWindows11 && os.Name.Contains("Windows 10")) os.Name = os.Name.Replace("Windows 10", "Windows 11");

        try
        {
            os.PowerPlan = Wmi.First("SELECT ElementName FROM Win32_PowerPlan WHERE IsActive = TRUE", @"\\.\root\cimv2\power")?.Str("ElementName");
        }
        catch { }
    }

    // ───────────────────────── Placa-mãe e BIOS ─────────────────────────

    private static void ReadBoard(BoardInfo b)
    {
        if (Wmi.First("SELECT Manufacturer, Model FROM Win32_ComputerSystem") is { } cs)
        {
            b.SystemMaker = ShortMaker(cs.Str("Manufacturer"));
            b.SystemModel = cs.Str("Model");
        }
        if (Wmi.First("SELECT Manufacturer, Product FROM Win32_BaseBoard") is { } bb)
        {
            b.BoardMaker = ShortMaker(bb.Str("Manufacturer"));
            b.BoardModel = bb.Str("Product");
        }
        if (Wmi.First("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS") is { } bios)
        {
            b.BiosMaker = bios.Str("Manufacturer");
            b.BiosVersion = bios.Str("SMBIOSBIOSVersion");
            b.BiosDate = bios.Date("ReleaseDate");
        }

        try
        {
            if (GetFirmwareType(out int type))
                b.FirmwareMode = type switch { 1 => "Legado (BIOS)", 2 => "UEFI", _ => "" };
        }
        catch { }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            if (key?.GetValue("UEFISecureBootEnabled") is int sb) b.SecureBoot = sb == 1;
        }
        catch { }

        try
        {
            var tpm = Wmi.First("SELECT SpecVersion FROM Win32_Tpm", @"\\.\root\cimv2\Security\MicrosoftTpm");
            var spec = tpm?.Str("SpecVersion").Split(',')[0].Trim();
            if (!string.IsNullOrEmpty(spec)) b.TpmVersion = spec;
        }
        catch { }
    }

    /// <summary>"ASUSTeK COMPUTER INC." → "ASUS", "Micro-Star International Co., Ltd." → "MSI".</summary>
    private static string ShortMaker(string maker) => maker switch
    {
        _ when maker.StartsWith("ASUSTeK", StringComparison.OrdinalIgnoreCase) => "ASUS",
        _ when maker.StartsWith("Micro-Star", StringComparison.OrdinalIgnoreCase) => "MSI",
        _ when maker.StartsWith("Gigabyte", StringComparison.OrdinalIgnoreCase) => "Gigabyte",
        _ when maker.StartsWith("ASRock", StringComparison.OrdinalIgnoreCase) => "ASRock",
        _ when maker.StartsWith("Hewlett", StringComparison.OrdinalIgnoreCase) => "HP",
        _ when maker.StartsWith("Dell", StringComparison.OrdinalIgnoreCase) => "Dell",
        _ when maker.StartsWith("LENOVO", StringComparison.OrdinalIgnoreCase) => "Lenovo",
        _ => maker,
    };

    // ───────────────────────── Processador ─────────────────────────

    private static void ReadCpu(CpuInfo c)
    {
        var rows = Wmi.Query("SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L2CacheSize, L3CacheSize, SocketDesignation, VirtualizationFirmwareEnabled FROM Win32_Processor");
        if (rows.Count == 0) return;
        var p = rows[0];
        c.Name = p.Str("Name");
        c.Maker = p.Str("Manufacturer") switch
        {
            "GenuineIntel" => "Intel",
            "AuthenticAMD" => "AMD",
            var m => m,
        };
        c.Sockets = rows.Count;
        c.Cores = rows.Sum(x => x.Int("NumberOfCores"));
        c.Threads = rows.Sum(x => x.Int("NumberOfLogicalProcessors"));
        c.BaseClockMhz = p.Int("MaxClockSpeed");
        c.L2CacheKb = p.Int("L2CacheSize");
        c.L3CacheKb = p.Int("L3CacheSize");
        c.Socket = p.Str("SocketDesignation");
        c.VirtualizationEnabled = p.Bool("VirtualizationFirmwareEnabled");
    }

    // ───────────────────────── Memória RAM ─────────────────────────

    private static void ReadMemory(MemoryInfo m)
    {
        foreach (var row in Wmi.Query("SELECT * FROM Win32_PhysicalMemory"))
        {
            long bytes = row.Long("Capacity") ?? 0;
            if (bytes <= 0) continue;
            var mod = new RamModule
            {
                Slot = row.Str("DeviceLocator"),
                Bank = row.Str("BankLabel"),
                SizeGb = bytes / 1073741824.0,
                SpeedMts = row.Int("Speed"),
                ConfiguredSpeedMts = row.Int("ConfiguredClockSpeed"),
                Maker = MemoryMaker(row.Str("Manufacturer")),
                PartNumber = row.Str("PartNumber"),
                FormFactor = row.Int("FormFactor") switch { 8 => "DIMM (desktop)", 12 => "SO-DIMM (notebook)", _ => "" },
                Voltage = row.Long("ConfiguredVoltage") is > 0 and var mv ? mv / 1000.0 : null,
            };
            if (mod.ConfiguredSpeedMts == 0) mod.ConfiguredSpeedMts = mod.SpeedMts;
            mod.Type = MemoryType(row.Int("SMBIOSMemoryType"), row.Int("MemoryType"), Math.Max(mod.SpeedMts, mod.ConfiguredSpeedMts));
            mod.Channel = ChannelOf(mod.Bank, mod.Slot);
            m.Modules.Add(mod);
        }

        foreach (var arr in Wmi.Query("SELECT MemoryDevices, MaxCapacityEx, MaxCapacity, Use FROM Win32_PhysicalMemoryArray"))
        {
            if (arr.Int("Use") is not (3 or 0)) continue; // 3 = memória do sistema
            m.Slots += arr.Int("MemoryDevices");
            long kb = arr.Long("MaxCapacityEx") ?? 0;
            if (kb <= 0) kb = arr.Long("MaxCapacity") ?? 0;
            m.MaxCapacityGb += kb / 1048576.0;
        }
        if (m.Slots < m.Modules.Count) m.Slots = m.Modules.Count;

        m.TotalGb = m.Modules.Sum(x => x.SizeGb);
        if (m.TotalGb <= 0 && Wmi.First("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem") is { } cs)
            m.TotalGb = (cs.Long("TotalPhysicalMemory") ?? 0) / 1073741824.0;

        var channels = m.Modules.Select(x => x.Channel).Where(x => x != null).Distinct().Count();
        m.ChannelMode = m.Modules.Count switch
        {
            0 => "Desconhecido",
            1 => "Single channel",
            _ when channels >= 4 => "Quad channel",
            _ when channels >= 2 => "Dual channel",
            _ when channels == 1 && m.Modules.All(x => x.Channel != null) => "Single channel",
            _ => "Dual channel (provável)",
        };

        var rated = m.Modules.Select(x => RatedSpeed(x.PartNumber)).Where(x => x != null).Select(x => x!.Value).ToList();
        if (rated.Count > 0) m.RatedSpeedFromPartNumber = rated.Min();
    }

    private static string MemoryType(int smbios, int legacy, int speed) => smbios switch
    {
        20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", 27 => "LPDDR", 28 => "LPDDR2",
        29 => "LPDDR3", 30 => "LPDDR4", 34 => "DDR5", 35 => "LPDDR5",
        _ => legacy switch
        {
            20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4",
            _ => speed >= 4400 ? "DDR5" : speed >= 1866 ? "DDR4" : speed > 0 ? "DDR3" : "",
        },
    };

    private static string MemoryMaker(string raw)
    {
        // Alguns BIOS informam o código JEDEC no lugar do nome.
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Samsung"] = "Samsung", ["00CE"] = "Samsung", ["CE00"] = "Samsung", ["80CE"] = "Samsung",
            ["SK Hynix"] = "SK Hynix", ["Hynix"] = "SK Hynix", ["00AD"] = "SK Hynix", ["AD00"] = "SK Hynix", ["80AD"] = "SK Hynix",
            ["Micron"] = "Micron", ["002C"] = "Micron", ["2C00"] = "Micron", ["802C"] = "Micron",
            ["Kingston"] = "Kingston", ["0198"] = "Kingston", ["9801"] = "Kingston",
            ["Corsair"] = "Corsair", ["029E"] = "Corsair", ["9E02"] = "Corsair",
            ["G Skill"] = "G.Skill", ["04CD"] = "G.Skill", ["CD04"] = "G.Skill",
            ["Crucial"] = "Crucial", ["859B"] = "Crucial", ["9B85"] = "Crucial",
            ["0B6D"] = "Team Group", ["6D0B"] = "Team Group", ["Team Group"] = "Team Group",
            ["XPG"] = "ADATA", ["04CB"] = "ADATA", ["CB04"] = "ADATA",
        };
        foreach (var (k, v) in map)
            if (raw.Contains(k, StringComparison.OrdinalIgnoreCase)) return v;
        return Regex.IsMatch(raw, "^[0-9A-F]{4,16}$") ? "" : raw;
    }

    /// <summary>Canal deduzido do nome do slot: "ChannelA-DIMM0", "P0 CHANNEL B", "DIMM_A1", "A2"...</summary>
    private static string? ChannelOf(string bank, string slot)
    {
        var text = $"{bank} {slot}".ToUpperInvariant();
        var m = Regex.Match(text, @"CHANNEL\s*([A-H])\b|CHANNEL\s*([A-H])-");
        if (m.Success) return m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
        m = Regex.Match(text, @"DIMM[\s_-]*([A-H])\d?\b");
        if (m.Success) return m.Groups[1].Value;
        m = Regex.Match(text, @"\b([A-H])\d\b");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static readonly int[] KnownSpeeds =
    {
        1600, 1866, 2133, 2400, 2666, 2800, 2933, 3000, 3200, 3333, 3466, 3600, 3733, 3800, 4000, 4133, 4266, 4400, 4600,
        4800, 5000, 5200, 5600, 6000, 6200, 6400, 6600, 6800, 7000, 7200, 7600, 8000, 8200, 8400,
    };

    /// <summary>
    /// Velocidade de fábrica pelo código do pente: "CMK16GX4M2B3200C16" → 3200, "KF432C16BB/8" → 3200,
    /// "BL8G36C16U4B" → 3600. Retorna nulo quando o código não segue um padrão conhecido.
    /// </summary>
    internal static int? RatedSpeed(string part)
    {
        var p = part.ToUpperInvariant();
        var k = Regex.Match(p, @"^(?:KF|HX)(\d)(\d{2})[A-Z]");
        if (k.Success) return Snap(int.Parse(k.Groups[2].Value) * 100);
        var c = Regex.Match(p, @"^BLS?\d*K?\d+G\d?(\d{2})[A-Z]");
        if (c.Success) return Snap(int.Parse(c.Groups[1].Value) * 100);

        var found = Regex.Matches(p, @"(?<!\d)(\d{4})(?!\d)")
            .Select(x => int.Parse(x.Groups[1].Value))
            .Where(x => KnownSpeeds.Contains(x))
            .ToList();
        return found.Count > 0 ? found.Max() : null;

        // "26" vira 2600, mas a velocidade real é 2666.
        static int? Snap(int v) => KnownSpeeds.Where(s => s >= v && s - v < 100).Cast<int?>().FirstOrDefault();
    }

    // ───────────────────────── Placa de vídeo ─────────────────────────

    private static List<GpuInfo> ReadGpus()
    {
        var vram = RegistryVram();
        var list = new List<GpuInfo>();
        var rows = Wmi.Query("SELECT Name, AdapterCompatibility, DriverVersion, DriverDate, AdapterRAM, PNPDeviceID FROM Win32_VideoController");
        // Adaptadores virtuais (área de trabalho remota, Parsec...) não são placas de verdade.
        var real = rows.Where(r => r.Str("PNPDeviceID").StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase)).ToList();
        if (real.Count == 0) real = rows;

        foreach (var row in real)
        {
            var name = row.Str("Name");
            var pnp = row.Str("PNPDeviceID");
            var g = new GpuInfo
            {
                Name = name,
                Maker = GpuMaker(name, row.Str("AdapterCompatibility")),
                DriverVersion = row.Str("DriverVersion"),
                DriverDate = row.Date("DriverDate"),
                IsBasicDriver = name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase),
            };
            g.IsIntegrated = IsIntegrated(g);

            var match = vram.FirstOrDefault(v => v.Id.Length > 0 && pnp.StartsWith(v.Id, StringComparison.OrdinalIgnoreCase));
            if (match.Bytes == 0) match = vram.FirstOrDefault(v => v.Desc.Equals(name, StringComparison.OrdinalIgnoreCase));
            long bytes = match.Bytes > 0 ? match.Bytes : row.Long("AdapterRAM") ?? 0;
            if (bytes > 0) g.VramGb = bytes / 1073741824.0;
            list.Add(g);
        }
        // Dedicada primeiro.
        return list.OrderBy(g => g.IsIntegrated).ThenBy(g => g.IsBasicDriver).ToList();
    }

    private static string GpuMaker(string name, string compat)
    {
        var s = name + " " + compat;
        if (s.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) return "NVIDIA";
        if (s.Contains("AMD", StringComparison.OrdinalIgnoreCase) || s.Contains("Radeon", StringComparison.OrdinalIgnoreCase) || s.Contains("ATI ", StringComparison.OrdinalIgnoreCase)) return "AMD";
        if (s.Contains("Intel", StringComparison.OrdinalIgnoreCase)) return "Intel";
        return WmiRow.Clean(compat);
    }

    private static bool IsIntegrated(GpuInfo g) => g.Maker switch
    {
        "Intel" => !Regex.IsMatch(g.Name, @"Arc.*\b[AB]\d{3}", RegexOptions.IgnoreCase),
        "AMD" => !g.Name.Contains("RX", StringComparison.OrdinalIgnoreCase) && !g.Name.Contains("Pro W", StringComparison.OrdinalIgnoreCase)
                 && (g.Name.EndsWith("Graphics", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(g.Name, @"\b\d{3}M\b|Vega \d+")),
        _ => false,
    };

    /// <summary>
    /// O WMI limita a VRAM a 4 GB (campo de 32 bits). O valor certo fica no registro do driver.
    /// </summary>
    private static List<(string Id, string Desc, long Bytes)> RegistryVram()
    {
        var list = new List<(string, string, long)>();
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls == null) return list;
            foreach (var sub in cls.GetSubKeyNames().Where(n => Regex.IsMatch(n, @"^\d{4}$")))
            {
                using var k = cls.OpenSubKey(sub);
                if (k == null) continue;
                long bytes = k.GetValue("HardwareInformation.qwMemorySize") switch
                {
                    long l => l,
                    byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
                    _ => k.GetValue("HardwareInformation.MemorySize") switch
                    {
                        int i => (uint)i,
                        byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0),
                        _ => 0,
                    },
                };
                if (bytes > 0)
                    list.Add((k.GetValue("MatchingDeviceId") as string ?? "", k.GetValue("DriverDesc") as string ?? "", bytes));
            }
        }
        catch { }
        return list;
    }

    // ───────────────────────── Discos ─────────────────────────

    private static List<DiskInfo> ReadDisks(IReadOnlyList<SensorReading>? sensors)
    {
        var disks = new List<DiskInfo>();
        try
        {
            ReadPhysicalDisks(disks);
        }
        catch
        {
            disks.Clear();
        }
        if (disks.Count == 0) ReadLegacyDisks(disks);

        // PNP id de cada disco (para casar com a tabela S.M.A.R.T.).
        try
        {
            foreach (var row in Wmi.Query("SELECT Index, PNPDeviceID, SerialNumber FROM Win32_DiskDrive"))
                if (disks.FirstOrDefault(d => d.Number == row.Int("Index")) is { } d) d.PnpId = row.Str("PNPDeviceID");
        }
        catch { }

        try { ReadSmartTables(disks); } catch { }
        try { ReadVolumes(disks); } catch { }

        foreach (var d in disks) FillFromSmart(d, sensors);
        return disks.OrderBy(d => d.Number).ToList();
    }

    private static void ReadPhysicalDisks(List<DiskInfo> disks)
    {
        var counters = new Dictionary<string, WmiRow>();
        try
        {
            foreach (var rc in Wmi.Query("SELECT * FROM MSFT_StorageReliabilityCounter", Wmi.Storage))
                counters[rc.Str("DeviceId")] = rc;
        }
        catch { }

        Wmi.Each("SELECT * FROM MSFT_PhysicalDisk", mo =>
        {
            var row = new WmiRow(mo);
            if (!int.TryParse(row.Str("DeviceId"), out int number)) return;
            int bus = row.Int("BusType");
            long rpm = row.Long("SpindleSpeed") ?? -1;
            var d = new DiskInfo
            {
                Number = number,
                Model = row.Str("FriendlyName"),
                SizeGb = (row.Long("Size") ?? 0) / 1e9,
                Firmware = row.Str("FirmwareVersion"),
                Serial = row.Str("SerialNumber"),
                Bus = bus switch
                {
                    1 => "SCSI", 3 => "ATA", 7 => "USB", 8 => "RAID", 10 => "SAS", 11 => "SATA",
                    12 => "Cartão SD", 15 => "Virtual", 16 => "Espaços de Armazenamento", 17 => "NVMe", _ => "Outro",
                },
                Media = row.Int("MediaType") switch
                {
                    3 => "HD",
                    4 => "SSD",
                    5 => "SCM",
                    _ => bus == 17 || rpm == 0 ? "SSD" : rpm > 0 && rpm < uint.MaxValue ? "HD" : "Desconhecido",
                },
                WindowsHealth = row.Int("HealthStatus") switch { 0 => "Saudável", 1 => "Atenção", 2 => "Com defeito", _ => "Desconhecida" },
            };
            if (rpm > 0 && rpm < uint.MaxValue) d.SpindleRpm = (int)rpm;

            if (!counters.TryGetValue(row.Str("DeviceId"), out var rc))
            {
                try
                {
                    foreach (ManagementObject rel in mo.GetRelated("MSFT_StorageReliabilityCounter"))
                        using (rel) rc = new WmiRow(rel);
                }
                catch { }
            }
            if (rc != null)
            {
                d.TemperatureC = rc.Int("Temperature") is > 0 and < 150 and var t ? t : null;
                d.TemperatureMaxC = rc.Int("TemperatureMax") is > 0 and < 150 and var tm ? tm : null;
                // Desgaste 0 pode ser "novo" ou "não informado"; em NVMe o campo é confiável.
                int wear = rc.Int("Wear");
                if (wear > 0 || (bus == 17 && d.Media == "SSD")) d.WearPercent = Math.Clamp(wear, 0, 100);
                d.PowerOnHours = rc.Long("PowerOnHours") is > 0 and var h ? h : null;
                d.StartStopCycles = rc.Long("StartStopCycleCount") is > 0 and var s ? s : null;
                d.ReadErrorsUncorrected = rc.Long("ReadErrorsUncorrected");
                d.WriteErrorsUncorrected = rc.Long("WriteErrorsUncorrected");
            }
            disks.Add(d);
        }, Wmi.Storage);
    }

    /// <summary>Plano B para Windows sem o namespace de armazenamento moderno.</summary>
    private static void ReadLegacyDisks(List<DiskInfo> disks)
    {
        foreach (var row in Wmi.Query("SELECT Index, Model, Size, InterfaceType, FirmwareRevision, MediaType, Status FROM Win32_DiskDrive"))
        {
            disks.Add(new DiskInfo
            {
                Number = row.Int("Index"),
                Model = row.Str("Model"),
                SizeGb = (row.Long("Size") ?? 0) / 1e9,
                Bus = row.Str("InterfaceType") switch { "IDE" => "SATA", "SCSI" => "SATA/NVMe", var x => x },
                Firmware = row.Str("FirmwareRevision"),
                Media = "Desconhecido",
                WindowsHealth = row.Str("Status") == "OK" ? "Saudável" : row.Str("Status").Length > 0 ? "Atenção" : "Desconhecida",
            });
        }
    }

    private static void ReadSmartTables(List<DiskInfo> disks)
    {
        DiskInfo? Find(string instance) => disks.FirstOrDefault(d =>
            d.PnpId.Length > 0 && instance.StartsWith(d.PnpId, StringComparison.OrdinalIgnoreCase));

        foreach (var row in Wmi.Query("SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus", Wmi.RootWmi, 10))
            if (Find(row.Str("InstanceName")) is { } d) d.SmartPredictsFailure = row.Bool("PredictFailure");

        var thresholds = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var row in Wmi.Query("SELECT InstanceName, VendorSpecific FROM MSStorageDriver_FailurePredictThresholds", Wmi.RootWmi, 10))
                if (row.Bytes("VendorSpecific") is { } b) thresholds[row.Str("InstanceName")] = b;
        }
        catch { }

        foreach (var row in Wmi.Query("SELECT InstanceName, VendorSpecific FROM MSStorageDriver_FailurePredictData", Wmi.RootWmi, 10))
        {
            var inst = row.Str("InstanceName");
            if (Find(inst) is { } d && row.Bytes("VendorSpecific") is { } data)
                d.Smart = Smart.Parse(data, thresholds.GetValueOrDefault(inst));
        }
    }

    /// <summary>Completa o que os contadores do Windows não deram com a tabela S.M.A.R.T. e os sensores.</summary>
    private static void FillFromSmart(DiskInfo d, IReadOnlyList<SensorReading>? sensors)
    {
        SmartAttribute? A(int id) => d.Smart.FirstOrDefault(a => a.Id == id);

        if (d.TemperatureC == null && (A(194) ?? A(190)) is { Raw: > 0 and < 150 } temp) d.TemperatureC = (int)temp.Raw;
        if (d.PowerOnHours == null && A(9) is { Raw: > 0 } poh) d.PowerOnHours = poh.Raw;
        if (d.WearPercent == null && d.Media == "SSD")
        {
            var life = Smart.LifeAttributes.Select(A).FirstOrDefault(a => a is { Current: > 0 and <= 100 });
            if (life != null) d.WearPercent = 100 - life.Current;
        }
        if (d.TemperatureC == null && sensors != null)
        {
            var s = sensors.FirstOrDefault(x => x.Type == "Temperatura" && x.Value is > 0 and < 150
                                                && x.Hardware.Equals(d.Model, StringComparison.OrdinalIgnoreCase));
            if (s?.Value is { } v) d.TemperatureC = (int)Math.Round(v);
        }
    }

    private static void ReadVolumes(List<DiskInfo> disks)
    {
        var systemLetter = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\', ':') ?? "C";
        var pairs = new List<(int Disk, string Letter)>();

        try
        {
            foreach (var row in Wmi.Query("SELECT DiskNumber, DriveLetter FROM MSFT_Partition", Wmi.Storage))
            {
                var letter = row["DriveLetter"] switch
                {
                    char ch when char.IsLetter(ch) => ch.ToString(),
                    ushort u when char.IsLetter((char)u) => ((char)u).ToString(),
                    _ => "",
                };
                if (letter.Length > 0) pairs.Add((row.Int("DiskNumber"), letter.ToUpperInvariant()));
            }
        }
        catch
        {
            // Plano B: associação clássica partição → unidade lógica.
            foreach (var row in Wmi.Query("SELECT Antecedent, Dependent FROM Win32_LogicalDiskToPartition"))
            {
                var disk = Regex.Match(row.Str("Antecedent"), @"Disk #(\d+)");
                var drive = Regex.Match(row.Str("Dependent"), @"DeviceID=""([A-Z]):""", RegexOptions.IgnoreCase);
                if (disk.Success && drive.Success) pairs.Add((int.Parse(disk.Groups[1].Value), drive.Groups[1].Value.ToUpperInvariant()));
            }
        }

        foreach (var (number, letter) in pairs.Distinct())
        {
            var d = disks.FirstOrDefault(x => x.Number == number);
            if (d == null) continue;
            var v = new VolumeInfo { Letter = letter + ":" };
            try
            {
                var info = new DriveInfo(letter);
                if (info.IsReady)
                {
                    v.Label = info.VolumeLabel;
                    v.FileSystem = info.DriveFormat;
                    v.TotalGb = info.TotalSize / 1e9;
                    v.FreeGb = info.AvailableFreeSpace / 1e9;
                }
            }
            catch { }
            v.IsSystem = letter.Equals(systemLetter, StringComparison.OrdinalIgnoreCase);
            if (v.IsSystem) d.HoldsWindows = true;
            d.Volumes.Add(v);
        }
        foreach (var d in disks) d.Volumes.Sort((a, b) => string.CompareOrdinal(a.Letter, b.Letter));
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetFirmwareType(out int firmwareType);
}
