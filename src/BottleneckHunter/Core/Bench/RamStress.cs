using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BottleneckHunter.Core.Bench;

/// <summary>
/// Teste de memória RAM: ocupa uma parte da RAM livre, escreve padrões conhecidos e lê de volta conferindo bit a bit.
/// Qualquer diferença é um erro de memória (pente com defeito, XMP/EXPO instável ou mal encaixado).
/// </summary>
public sealed unsafe class RamStress
{
    private const nuint Chunk = 128 * 1024 * 1024;
    private readonly List<nint> _chunks = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly List<Thread> _threads = new();
    private long _bytesChecked;
    private long _errors;
    private int _passes;

    public long BytesChecked => Interlocked.Read(ref _bytesChecked);
    public long Errors => Interlocked.Read(ref _errors);
    /// <summary>Quantas vezes toda a memória reservada foi escrita e conferida.</summary>
    public int Passes => Volatile.Read(ref _passes);
    public long AllocatedBytes => _chunks.Count * (long)Chunk;

    /// <summary>Quantos MB de RAM estão livres agora.</summary>
    public static long FreeMb() => MemoryInfo.FreeMb;

    /// <param name="megabytes">Quanto reservar. O Windows precisa de folga, então limite a ~85% da RAM livre.</param>
    public void Start(long megabytes, int threads)
    {
        long chunks = Math.Max(1, megabytes * 1024 * 1024 / (long)Chunk);
        for (long i = 0; i < chunks; i++)
        {
            try { _chunks.Add((nint)NativeMemory.AlignedAlloc(Chunk, 4096)); }
            catch (OutOfMemoryException) { break; }
        }
        if (_chunks.Count == 0) throw new OutOfMemoryException("Não há memória livre suficiente para o teste.");

        threads = Math.Clamp(threads, 1, _chunks.Count);
        int perPassDone = 0;
        for (int i = 0; i < threads; i++)
        {
            int id = i;
            var t = new Thread(() =>
            {
                int pass = 0;
                while (!_cts.IsCancellationRequested)
                {
                    for (int c = id; c < _chunks.Count && !_cts.IsCancellationRequested; c += threads)
                    {
                        long bad = Pass((byte*)_chunks[c], Chunk, pass, (ulong)c);
                        if (bad > 0) Interlocked.Add(ref _errors, bad);
                        Interlocked.Add(ref _bytesChecked, (long)Chunk);
                    }
                    pass++;
                    if (Interlocked.Increment(ref perPassDone) % threads == 0) Interlocked.Increment(ref _passes);
                }
            })
            { IsBackground = true, Name = "RamStress", Priority = ThreadPriority.Lowest };
            _threads.Add(t);
            t.Start();
        }
    }

    public void Stop()
    {
        _cts.Cancel();
        // Espera de verdade: liberar a memória com uma thread ainda escrevendo nela derrubaria o app.
        foreach (var t in _threads) t.Join();
        foreach (var p in _chunks) NativeMemory.AlignedFree((void*)p);
        _chunks.Clear();
    }

    /// <summary>Escreve um padrão no bloco inteiro e confere. O padrão muda a cada passada.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static long Pass(byte* p, nuint size, int pass, ulong salt)
    {
        ulong* w = (ulong*)p;
        nuint n = size / 8;
        long errors = 0;

        switch (pass % 4)
        {
            case 0: // Endereço dentro do endereço: pega linhas de endereço trocadas.
                for (nuint i = 0; i < n; i++) w[i] = (ulong)(w + i);
                for (nuint i = 0; i < n; i++) if (w[i] != (ulong)(w + i)) errors++;
                break;
            case 1: // Bits alternados 1010... e 0101...: pega bits vizinhos que interferem um no outro.
                for (nuint i = 0; i < n; i++) w[i] = (i & 1) == 0 ? 0xAAAAAAAAAAAAAAAAUL : 0x5555555555555555UL;
                for (nuint i = 0; i < n; i++) if (w[i] != ((i & 1) == 0 ? 0xAAAAAAAAAAAAAAAAUL : 0x5555555555555555UL)) errors++;
                break;
            default: // Números aleatórios (e o inverso deles): pega falhas que só aparecem com dados variados.
                bool invert = pass % 4 == 3;
                ulong x = 0x9E3779B97F4A7C15UL ^ (salt * 0xBF58476D1CE4E5B9UL) ^ (ulong)pass;
                for (nuint i = 0; i < n; i++)
                {
                    x ^= x << 13; x ^= x >> 7; x ^= x << 17;
                    w[i] = invert ? ~x : x;
                }
                x = 0x9E3779B97F4A7C15UL ^ (salt * 0xBF58476D1CE4E5B9UL) ^ (ulong)pass;
                for (nuint i = 0; i < n; i++)
                {
                    x ^= x << 13; x ^= x >> 7; x ^= x << 17;
                    if (w[i] != (invert ? ~x : x)) errors++;
                }
                break;
        }
        return errors;
    }
}
