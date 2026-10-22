namespace BottleneckHunter.Core.Bottleneck;

/// <summary>O que está limitando o desempenho num dado instante.</summary>
public enum Limiter
{
    Idle,
    Gpu,
    Cpu,
    Vram,
    Ram,
    Thermal,
    FpsCap,
}

public sealed record LimiterInfo(Limiter Kind, string Name, string BrushKey, string Icon, string Explanation, string[] Tips);

/// <summary>Uma leitura instantânea: quem limita agora e por quê.</summary>
public sealed record LiveVerdict(Limiter Kind, string Title, string Detail);

/// <summary>Resultado de uma sessão gravada.</summary>
public sealed class SessionReport
{
    public TimeSpan Duration { get; init; }
    public int Samples { get; init; }
    public int ActiveSamples { get; init; }
    public Dictionary<Limiter, double> Share { get; init; } = new();
    public Limiter Main { get; init; }
    public double AvgCpu { get; init; }
    public double AvgMaxCore { get; init; }
    public double AvgGpu { get; init; }
    public double? PeakCpuTemp { get; init; }
    public double? PeakGpuTemp { get; init; }
    public double PeakRam { get; init; }
    public double? PeakVram { get; init; }
    public bool MostlyIdle => Samples > 0 && ActiveSamples < Samples * 0.3;
}

/// <summary>
/// Regras para descobrir o gargalo. A ideia central: num jogo, a GPU deveria estar perto de 100%.
/// Se não está, alguém está segurando: CPU (normalmente um núcleo só), memória, temperatura ou um limite de FPS.
/// </summary>
public static class BottleneckAnalyzer
{
    public const float GpuBoundAt = 92;
    public const float CpuBoundCoreAt = 85;
    public const float VramFullAt = 95;
    public const float RamFullAt = 92;
    public const float CpuHotAt = 95;
    public const float GpuHotAt = 88;
    public const float IdleBelow = 30;

    public static readonly IReadOnlyDictionary<Limiter, LimiterInfo> Info = new Dictionary<Limiter, LimiterInfo>
    {
        [Limiter.Gpu] = new(Limiter.Gpu, "Placa de vídeo (GPU)", "Gpu", "",
            "A placa de vídeo está trabalhando no máximo. Em jogos isso é o ideal: quer dizer que o resto do PC dá conta de alimentá-la.",
            new[]
            {
                "Quer mais FPS? Baixe a resolução ou a qualidade gráfica (sombras e iluminação pesam mais).",
                "Ative DLSS, FSR ou XeSS no jogo: renderizam em resolução menor e reconstroem a imagem.",
                "Se ainda for pouco, o próximo upgrade é a placa de vídeo.",
            }),
        [Limiter.Cpu] = new(Limiter.Cpu, "Processador (CPU)", "Cpu", "",
            "O processador não está conseguindo preparar os quadros tão rápido quanto a placa de vídeo desenha. Repare que muitas vezes é UM núcleo que chega a 100%, mesmo com o uso total baixo: a maioria dos jogos depende de uma thread principal.",
            new[]
            {
                "Feche programas em segundo plano (navegador, Discord com vídeo, launchers).",
                "Aumente a qualidade gráfica ou a resolução: a GPU passa a trabalhar mais sem custar CPU, e a imagem fica mais bonita de graça.",
                "Ative o XMP/EXPO na BIOS: memória mais rápida ajuda muito o processador em jogos.",
                "Limite o FPS um pouco abaixo da média para deixar o frametime mais estável.",
                "Se nada disso resolver, o próximo upgrade é o processador.",
            }),
        [Limiter.Vram] = new(Limiter.Vram, "Memória de vídeo (VRAM)", "Warn", "",
            "A memória da placa de vídeo encheu. As texturas que não cabem vão para a RAM, que é muito mais lenta, e o jogo dá travadas.",
            new[]
            {
                "Baixe a qualidade das texturas: é a opção que mais usa VRAM.",
                "Desative ray tracing e geração de quadros se o jogo estiver no limite.",
                "Resoluções altas (1440p/4K) usam bem mais VRAM.",
            }),
        [Limiter.Ram] = new(Limiter.Ram, "Memória RAM", "Warn", "",
            "A memória RAM está quase cheia. O Windows começa a usar o disco como memória, o que causa engasgos.",
            new[]
            {
                "Feche programas pesados enquanto joga (navegador com muitas abas é o campeão).",
                "Para jogos atuais, 16 GB é o mínimo confortável e 32 GB é o ideal.",
                "Use dois pentes iguais (dual channel) em vez de um só.",
            }),
        [Limiter.Thermal] = new(Limiter.Thermal, "Temperatura", "Bad", "",
            "Uma peça esquentou demais. Para se proteger, ela reduz o próprio clock (thermal throttling) e o desempenho cai.",
            new[]
            {
                "Limpe a poeira dos filtros, ventoinhas e dissipadores.",
                "Confira se as ventoinhas do gabinete estão girando e na direção certa (entrada na frente, saída atrás/em cima).",
                "Trocar a pasta térmica de um PC com alguns anos costuma baixar vários graus.",
                "Em notebook, use em superfície dura ou com base refrigerada.",
            }),
        [Limiter.FpsCap] = new(Limiter.FpsCap, "Limite de FPS", "TextDim", "",
            "Nenhuma peça está no limite. O FPS provavelmente está travado por V-Sync, por um limite no jogo/driver, ou o jogo está esperando rede ou disco.",
            new[]
            {
                "Isso não é ruim: menos calor, menos barulho e frametime estável.",
                "Se quiser mais FPS, desative o V-Sync ou aumente o limite de FPS no jogo.",
                "O ideal é limitar o FPS na taxa do monitor (ex.: 144 Hz → 141 FPS com G-Sync/FreeSync).",
            }),
        [Limiter.Idle] = new(Limiter.Idle, "Sem carga", "Good", "",
            "O PC está tranquilo, nada pesado rodando.",
            new[] { "Abra um jogo e volte aqui (ou grave uma sessão) para descobrir quem limita o desempenho." }),
    };

    public static Limiter Classify(HwSnapshot s)
    {
        float cpu = s.CpuLoad ?? 0;
        float core = s.MaxCoreLoad ?? cpu;
        float gpu = s.GpuLoad ?? 0;

        if (s.CpuTemp >= CpuHotAt || s.GpuTemp >= GpuHotAt) return Limiter.Thermal;
        if (gpu < IdleBelow && cpu < IdleBelow && core < 60) return Limiter.Idle;
        if (s.VramPercent >= VramFullAt) return Limiter.Vram;
        if (s.RamPercent >= RamFullAt) return Limiter.Ram;
        if (gpu >= GpuBoundAt) return Limiter.Gpu;
        if (core >= CpuBoundCoreAt || cpu >= CpuBoundCoreAt) return Limiter.Cpu;
        return Limiter.FpsCap;
    }

    public static LiveVerdict Live(HwSnapshot s)
    {
        if (s.GpuLoad == null && s.CpuLoad == null)
            return new LiveVerdict(Limiter.Idle, "Lendo sensores...", "Aguarde alguns segundos.");

        var kind = Classify(s);
        float cpu = s.CpuLoad ?? 0, core = s.MaxCoreLoad ?? cpu, gpu = s.GpuLoad ?? 0;
        string detail = kind switch
        {
            Limiter.Gpu => $"GPU em {gpu:0}%. Ela é quem define o FPS agora.",
            Limiter.Cpu => $"Núcleo mais ocupado da CPU em {core:0}% enquanto a GPU está em {gpu:0}% (sobra placa de vídeo).",
            Limiter.Vram => $"VRAM em {s.VramPercent:0}% de uso.",
            Limiter.Ram => $"RAM em {s.RamPercent:0}% de uso ({s.RamUsedGb:0.0} de {s.RamTotalGb:0} GB).",
            Limiter.Thermal => $"CPU a {s.CpuTemp?.ToString("0") ?? "—"} °C e GPU a {s.GpuTemp?.ToString("0") ?? "—"} °C.",
            Limiter.FpsCap => $"CPU em {cpu:0}% (núcleo máx. {core:0}%) e GPU em {gpu:0}%: ninguém no limite.",
            _ => "Nada pesado rodando agora.",
        };
        string title = kind switch
        {
            Limiter.Gpu => "A GPU está dando tudo",
            Limiter.Cpu => "Gargalo no processador",
            Limiter.Vram => "Memória de vídeo cheia",
            Limiter.Ram => "Memória RAM quase cheia",
            Limiter.Thermal => "Temperatura alta",
            Limiter.FpsCap => "FPS limitado (sem gargalo de hardware)",
            _ => "PC tranquilo",
        };
        return new LiveVerdict(kind, title, detail);
    }

    public static SessionReport Summarize(IReadOnlyList<HwSnapshot> samples)
    {
        var active = samples.Select(s => (s, k: Classify(s))).Where(x => x.k != Limiter.Idle).ToList();
        var basis = active.Count > 0 ? active : samples.Select(s => (s, k: Limiter.Idle)).ToList();

        var share = basis.GroupBy(x => x.k).ToDictionary(g => g.Key, g => g.Count() * 100.0 / Math.Max(1, basis.Count));
        var main = share.Count > 0 ? share.MaxBy(kv => kv.Value).Key : Limiter.Idle;

        double Avg(Func<HwSnapshot, float?> f) => basis.Count == 0 ? 0 : basis.Average(x => f(x.s) ?? 0);
        double? Peak(Func<HwSnapshot, float?> f)
        {
            var values = samples.Select(f).Where(v => v.HasValue).ToList();
            return values.Count == 0 ? null : values.Max();
        }

        return new SessionReport
        {
            Duration = samples.Count > 1 ? samples[^1].Time - samples[0].Time : TimeSpan.Zero,
            Samples = samples.Count,
            ActiveSamples = active.Count,
            Share = share,
            Main = main,
            AvgCpu = Avg(s => s.CpuLoad),
            AvgMaxCore = Avg(s => s.MaxCoreLoad),
            AvgGpu = Avg(s => s.GpuLoad),
            PeakCpuTemp = Peak(s => s.CpuTemp),
            PeakGpuTemp = Peak(s => s.GpuTemp),
            PeakRam = samples.Count == 0 ? 0 : samples.Max(s => s.RamPercent),
            PeakVram = Peak(s => s.VramPercent),
        };
    }
}
