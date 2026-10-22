using System.Runtime.InteropServices;

namespace BottleneckHunter.Core.Bench;

/// <summary>Memória física total e livre agora, direto do Windows.</summary>
public static class MemoryInfo
{
    public static long TotalMb => (long)(Read().ullTotalPhys / 1048576);

    public static long FreeMb
    {
        get
        {
            var m = Read();
            return (long)(m.ullAvailPhys / 1048576);
        }
    }

    private static MEMORYSTATUSEX Read()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref m) ? m : default;
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
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
}
