using System.Runtime.InteropServices;

namespace BottleneckHunter.Core.SystemInfo;

/// <summary>
/// Lê os monitores ligados: resolução, taxa de atualização atual e a maior que o monitor aceita.
/// Assim dá para avisar o clássico "monitor de 144 Hz rodando a 60 Hz".
/// </summary>
internal static class DisplayReader
{
    public static List<DisplayInfo> Read()
    {
        var names = FriendlyNames();
        var list = new List<DisplayInfo>();

        for (uint i = 0; ; i++)
        {
            var adapter = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, i, ref adapter, 0)) break;
            if ((adapter.StateFlags & AttachedToDesktop) == 0) continue;

            var current = NewMode();
            if (!EnumDisplaySettings(adapter.DeviceName, EnumCurrentSettings, ref current)) continue;

            int max = current.dmDisplayFrequency;
            var mode = NewMode();
            for (int m = 0; EnumDisplaySettings(adapter.DeviceName, m, ref mode); m++)
            {
                if (mode.dmPelsWidth == current.dmPelsWidth && mode.dmPelsHeight == current.dmPelsHeight)
                    max = Math.Max(max, mode.dmDisplayFrequency);
            }

            // Nome real do monitor (ex.: "LG ULTRAGEAR"), quando o Windows informa.
            string name = "";
            var monitor = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, GetDeviceInterfaceName))
            {
                var key = InterfaceToInstance(monitor.DeviceID);
                name = names.FirstOrDefault(n => key.Length > 0 && n.Key.StartsWith(key, StringComparison.OrdinalIgnoreCase)).Value ?? "";
                if (name.Length == 0 && !monitor.DeviceString.Contains("Generic", StringComparison.OrdinalIgnoreCase)
                    && !monitor.DeviceString.Contains("PnP", StringComparison.OrdinalIgnoreCase))
                    name = monitor.DeviceString;
            }

            list.Add(new DisplayInfo
            {
                Name = name.Length > 0 ? name : $"Monitor {list.Count + 1}",
                Width = current.dmPelsWidth,
                Height = current.dmPelsHeight,
                RefreshHz = current.dmDisplayFrequency,
                MaxRefreshHz = max,
                IsPrimary = (adapter.StateFlags & PrimaryDevice) != 0,
            });
        }
        return list.OrderByDescending(d => d.IsPrimary).ToList();
    }

    /// <summary>"\\?\DISPLAY#GSM5B7F#4&amp;1a2b&amp;0&amp;UID4353#{guid}" → "DISPLAY\GSM5B7F\4&amp;1a2b&amp;0&amp;UID4353".</summary>
    private static string InterfaceToInstance(string id)
    {
        var parts = id.Split('#');
        return parts.Length >= 4 ? $"{parts[0].Replace(@"\\?\", "")}\\{parts[1]}\\{parts[2]}" : "";
    }

    /// <summary>Nomes gravados no próprio monitor (EDID), por InstanceName do WMI.</summary>
    private static Dictionary<string, string> FriendlyNames()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var row in Wmi.Query("SELECT InstanceName, UserFriendlyName FROM WmiMonitorID", Wmi.RootWmi, 8))
            {
                if (row["UserFriendlyName"] is not ushort[] chars) continue;
                var name = new string(chars.TakeWhile(c => c != 0).Select(c => (char)c).ToArray()).Trim();
                if (name.Length > 0) result[row.Str("InstanceName")] = name;
            }
        }
        catch { }
        return result;
    }

    private static DEVMODE NewMode() => new() { dmSize = (short)Marshal.SizeOf<DEVMODE>() };

    private const int EnumCurrentSettings = -1;
    private const int AttachedToDesktop = 0x1;
    private const int PrimaryDevice = 0x4;
    private const uint GetDeviceInterfaceName = 0x1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);
}
