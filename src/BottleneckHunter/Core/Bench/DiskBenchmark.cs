using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BottleneckHunter.Core.Bench;

/// <summary>Resultado do teste de disco.</summary>
public sealed class DiskBenchResult
{
    public string Drive { get; set; } = "";
    public double SeqReadMBs { get; set; }
    public double SeqWriteMBs { get; set; }
    public double RandReadIops { get; set; }
    public double RandWriteIops { get; set; }
    public double RandReadMBs => RandReadIops * 4096 / 1e6;
    public double RandWriteMBs => RandWriteIops * 4096 / 1e6;
}

/// <summary>
/// Mede o disco com um arquivo de teste temporário: leitura e escrita sequenciais (blocos de 1 MB, como copiar um filme)
/// e aleatórias (blocos de 4 KB, como abrir programas e carregar jogos).
/// O cache do Windows é desligado para medir o disco de verdade, e o arquivo é apagado sozinho no fim.
/// </summary>
public static unsafe class DiskBenchmark
{
    public static readonly string[] Steps = { "Escrita sequencial", "Leitura sequencial", "Leitura aleatória 4K", "Escrita aleatória 4K" };

    // Sem cache do Windows. O .NET aceita este valor (FILE_FLAG_NO_BUFFERING) em FileOptions.
    private const FileOptions NoBuffering = (FileOptions)0x20000000;
    private const int Block = 1024 * 1024;
    private const int Small = 4096;

    /// <summary>Unidades com espaço livre suficiente para o teste.</summary>
    public static List<DriveInfo> Drives(long fileBytes)
    {
        var list = new List<DriveInfo>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable && d.AvailableFreeSpace > fileBytes * 2)
                    list.Add(d);
            }
            catch
            {
                // Unidade que some no meio da listagem (pendrive removido): ignora.
            }
        }
        return list;
    }

    /// <summary>
    /// Pasta do arquivo de teste. Na unidade do Windows usamos a pasta do app em %LocalAppData%;
    /// nas outras, a raiz da unidade. O nome é aleatório e criado com CreateNew, então nunca
    /// sobrescreve um arquivo ou link que já esteja lá.
    /// </summary>
    private static string TestPath(DriveInfo drive)
    {
        string sysRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        string folder = string.Equals(drive.RootDirectory.FullName, sysRoot, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BottleneckHunter")
            : drive.RootDirectory.FullName;
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, $"BottleneckHunter-teste-{Guid.NewGuid():N}.tmp");
    }

    public static DiskBenchResult Run(DriveInfo drive, long fileBytes, Action<int, double>? progress, CancellationToken ct)
    {
        fileBytes = fileBytes / Block * Block;
        var result = new DiskBenchResult { Drive = drive.Name };
        string path = TestPath(drive);
        byte* buf = (byte*)NativeMemory.AlignedAlloc(Block, 4096);
        try
        {
            // Dados aleatórios: alguns SSDs compactam dados repetidos e mostrariam números irreais.
            Random.Shared.NextBytes(new Span<byte>(buf, Block));

            using var h = File.OpenHandle(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                FileOptions.WriteThrough | NoBuffering | FileOptions.DeleteOnClose, fileBytes);

            result.SeqWriteMBs = Sequential(h, buf, fileBytes, write: true, p => progress?.Invoke(0, p), ct);
            result.SeqReadMBs = Sequential(h, buf, fileBytes, write: false, p => progress?.Invoke(1, p), ct);
            result.RandReadIops = Random4K(h, buf, fileBytes, write: false, p => progress?.Invoke(2, p), ct);
            result.RandWriteIops = Random4K(h, buf, fileBytes, write: true, p => progress?.Invoke(3, p), ct);
            progress?.Invoke(3, 1);
            return result;
        }
        finally
        {
            NativeMemory.AlignedFree(buf);
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    private static double Sequential(SafeFileHandle h, byte* buf, long size, bool write, Action<double> progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        long done = 0;
        for (long off = 0; off < size; off += Block)
        {
            ct.ThrowIfCancellationRequested();
            if (write) RandomAccess.Write(h, new ReadOnlySpan<byte>(buf, Block), off);
            else RandomAccess.Read(h, new Span<byte>(buf, Block), off);
            done += Block;
            if ((off / Block) % 16 == 0) progress((double)done / size);
        }
        return done / sw.Elapsed.TotalSeconds / 1e6;
    }

    private static double Random4K(SafeFileHandle h, byte* buf, long size, bool write, Action<double> progress, CancellationToken ct)
    {
        const double seconds = 5;
        long blocks = size / Small;
        var rng = new Random(42);
        var sw = Stopwatch.StartNew();
        long ops = 0;
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            ct.ThrowIfCancellationRequested();
            long off = rng.NextInt64(blocks) * Small;
            if (write) RandomAccess.Write(h, new ReadOnlySpan<byte>(buf, Small), off);
            else RandomAccess.Read(h, new Span<byte>(buf, Small), off);
            ops++;
            if (ops % 64 == 0) progress(Math.Min(1, sw.Elapsed.TotalSeconds / seconds));
        }
        return ops / sw.Elapsed.TotalSeconds;
    }
}
