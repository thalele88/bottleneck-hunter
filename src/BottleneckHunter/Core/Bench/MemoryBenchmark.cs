using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace BottleneckHunter.Core.Bench;

/// <summary>Resultado do teste de memória RAM.</summary>
public sealed class MemoryBenchResult
{
    public double ReadGBs { get; set; }
    public double WriteGBs { get; set; }
    public double CopyGBs { get; set; }
    public double LatencyNs { get; set; }
}

/// <summary>
/// Mede a velocidade da memória RAM: leitura, escrita, cópia (GB/s, com todos os núcleos)
/// e latência (quanto tempo a CPU espera por um dado que não está no cache).
/// Usa memória nativa (fora do coletor de lixo) e blocos bem maiores que o cache da CPU.
/// </summary>
public static unsafe class MemoryBenchmark
{
    public static readonly string[] Steps = { "Leitura", "Escrita", "Cópia", "Latência" };

    public static MemoryBenchResult Run(Action<int, double>? progress, CancellationToken ct)
    {
        // 512 MB é bem maior que qualquer cache L3 atual. Com pouca RAM livre, usamos menos.
        nuint size = (nuint)(SystemFreeMb() > 3000 ? 512 : 256) * 1024 * 1024;
        int threads = Environment.ProcessorCount;
        byte* buf = (byte*)NativeMemory.AlignedAlloc(size, 4096);
        try
        {
            // Encosta em todas as páginas antes de medir, para o Windows já ter entregue a memória.
            new Span<byte>(buf, (int)Math.Min(size, int.MaxValue)).Fill(1);
            if (size > int.MaxValue) new Span<byte>(buf + int.MaxValue, (int)(size - int.MaxValue)).Fill(1);

            var r = new MemoryBenchResult();
            nuint half = size / 2;
            r.ReadGBs = Best(threads, size, (p, n) => ReadSlice(buf + p, n), 0, progress, ct);
            r.WriteGBs = Best(threads, size, (p, n) => WriteSlice(buf + p, n), 1, progress, ct);
            r.CopyGBs = Best(threads, half, (p, n) => Buffer.MemoryCopy(buf + p, buf + half + p, (long)n, (long)n), 2, progress, ct);
            r.LatencyNs = Latency(buf, size / 2, p => progress?.Invoke(3, p), ct);
            progress?.Invoke(3, 1);
            return r;
        }
        finally
        {
            NativeMemory.AlignedFree(buf);
        }
    }

    /// <summary>Repete a operação em todos os núcleos por cerca de 1 s e devolve a melhor passada em GB/s.</summary>
    private static double Best(int threads, nuint total, Action<nuint, nuint> work, int step, Action<int, double>? progress, CancellationToken ct)
    {
        double best = 0;
        var sw = Stopwatch.StartNew();
        nuint slice = total / (nuint)threads / 64 * 64;
        while (sw.Elapsed.TotalSeconds < 1.2)
        {
            ct.ThrowIfCancellationRequested();
            long t0 = Stopwatch.GetTimestamp();
            Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, i => work((nuint)i * slice, slice));
            double secs = (Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency;
            best = Math.Max(best, slice * (double)threads / secs / 1e9);
            progress?.Invoke(step, Math.Min(1, sw.Elapsed.TotalSeconds / 1.2));
        }
        return best;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ReadSlice(byte* p, nuint n)
    {
        if (Vector256.IsHardwareAccelerated)
        {
            var a = Vector256<long>.Zero;
            var b = Vector256<long>.Zero;
            for (nuint i = 0; i + 64 <= n; i += 64)
            {
                a += Vector256.Load((long*)(p + i));
                b += Vector256.Load((long*)(p + i + 32));
            }
            Sink = (a + b).GetElement(0);
        }
        else
        {
            long s = 0;
            for (nuint i = 0; i + 8 <= n; i += 8) s += *(long*)(p + i);
            Sink = s;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void WriteSlice(byte* p, nuint n)
    {
        if (Avx.IsSupported)
        {
            // Escrita "não temporal": vai direto para a RAM sem sujar o cache, como os testes profissionais fazem.
            var v = Vector256.Create(0x5A5A5A5A5A5A5A5AL).AsByte();
            for (nuint i = 0; i + 64 <= n; i += 64)
            {
                Avx.StoreAlignedNonTemporal(p + i, v);
                Avx.StoreAlignedNonTemporal(p + i + 32, v);
            }
            Sse.StoreFence();
        }
        else
        {
            new Span<byte>(p, (int)n).Fill(0x5A);
        }
    }

    private static long Sink;

    /// <summary>
    /// Latência: percorre a memória numa ordem aleatória em que cada endereço depende do anterior,
    /// então a CPU não consegue adiantar nada e espera a RAM a cada passo.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static double Latency(byte* buf, nuint size, Action<double> progress, CancellationToken ct)
    {
        const int line = 64;
        int count = (int)(size / line);
        var order = new int[count];
        for (int i = 0; i < count; i++) order[i] = i;

        // Embaralha em um único ciclo (algoritmo de Sattolo): visita todas as linhas antes de repetir.
        var rng = new Random(1234);
        for (int i = count - 1; i > 0; i--)
        {
            int j = rng.Next(i);
            (order[i], order[j]) = (order[j], order[i]);
        }
        for (int i = 0; i < count; i++)
            *(nint*)(buf + (nuint)order[i] * line) = (nint)(buf + (nuint)order[(i + 1) % count] * line);

        nint cur = (nint)buf;
        const int steps = 2_000_000;
        double best = double.MaxValue;
        for (int pass = 0; pass < 5; pass++)
        {
            ct.ThrowIfCancellationRequested();
            long t0 = Stopwatch.GetTimestamp();
            for (int i = 0; i < steps; i++) cur = *(nint*)cur;
            double ns = (Stopwatch.GetTimestamp() - t0) * 1e9 / Stopwatch.Frequency / steps;
            best = Math.Min(best, ns);
            progress((pass + 1) / 5.0);
        }
        Sink = cur;
        return best;
    }

    private static double SystemFreeMb() => MemoryInfo.FreeMb;
}
