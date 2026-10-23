using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace BottleneckHunter.Core.Bench;

/// <summary>
/// Stress de CPU: coloca todos os núcleos escolhidos em 100% com contas pesadas e confere cada resultado.
/// Se uma conta der diferente da resposta certa, a CPU está instável (overclock/undervolt agressivo,
/// temperatura alta demais ou fonte de energia fraca).
/// </summary>
public sealed class CpuStress
{
    private const int Seeds = 8;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<Thread> _threads = new();
    private long _rounds;
    private long _errors;

    public long Rounds => Interlocked.Read(ref _rounds);
    public long Errors => Interlocked.Read(ref _errors);
    public int Threads => _threads.Count;
    /// <summary>"AVX2 + FMA", "AVX" ou "SSE": o conjunto de instruções usado no modo pesado.</summary>
    public static string HeavyKind => Fma.IsSupported && Avx2.IsSupported ? "AVX2 + FMA" : Avx.IsSupported ? "AVX" : "SSE";

    /// <param name="threads">Quantas threads (normalmente todas as lógicas).</param>
    /// <param name="heavy">Contas vetoriais com FMA: esquentam muito mais. Desligado, usa só inteiros.</param>
    public void Start(int threads, bool heavy)
    {
        // As respostas certas são calculadas antes de a CPU esquentar.
        var expected = new double[Seeds];
        for (int s = 0; s < Seeds; s++) expected[s] = heavy ? HeavyKernel(s) : IntKernel(s);

        for (int i = 0; i < threads; i++)
        {
            int id = i;
            var t = new Thread(() =>
            {
                int seed = id;
                while (!_cts.IsCancellationRequested)
                {
                    int s = seed++ % Seeds;
                    double r = heavy ? HeavyKernel(s) : IntKernel(s);
                    if (BitConverter.DoubleToInt64Bits(r) != BitConverter.DoubleToInt64Bits(expected[s]))
                        Interlocked.Increment(ref _errors);
                    Interlocked.Increment(ref _rounds);
                }
            })
            {
                IsBackground = true,
                Name = "CpuStress",
                // Prioridade mínima: a CPU fica em 100%, mas a janela do app continua respondendo.
                Priority = ThreadPriority.Lowest,
            };
            _threads.Add(t);
            t.Start();
        }
    }

    public void Stop()
    {
        _cts.Cancel();
        foreach (var t in _threads) t.Join(2000);
    }

    /// <summary>Muitas multiplicações-e-somas em vetores de 256 bits: o tipo de conta que mais esquenta a CPU.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static double HeavyKernel(int seed)
    {
        if (Fma.IsSupported)
        {
            var m = Vector256.Create(0.999999);
            var c = Vector256.Create(1e-7 * (seed + 1));
            var a0 = Vector256.Create(1.0 + seed * 0.01);
            var a1 = a0 + Vector256.Create(0.1);
            var a2 = a0 + Vector256.Create(0.2);
            var a3 = a0 + Vector256.Create(0.3);
            var a4 = a0 + Vector256.Create(0.4);
            var a5 = a0 + Vector256.Create(0.5);
            var a6 = a0 + Vector256.Create(0.6);
            var a7 = a0 + Vector256.Create(0.7);
            for (int i = 0; i < 400_000; i++)
            {
                a0 = Fma.MultiplyAdd(a0, m, c); a1 = Fma.MultiplyAdd(a1, m, c);
                a2 = Fma.MultiplyAdd(a2, m, c); a3 = Fma.MultiplyAdd(a3, m, c);
                a4 = Fma.MultiplyAdd(a4, m, c); a5 = Fma.MultiplyAdd(a5, m, c);
                a6 = Fma.MultiplyAdd(a6, m, c); a7 = Fma.MultiplyAdd(a7, m, c);
            }
            var sum = ((a0 + a1) + (a2 + a3)) + ((a4 + a5) + (a6 + a7));
            return sum.GetElement(0) + sum.GetElement(1) + sum.GetElement(2) + sum.GetElement(3);
        }
        else
        {
            var m = Vector128.Create(0.999999);
            var c = Vector128.Create(1e-7 * (seed + 1));
            var a0 = Vector128.Create(1.0 + seed * 0.01);
            var a1 = a0 + Vector128.Create(0.1);
            var a2 = a0 + Vector128.Create(0.2);
            var a3 = a0 + Vector128.Create(0.3);
            for (int i = 0; i < 400_000; i++)
            {
                a0 = a0 * m + c; a1 = a1 * m + c;
                a2 = a2 * m + c; a3 = a3 * m + c;
            }
            var sum = (a0 + a1) + (a2 + a3);
            return sum.GetElement(0) + sum.GetElement(1);
        }
    }

    /// <summary>Contas só com inteiros (multiplicações, rotações, xor): aquece menos, bom para testar estabilidade básica.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static double IntKernel(int seed)
    {
        ulong a = 0x9E3779B97F4A7C15UL + (ulong)seed, b = 0xC2B2AE3D27D4EB4FUL, c = 0x165667B19E3779F9UL, d = (ulong)seed * 31 + 7;
        for (int i = 0; i < 1_500_000; i++)
        {
            a = System.Numerics.BitOperations.RotateLeft(a * 0xFF51AFD7ED558CCDUL, 31) ^ b;
            b = System.Numerics.BitOperations.RotateLeft(b + c, 17) * 5;
            c ^= a + (d >> 3);
            d = d * 6364136223846793005UL + 1442695040888963407UL;
        }
        return a ^ b ^ c ^ d;
    }
}
