namespace BottleneckHunter.Core.Bench;

/// <summary>O que o usuário escolheu antes de começar o teste de estabilidade.</summary>
public sealed class StressOptions
{
    public bool Cpu { get; set; } = true;
    public bool CpuHeavy { get; set; } = true;
    public int CpuThreads { get; set; } = Environment.ProcessorCount;
    public bool Ram { get; set; }
    public long RamMb { get; set; }
    public bool Gpu { get; set; }
    /// <summary>Duração; nulo = até o usuário parar.</summary>
    public TimeSpan? Duration { get; set; } = TimeSpan.FromMinutes(5);
    public float CpuTempLimit { get; set; } = 90;
    public float GpuTempLimit { get; set; } = 87;
}

/// <summary>Uma leitura por segundo durante o teste, para o gráfico e para o relatório.</summary>
public sealed record StressSample(TimeSpan At, float? CpuTemp, float? CpuClock, float? CpuPower, float? CpuLoad,
    float? GpuTemp, float? GpuLoad, float? GpuClock, double? GpuFps);

public enum StressEnd { Completed, Stopped, TooHot, Errors, GpuCrashed, Failed }

/// <summary>Uma linha do relatório: título, explicação e cor (chave do tema).</summary>
public sealed record StressFinding(string Title, string Text, string Brush);

/// <summary>Relatório final do teste de estabilidade.</summary>
public sealed class StressReport
{
    public StressEnd End { get; init; }
    public TimeSpan Duration { get; init; }
    public string Verdict { get; init; } = "";
    public string VerdictBrush { get; init; } = "Text";
    public List<StressFinding> Findings { get; } = new();
    public float? MaxCpuTemp { get; init; }
    public float? MaxGpuTemp { get; init; }
    public long CpuErrors { get; init; }
    public long RamErrors { get; init; }
}

/// <summary>
/// Comanda um teste de estabilidade: liga os testes escolhidos, lê os sensores a cada segundo,
/// para sozinho se a temperatura passar do limite ou se aparecer erro, e no fim monta um relatório explicado.
/// </summary>
public sealed class StressSession
{
    public StressOptions Options { get; }
    public CpuStress? Cpu { get; private set; }
    public RamStress? Ram { get; private set; }
    /// <summary>O stress de GPU vive numa janela própria; a tela conecta o renderizador aqui.</summary>
    public GpuRenderer? Gpu { get; set; }
    public List<StressSample> Samples { get; } = new();
    public TimeSpan Elapsed => DateTime.Now - _start;
    public bool Running { get; private set; }

    /// <summary>Nova leitura (chega na thread dos sensores).</summary>
    public event Action<StressSample>? Sampled;
    /// <summary>O teste terminou (chega em qualquer thread).</summary>
    public event Action<StressReport>? Ended;

    private DateTime _start;
    private int _hotCpu, _hotGpu;
    private readonly object _lock = new();

    public StressSession(StressOptions options) => Options = options;

    public void Start()
    {
        _start = DateTime.Now;
        Running = true;
        if (Options.Ram && Options.RamMb > 0)
        {
            Ram = new RamStress();
            Ram.Start(Options.RamMb, Math.Max(1, Environment.ProcessorCount / 2));
        }
        if (Options.Cpu)
        {
            Cpu = new CpuStress();
            Cpu.Start(Options.CpuThreads, Options.CpuHeavy);
        }
        App.Sensors.Updated += OnSensors;
    }

    /// <summary>Para tudo e gera o relatório. Pode ser chamado mais de uma vez; só a primeira conta.</summary>
    public void Stop(StressEnd reason, string? detail = null)
    {
        lock (_lock)
        {
            if (!Running) return;
            Running = false;
        }
        App.Sensors.Updated -= OnSensors;
        Cpu?.Stop();
        Ram?.Stop();
        Gpu?.Stop();
        Ended?.Invoke(BuildReport(reason, detail));
    }

    private void OnSensors(HwSnapshot s)
    {
        if (!Running) return;
        var sample = new StressSample(Elapsed, s.CpuTemp, s.CpuClockAvg, s.CpuPower, s.CpuLoad,
            s.GpuTemp, s.GpuLoad, s.GpuClock, Gpu?.Running == true ? Gpu.Fps : null);
        lock (Samples) Samples.Add(sample);
        Sampled?.Invoke(sample);

        // Duas leituras seguidas acima do limite: para antes de qualquer risco.
        _hotCpu = Options.Cpu && s.CpuTemp >= Options.CpuTempLimit ? _hotCpu + 1 : 0;
        _hotGpu = Options.Gpu && s.GpuTemp >= Options.GpuTempLimit ? _hotGpu + 1 : 0;

        if (_hotCpu >= 2)
            Stop(StressEnd.TooHot, $"A CPU chegou a {s.CpuTemp:0} °C (seu limite é {Options.CpuTempLimit:0} °C).");
        else if (_hotGpu >= 2)
            Stop(StressEnd.TooHot, $"A placa de vídeo chegou a {s.GpuTemp:0} °C (seu limite é {Options.GpuTempLimit:0} °C).");
        else if (Cpu?.Errors > 0 || Ram?.Errors > 0)
            Stop(StressEnd.Errors);
        else if (Gpu is { DeviceLost: true })
            Stop(StressEnd.GpuCrashed);
        else if (Options.Duration is { } d && Elapsed >= d)
            Stop(StressEnd.Completed);
    }

    private StressReport BuildReport(StressEnd end, string? detail)
    {
        List<StressSample> samples;
        lock (Samples) samples = Samples.ToList();

        float? maxCpu = samples.Max(x => x.CpuTemp);
        float? maxGpu = samples.Max(x => x.GpuTemp);
        long cpuErr = Cpu?.Errors ?? 0, ramErr = Ram?.Errors ?? 0;

        var (verdict, brush) = end switch
        {
            StressEnd.Completed => ("Estável: passou no teste ✔", "Good"),
            StressEnd.Stopped => ("Teste interrompido por você", "TextDim"),
            StressEnd.TooHot => ("Parou por temperatura alta 🔥", "Bad"),
            StressEnd.Errors => ("Instável: apareceram erros ✖", "Bad"),
            StressEnd.GpuCrashed => ("A placa de vídeo travou ✖", "Bad"),
            _ => ("O teste não pôde continuar", "Warn"),
        };

        var r = new StressReport
        {
            End = end,
            Duration = Elapsed,
            Verdict = verdict,
            VerdictBrush = brush,
            MaxCpuTemp = maxCpu,
            MaxGpuTemp = maxGpu,
            CpuErrors = cpuErr,
            RamErrors = ramErr,
        };

        if (detail != null) r.Findings.Add(new("O que aconteceu", detail, brush));

        if (Options.Cpu) AddCpuFindings(r, samples, cpuErr, maxCpu);
        if (Options.Ram && Ram != null)
        {
            r.Findings.Add(ramErr > 0
                ? new("Memória RAM com erros",
                    $"Foram encontrados {ramErr:N0} erros em {Ram.BytesChecked / 1e9:0.#} GB conferidos. Se você ativou XMP/EXPO, tente uma velocidade menor; " +
                    "se não, teste um pente de cada vez para achar o defeituoso e confira se estão bem encaixados.", "Bad")
                : new("Memória RAM sem erros",
                    $"{Ram.BytesChecked / 1e9:0.#} GB escritos e conferidos ({Ram.Passes} passadas por {Ram.AllocatedBytes / 1073741824.0:0.#} GB). " +
                    "Para ter certeza absoluta, deixe rodar 30 minutos ou mais.", "Good"));
        }
        if (Options.Gpu) AddGpuFindings(r, samples, maxGpu);

        if (end == StressEnd.Completed && r.Duration < TimeSpan.FromMinutes(10))
            r.Findings.Add(new("Dica", "Testes curtos pegam os problemas mais graves. Para validar um overclock, rode de 30 a 60 minutos.", "TextDim"));
        return r;
    }

    private void AddCpuFindings(StressReport r, List<StressSample> samples, long errors, float? maxTemp)
    {
        if (errors > 0)
            r.Findings.Add(new("A CPU errou contas",
                $"{errors:N0} resultados vieram errados. Isso não deveria acontecer nunca: é sinal de overclock ou undervolt instável, " +
                "temperatura alta demais ou fonte fraca. Volte as configurações da BIOS para o padrão e teste de novo.", "Bad"));
        else if (Cpu != null)
            r.Findings.Add(new("Nenhum erro de cálculo",
                $"A CPU fez {Cpu.Rounds:N0} rodadas de contas com {Cpu.Threads} threads e todas bateram com a resposta certa.", "Good"));

        if (maxTemp is { } t)
        {
            var (title, text, brush) = t switch
            {
                >= 95 => ("Temperatura no limite", $"A CPU chegou a {t:0} °C. Ela se protege reduzindo o clock (thermal throttling). Melhore a refrigeração: limpe a poeira, troque a pasta térmica ou use um cooler melhor.", "Bad"),
                >= 85 => ("Temperatura alta, mas aceitável", $"Máxima de {t:0} °C. Em teste pesado isso é comum; em jogos deve ficar mais baixo. Vale conferir a ventilação do gabinete.", "Warn"),
                _ => ("Temperatura ótima", $"Máxima de {t:0} °C: a refrigeração está dando conta do recado.", "Good"),
            };
            r.Findings.Add(new(title, text, brush));
        }
        else
        {
            r.Findings.Add(new("Sem leitura de temperatura",
                "Não conseguimos ler a temperatura da CPU, então o limite de segurança não funcionou. Instale o driver PawnIO (winget install namazso.PawnIO).", "Warn"));
        }

        // Clock no começo (após 5 s) contra o fim do teste: queda grande = throttling.
        var clocks = samples.Where(s => s.CpuClock is > 0).ToList();
        if (clocks.Count >= 40)
        {
            double early = clocks.Skip(5).Take(15).Average(s => s.CpuClock!.Value);
            double late = clocks.Skip(clocks.Count - 20).Average(s => s.CpuClock!.Value);
            double drop = (early - late) / early * 100;
            if (drop >= 8)
            {
                bool hot = maxTemp >= 90;
                r.Findings.Add(new(hot ? "O clock caiu por calor" : "O clock caiu por limite de energia",
                    $"O processador começou a {early / 1000:0.00} GHz e terminou a {late / 1000:0.00} GHz ({drop:0}% a menos). " +
                    (hot
                        ? "Como a temperatura estava alta, é thermal throttling: a refrigeração não segura o calor."
                        : "A temperatura estava ok, então é o limite de energia da placa-mãe ou do notebook (normal após 1 a 2 minutos em muitos PCs)."),
                    "Warn"));
            }
            else
            {
                r.Findings.Add(new("Clock firme", $"O processador manteve cerca de {late / 1000:0.00} GHz do começo ao fim, sem perder desempenho.", "Good"));
            }
        }
    }

    private void AddGpuFindings(StressReport r, List<StressSample> samples, float? maxTemp)
    {
        if (Gpu?.DeviceLost == true)
            r.Findings.Add(new("O driver de vídeo travou",
                "O Windows precisou reiniciar o driver da placa de vídeo. Causas comuns: overclock da GPU, fonte fraca ou velha, superaquecimento " +
                "ou driver com problema. Volte a GPU para o clock padrão e atualize o driver.", "Bad"));
        else if (Gpu?.Error is { } err)
            r.Findings.Add(new("Teste de GPU não rodou", err, "Warn"));

        if (maxTemp is { } t)
            r.Findings.Add(t >= 87
                ? new("Placa de vídeo quente", $"Chegou a {t:0} °C. Confira se as ventoinhas giram, limpe a poeira e melhore a ventilação do gabinete.", "Warn")
                : new("Temperatura da GPU boa", $"Máxima de {t:0} °C durante o teste.", "Good"));

        var fps = samples.Where(s => s.GpuFps is > 0).Select(s => s.GpuFps!.Value).ToList();
        if (fps.Count >= 40)
        {
            double early = fps.Skip(5).Take(15).Average();
            double late = fps.Skip(fps.Count - 20).Average();
            double drop = (early - late) / early * 100;
            r.Findings.Add(drop >= 8
                ? new("Desempenho da GPU caiu", $"O FPS caiu de {early:0} para {late:0} ({drop:0}%) com o tempo: a placa reduziu o clock por calor ou energia.", "Warn")
                : new("Desempenho da GPU constante", $"Média de {late:0} FPS do começo ao fim: nada de perda por calor.", "Good"));
        }
    }
}
