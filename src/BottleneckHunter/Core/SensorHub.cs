using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace BottleneckHunter.Core;

/// <summary>Uma leitura de sensor para a tela de Sensores.</summary>
public sealed record SensorReading(string Hardware, string Name, string Type, float? Value, float? Min, float? Max, string Unit);

/// <summary>Resumo do que importa para gargalo, montado a cada segundo.</summary>
public sealed class HwSnapshot
{
    public DateTime Time { get; init; } = DateTime.Now;

    public float? CpuLoad { get; set; }
    public float? CpuTemp { get; set; }
    public float? CpuPower { get; set; }
    public float? CpuClockAvg { get; set; }
    public float? CpuClockMax { get; set; }
    public float[] CoreLoads { get; set; } = Array.Empty<float>();
    public float? MaxCoreLoad => CoreLoads.Length > 0 ? CoreLoads.Max() : CpuLoad;

    public string? GpuName { get; set; }
    public float? GpuLoad { get; set; }
    public float? GpuTemp { get; set; }
    public float? GpuHotspot { get; set; }
    public float? GpuPower { get; set; }
    public float? GpuClock { get; set; }
    public float? GpuMemClock { get; set; }
    public float? GpuFan { get; set; }
    public float? VramUsedMb { get; set; }
    public float? VramTotalMb { get; set; }
    public float? VramPercent => VramUsedMb is { } u && VramTotalMb is > 0 ? u / VramTotalMb * 100 : null;

    public float RamUsedGb { get; set; }
    public float RamTotalGb { get; set; }
    public float RamPercent => RamTotalGb > 0 ? RamUsedGb / RamTotalGb * 100 : 0;

    public IReadOnlyList<SensorReading> All { get; set; } = Array.Empty<SensorReading>();
}

/// <summary>
/// Lê os sensores com a LibreHardwareMonitorLib numa thread própria, uma vez por segundo,
/// e publica um <see cref="HwSnapshot"/> pelo evento <see cref="Updated"/>.
/// </summary>
public sealed class SensorHub : IDisposable
{
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = true,
        IsMotherboardEnabled = true,
        IsStorageEnabled = true,
        IsControllerEnabled = true,
    };

    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;
    private volatile bool _paused;

    public HwSnapshot Latest { get; private set; } = new();
    public bool IsReady { get; private set; }
    public string? Error { get; private set; }

    /// <summary>Histórico curto (15 min) para gráficos e análise de sessão.</summary>
    public List<HwSnapshot> History { get; } = new();
    private readonly object _historyLock = new();

    public event Action<HwSnapshot>? Updated;

    public void Start()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "SensorHub", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    public void Pause(bool paused) => _paused = paused;

    public List<HwSnapshot> HistorySince(TimeSpan span)
    {
        var from = DateTime.Now - span;
        lock (_historyLock) return History.Where(h => h.Time >= from).ToList();
    }

    private void Loop()
    {
        try
        {
            _computer.Open();
            IsReady = true;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }

        while (!_cts.IsCancellationRequested)
        {
            if (!_paused)
            {
                try
                {
                    var snap = Read();
                    Latest = snap;
                    lock (_historyLock)
                    {
                        History.Add(snap);
                        if (History.Count > 900) History.RemoveAt(0);
                    }
                    Updated?.Invoke(snap);
                }
                catch (Exception ex)
                {
                    Error = ex.Message;
                }
            }
            _cts.Token.WaitHandle.WaitOne(1000);
        }
    }

    private HwSnapshot Read()
    {
        var snap = new HwSnapshot();
        var all = new List<SensorReading>();
        var coreLoads = new List<float>();
        var coreClocks = new List<float>();
        IHardware? mainGpu = null;

        if (IsReady)
        {
            foreach (var hw in _computer.Hardware)
            {
                hw.Update();
                foreach (var sub in hw.SubHardware) sub.Update();

                // Preferimos a GPU dedicada (NVIDIA/AMD) à integrada.
                if (hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd)
                    mainGpu ??= hw;
                else if (hw.HardwareType == HardwareType.GpuIntel && mainGpu == null)
                    mainGpu = hw;

                foreach (var s in hw.Sensors.Concat(hw.SubHardware.SelectMany(x => x.Sensors)))
                    all.Add(new SensorReading(hw.Name, s.Name, TypeName(s.SensorType), s.Value, s.Min, s.Max, Unit(s.SensorType)));
            }

            foreach (var hw in _computer.Hardware)
            {
                if (hw.HardwareType == HardwareType.Cpu)
                {
                    foreach (var s in hw.Sensors)
                    {
                        if (s.Value is not { } v) continue;
                        switch (s.SensorType)
                        {
                            case SensorType.Load when s.Name == "CPU Total":
                                snap.CpuLoad = v; break;
                            case SensorType.Load when s.Name.StartsWith("CPU Core #") && !s.Name.Contains("Thread"):
                            case SensorType.Load when s.Name.StartsWith("CPU Core #") && s.Name.Contains("Thread"):
                                coreLoads.Add(v); break;
                            case SensorType.Temperature when s.Name is "CPU Package" or "Core (Tctl/Tdie)" or "Core Max" or "CPU Total":
                                snap.CpuTemp = Math.Max(snap.CpuTemp ?? 0, v); break;
                            case SensorType.Power when s.Name is "CPU Package" or "Package":
                                snap.CpuPower = v; break;
                            case SensorType.Clock when s.Name.StartsWith("Core #") || s.Name.StartsWith("CPU Core #"):
                                coreClocks.Add(v); break;
                        }
                    }
                }
            }

            if (mainGpu != null)
            {
                snap.GpuName = mainGpu.Name;
                foreach (var s in mainGpu.Sensors)
                {
                    if (s.Value is not { } v) continue;
                    switch (s.SensorType)
                    {
                        case SensorType.Load when s.Name is "GPU Core" or "D3D 3D":
                            snap.GpuLoad = Math.Max(snap.GpuLoad ?? 0, v); break;
                        case SensorType.Temperature when s.Name == "GPU Core":
                            snap.GpuTemp = v; break;
                        case SensorType.Temperature when s.Name.Contains("Hot Spot"):
                            snap.GpuHotspot = v; break;
                        case SensorType.Power when s.Name is "GPU Package" or "GPU Power" or "GPU Core":
                            snap.GpuPower = Math.Max(snap.GpuPower ?? 0, v); break;
                        case SensorType.Clock when s.Name == "GPU Core":
                            snap.GpuClock = v; break;
                        case SensorType.Clock when s.Name == "GPU Memory":
                            snap.GpuMemClock = v; break;
                        case SensorType.Control when s.Name.StartsWith("GPU Fan"):
                            snap.GpuFan = v; break;
                        case SensorType.SmallData when s.Name is "GPU Memory Used" or "D3D Dedicated Memory Used":
                            snap.VramUsedMb = Math.Max(snap.VramUsedMb ?? 0, v); break;
                        case SensorType.SmallData when s.Name is "GPU Memory Total" or "D3D Dedicated Memory Total":
                            snap.VramTotalMb = Math.Max(snap.VramTotalMb ?? 0, v); break;
                    }
                }
            }
        }

        // RAM direto do Windows: mais confiável que qualquer sensor.
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref mem))
        {
            snap.RamTotalGb = mem.ullTotalPhys / 1073741824f;
            snap.RamUsedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / 1073741824f;
        }

        snap.CoreLoads = coreLoads.ToArray();
        if (coreClocks.Count > 0)
        {
            snap.CpuClockAvg = coreClocks.Average();
            snap.CpuClockMax = coreClocks.Max();
        }
        snap.All = all;
        return snap;
    }

    private static string TypeName(SensorType t) => t switch
    {
        SensorType.Temperature => "Temperatura",
        SensorType.Load => "Uso",
        SensorType.Clock => "Clock",
        SensorType.Voltage => "Voltagem",
        SensorType.Power => "Consumo",
        SensorType.Fan => "Ventoinha",
        SensorType.Control => "Controle",
        SensorType.Data or SensorType.SmallData => "Dados",
        SensorType.Throughput => "Taxa",
        SensorType.Frequency => "Frequência",
        _ => t.ToString(),
    };

    private static string Unit(SensorType t) => t switch
    {
        SensorType.Temperature => "°C",
        SensorType.Load or SensorType.Control or SensorType.Level => "%",
        SensorType.Clock => "MHz",
        SensorType.Voltage => "V",
        SensorType.Power => "W",
        SensorType.Fan => "RPM",
        SensorType.Data => "GB",
        SensorType.SmallData => "MB",
        SensorType.Throughput => "B/s",
        SensorType.Frequency => "Hz",
        _ => "",
    };

    public void Dispose()
    {
        _cts.Cancel();
        _thread?.Join(2000);
        try { _computer.Close(); } catch { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
