using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace BottleneckHunter.Core.Bench;

/// <summary>Um dos testes do benchmark de CPU, com a explicação que aparece na tela.</summary>
public sealed record CpuTestInfo(string Key, string Name, string Explain);

/// <summary>Andamento do benchmark, enviado várias vezes por segundo para a tela.</summary>
public sealed record CpuBenchProgress(int TestIndex, bool Multi, double Fraction, double LiveScore, double Overall);

/// <summary>Resultado final: nota de 1 núcleo, nota de todos os núcleos e a nota de cada teste.</summary>
public sealed class CpuBenchResult
{
    public int Threads { get; set; }
    public double SingleScore { get; set; }
    public double MultiScore { get; set; }
    public double[] Single { get; set; } = Array.Empty<double>();
    public double[] Multi { get; set; } = Array.Empty<double>();
    /// <summary>Falso se alguma conta deu resultado diferente do esperado (CPU instável).</summary>
    public bool ResultsOk { get; set; } = true;
    public double Scaling => SingleScore > 0 ? MultiScore / SingleScore : 0;
}

/// <summary>
/// Benchmark de CPU próprio do Bottleneck Hunter. Cada teste é dividido em "unidades" pequenas
/// (alguns milissegundos cada); a nota é quantas unidades por segundo a CPU termina.
/// Cada unidade devolve um número de conferência: se ele mudar de uma execução para outra,
/// a CPU errou uma conta, sinal clássico de overclock ou undervolt instável.
/// </summary>
public static class CpuBenchmark
{
    public static readonly CpuTestInfo[] Tests =
    {
        new("int", "Números inteiros",
            "Contas com números inteiros, como as usadas para compactar arquivos, criptografar dados e rodar a lógica dos jogos."),
        new("float", "Ponto flutuante",
            "Contas com casas decimais, como física de jogos e edição de foto. Neste teste a CPU desenha um fractal (a imagem ao lado)."),
        new("simd", "Vetores (SIMD/AVX)",
            "Instruções que fazem 8 ou 16 contas de uma vez só. São usadas em vídeo, IA, compressão e motores de jogos modernos."),
        new("sort", "Ordenação e decisões",
            "Muitos \"se isso, então aquilo\" e acessos à memória cache. Parecido com o trabalho que um jogo faz a cada quadro."),
    };

    /// <summary>Unidades por segundo que valem 1.000 pontos em cada teste (1.000 ≈ um núcleo de servidor na nuvem usado como referência).</summary>
    private static readonly double[] Reference = { 428, 169, 468, 684 };

    public static readonly TimeSpan PhaseTime = TimeSpan.FromSeconds(2.5);

    /// <summary>Imagem do fractal mais recente (largura × altura em contagens de iteração), para a tela mostrar ao vivo.</summary>
    public const int PreviewW = 160, PreviewH = 100;
    private static readonly int[] _preview = new int[PreviewW * PreviewH];
    private static int _previewFrame;
    public static int PreviewFrame => Volatile.Read(ref _previewFrame);

    public static void CopyPreview(int[] target)
    {
        lock (_preview) Array.Copy(_preview, target, _preview.Length);
    }

    public static CpuBenchResult Run(IProgress<CpuBenchProgress>? progress, CancellationToken ct)
    {
        int threads = Environment.ProcessorCount;
        var result = new CpuBenchResult
        {
            Threads = threads,
            Single = new double[Tests.Length],
            Multi = new double[Tests.Length],
        };

        // Respostas esperadas, calculadas uma vez antes de medir (também serve de aquecimento).
        var expected = new long[Tests.Length][];
        for (int t = 0; t < Tests.Length; t++)
        {
            var scratch = new Scratch();
            expected[t] = new long[Seeds];
            for (int s = 0; s < Seeds; s++) expected[t][s] = RunUnit(t, s, scratch, preview: false);
        }

        int phase = 0, phases = Tests.Length * 2;
        foreach (bool multi in new[] { false, true })
        {
            for (int t = 0; t < Tests.Length; t++)
            {
                ct.ThrowIfCancellationRequested();
                int ti = t;
                int p = phase;
                double score = Measure(t, multi ? threads : 1, expected[t], ref result, ct, live =>
                    progress?.Report(new CpuBenchProgress(ti, multi, live.Fraction, live.Score, (p + live.Fraction) / phases)));
                (multi ? result.Multi : result.Single)[t] = score;
                phase++;
            }
        }

        result.SingleScore = GeoMean(result.Single);
        result.MultiScore = GeoMean(result.Multi);
        progress?.Report(new CpuBenchProgress(Tests.Length - 1, true, 1, result.MultiScore, 1));
        return result;
    }

    private const int Seeds = 16;

    private static double Measure(int test, int threads, long[] expected, ref CpuBenchResult result, CancellationToken ct,
        Action<(double Fraction, double Score)> live)
    {
        long units = 0;
        bool ok = true;
        var start = new ManualResetEventSlim(false);
        var stopAt = 0L;
        var workers = new Thread[threads];

        for (int i = 0; i < threads; i++)
        {
            int id = i;
            workers[i] = new Thread(() =>
            {
                var scratch = new Scratch();
                int seed = id;
                start.Wait();
                while (Stopwatch.GetTimestamp() < Volatile.Read(ref stopAt) && !ct.IsCancellationRequested)
                {
                    int s = seed++ % Seeds;
                    long r = RunUnit(test, s, scratch, preview: id == 0);
                    if (r != expected[s]) ok = false;
                    Interlocked.Increment(ref units);
                }
            })
            { IsBackground = true, Name = "Bench" };
            workers[i].Start();
        }

        long t0 = Stopwatch.GetTimestamp();
        long len = (long)(PhaseTime.TotalSeconds * Stopwatch.Frequency);
        Volatile.Write(ref stopAt, t0 + len);
        start.Set();

        while (workers.Any(w => w.IsAlive))
        {
            Thread.Sleep(100);
            double elapsed = (Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency;
            if (elapsed > 0.2)
                live((Math.Min(1, elapsed / PhaseTime.TotalSeconds), Interlocked.Read(ref units) / elapsed / Reference[test] * 1000));
        }
        ct.ThrowIfCancellationRequested();

        double secs = (Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency;
        if (!ok) result.ResultsOk = false;
        return units / secs / Reference[test] * 1000;
    }

    private static double GeoMean(double[] v) =>
        v.Length == 0 || v.Any(x => x <= 0) ? 0 : Math.Exp(v.Average(Math.Log));

    /// <summary>Memória de trabalho de cada thread, criada uma vez para não medir o coletor de lixo.</summary>
    private sealed class Scratch
    {
        public readonly byte[] Bytes = new byte[32 * 1024];
        public readonly int[] Pixels = new int[PreviewW * PreviewH];
        public readonly float[] A = new float[4096], B = new float[4096];
        public readonly int[] Sort = new int[16384];
    }

    private static long RunUnit(int test, int seed, Scratch s, bool preview) => test switch
    {
        0 => IntegerUnit(seed, s.Bytes),
        1 => FloatUnit(seed, s.Pixels, preview),
        2 => SimdUnit(seed, s.A, s.B),
        _ => SortUnit(seed, s.Sort),
    };

    // ---------------------------------------------------------------- Inteiros

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }

    /// <summary>Gera dados pseudoaleatórios, calcula um CRC32 com tabela e um hash de mistura de 64 bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static long IntegerUnit(int seed, byte[] buf)
    {
        ulong x = 0x9E3779B97F4A7C15UL * (ulong)(seed + 1);
        for (int i = 0; i < buf.Length; i += 8)
        {
            x ^= x << 13; x ^= x >> 7; x ^= x << 17;
            BitConverter.TryWriteBytes(buf.AsSpan(i), x);
        }

        long total = 0;
        for (int round = 0; round < 24; round++)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in buf) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);

            ulong h = 0xCBF29CE484222325UL ^ (ulong)round;
            var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(buf);
            foreach (ulong w in words)
            {
                h ^= w;
                h *= 0x100000001B3UL;
                h = BitOperations.RotateLeft(h, 29) + (h >> 17);
            }
            total += (long)(h ^ crc);
        }
        return total;
    }

    // ---------------------------------------------------------------- Ponto flutuante

    /// <summary>Desenha um quadro do fractal de Mandelbrot com zoom que depende da semente.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static long FloatUnit(int seed, int[] pixels, bool preview)
    {
        const int maxIter = 200;
        double zoom = Math.Pow(1.45, seed);
        double cx = -0.743643887037151, cy = 0.131825904205330;
        double scale = 3.0 / (PreviewW * zoom);
        long sum = 0;

        for (int py = 0; py < PreviewH; py++)
        {
            double y0 = cy + (py - PreviewH / 2) * scale;
            for (int px = 0; px < PreviewW; px++)
            {
                double x0 = cx + (px - PreviewW / 2) * scale;
                double x = 0, y = 0, x2 = 0, y2 = 0;
                int it = 0;
                while (x2 + y2 <= 4 && it < maxIter)
                {
                    y = 2 * x * y + y0;
                    x = x2 - y2 + x0;
                    x2 = x * x;
                    y2 = y * y;
                    it++;
                }
                pixels[py * PreviewW + px] = it;
                sum += it;
            }
        }

        if (preview)
        {
            lock (_preview) Array.Copy(pixels, _preview, pixels.Length);
            Interlocked.Increment(ref _previewFrame);
        }
        return sum;
    }

    // ---------------------------------------------------------------- Vetores

    /// <summary>Multiplica e soma vetores inteiros de floats; o JIT usa SSE/AVX/AVX-512 conforme a CPU.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static long SimdUnit(int seed, float[] a, float[] b)
    {
        for (int i = 0; i < a.Length; i++)
        {
            a[i] = ((i * 7 + seed * 13) % 97) / 97f;
            b[i] = ((i * 11 + seed * 5) % 89) / 89f;
        }

        var va = System.Runtime.InteropServices.MemoryMarshal.Cast<float, Vector<float>>(a);
        var vb = System.Runtime.InteropServices.MemoryMarshal.Cast<float, Vector<float>>(b);
        var k1 = new Vector<float>(0.9993f);
        var k2 = new Vector<float>(0.0007f);
        var k3 = new Vector<float>(0.5f);

        for (int round = 0; round < 1440; round++)
        {
            for (int i = 0; i < va.Length; i++)
            {
                var x = va[i];
                var y = vb[i];
                x = x * k1 + y * k2;
                y = y * k1 + (x * k3) * k2;
                x = x * k1 + y * k2;
                y = Vector.SquareRoot(y * x + k2);
                va[i] = x;
                vb[i] = y;
            }
        }

        double total = 0;
        for (int i = 0; i < a.Length; i += 64) total += a[i] + b[i];
        return BitConverter.DoubleToInt64Bits(total);
    }

    // ---------------------------------------------------------------- Ordenação

    /// <summary>Ordena números aleatórios e faz buscas binárias: muitos desvios difíceis de prever.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static long SortUnit(int seed, int[] arr)
    {
        uint x = (uint)(seed * 2654435761u + 12345);
        for (int i = 0; i < arr.Length; i++)
        {
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            arr[i] = (int)(x & 0x7FFFFFFF);
        }
        Array.Sort(arr);

        long found = 0;
        for (int i = 0; i < 4096; i++)
        {
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            int idx = Array.BinarySearch(arr, (int)(x & 0x7FFFFFFF));
            found += idx >= 0 ? idx : ~idx;
        }
        return found + arr[arr.Length / 2];
    }
}
