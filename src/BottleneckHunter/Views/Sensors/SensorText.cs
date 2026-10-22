using System.Globalization;
using System.Text.RegularExpressions;
using BottleneckHunter.Core;

namespace BottleneckHunter.Views.Sensors;

/// <summary>Tipo de peça, deduzido pelos sensores que ela tem.</summary>
public enum HwKind { Cpu, Gpu, Ram, Board, Disk, Other }

/// <summary>Faixas de alerta de um sensor e o que dizer em cada uma.</summary>
public sealed record SensorRule(double Warn, double Bad, string OkText, string WarnText, string BadText);

/// <summary>Um item do guia "Entenda as leituras".</summary>
public sealed record GuideItem(string Title, string Text, string DotKey);

/// <summary>
/// Textos da tela de sensores: tradução dos nomes da LibreHardwareMonitor,
/// explicações didáticas, faixas de alerta e formatação dos valores.
/// </summary>
public static class SensorText
{
    private static readonly Dictionary<string, string> Exact = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CPU Total"] = "Uso total da CPU",
        ["CPU Core Max"] = "Núcleo mais ocupado",
        ["CPU Package"] = "CPU (pacote inteiro)",
        ["Package"] = "Pacote inteiro",
        ["Core Max"] = "Núcleo mais quente",
        ["Core Average"] = "Média dos núcleos",
        ["Core (Tctl/Tdie)"] = "Núcleo (Tctl/Tdie)",
        ["Core (Tctl)"] = "Núcleo (Tctl)",
        ["Core (Tdie)"] = "Núcleo (Tdie)",
        ["CPU Cores"] = "Todos os núcleos",
        ["CPU Memory"] = "Controlador de memória",
        ["CPU Graphics"] = "Gráfico integrado",
        ["CPU Platform"] = "Plataforma",
        ["Bus Speed"] = "Velocidade do barramento",
        ["Distance to TjMax"] = "Distância até o limite (TjMax)",
        ["SoC"] = "SoC (chip auxiliar)",
        ["GPU Core"] = "Núcleo da GPU",
        ["GPU Hot Spot"] = "Ponto mais quente da GPU",
        ["GPU Memory"] = "Memória da GPU",
        ["GPU Memory Junction"] = "Junção da memória da GPU",
        ["GPU Memory Used"] = "Memória da GPU em uso",
        ["GPU Memory Free"] = "Memória da GPU livre",
        ["GPU Memory Total"] = "Memória da GPU total",
        ["GPU Memory Controller"] = "Controlador de memória da GPU",
        ["GPU Video Engine"] = "Motor de vídeo da GPU",
        ["GPU Bus"] = "Barramento da GPU (PCIe)",
        ["GPU Power"] = "Consumo da GPU",
        ["GPU Package"] = "GPU (pacote inteiro)",
        ["GPU Board Power"] = "Consumo da placa de vídeo",
        ["GPU PCIe Rx"] = "PCIe recebendo",
        ["GPU PCIe Tx"] = "PCIe enviando",
        ["GPU Shader"] = "Shaders da GPU",
        ["GPU SoC"] = "SoC da GPU",
        ["D3D 3D"] = "Direct3D: 3D (jogos)",
        ["D3D Copy"] = "Direct3D: cópia",
        ["D3D Compute_0"] = "Direct3D: computação",
        ["D3D Video Decode"] = "Direct3D: decodificar vídeo",
        ["D3D Video Encode"] = "Direct3D: codificar vídeo",
        ["D3D Video Processing"] = "Direct3D: processar vídeo",
        ["D3D Dedicated Memory Used"] = "Memória dedicada em uso",
        ["D3D Dedicated Memory Free"] = "Memória dedicada livre",
        ["D3D Dedicated Memory Total"] = "Memória dedicada total",
        ["D3D Shared Memory Used"] = "Memória compartilhada em uso",
        ["D3D Shared Memory Free"] = "Memória compartilhada livre",
        ["D3D Shared Memory Total"] = "Memória compartilhada total",
        ["Memory"] = "Memória",
        ["Memory Used"] = "Memória em uso",
        ["Memory Available"] = "Memória livre",
        ["Virtual Memory"] = "Memória virtual",
        ["Virtual Memory Used"] = "Memória virtual em uso",
        ["Virtual Memory Available"] = "Memória virtual livre",
        ["Used Space"] = "Espaço usado",
        ["Read Activity"] = "Atividade de leitura",
        ["Write Activity"] = "Atividade de escrita",
        ["Total Activity"] = "Atividade total",
        ["Read Rate"] = "Velocidade de leitura",
        ["Write Rate"] = "Velocidade de escrita",
        ["Data Read"] = "Dados lidos (total)",
        ["Data Written"] = "Dados gravados (total)",
        ["Data Uploaded"] = "Dados enviados",
        ["Data Downloaded"] = "Dados recebidos",
        ["Upload Speed"] = "Velocidade de envio",
        ["Download Speed"] = "Velocidade de recebimento",
        ["Network Utilization"] = "Uso da rede",
        ["Temperature"] = "Temperatura",
        ["Available Spare"] = "Reserva disponível",
        ["Available Spare Threshold"] = "Limite da reserva",
        ["Percentage Used"] = "Desgaste do SSD",
        ["Remaining Life"] = "Vida útil restante",
        ["Power Cycle Count"] = "Vezes que foi ligado",
        ["Power On Hours"] = "Horas ligado",
        ["Vcore"] = "Tensão do núcleo (Vcore)",
        ["CPU VCore"] = "Tensão do núcleo (Vcore)",
        ["System"] = "Sistema",
        ["Motherboard"] = "Placa-mãe",
        ["Chipset"] = "Chipset",
    };

    private static readonly (Regex Pattern, string Replacement)[] Patterns =
    {
        (new Regex(@"^CPU Core #(\d+) Thread #(\d+)$"), "Núcleo #$1, thread #$2"),
        (new Regex(@"^CPU Core #(\d+)$"), "Núcleo #$1"),
        (new Regex(@"^Core #(\d+) Thread #(\d+)$"), "Núcleo #$1, thread #$2"),
        (new Regex(@"^Core #(\d+) \((.+)\)$"), "Núcleo #$1 ($2)"),
        (new Regex(@"^Core #(\d+)$"), "Núcleo #$1"),
        (new Regex(@"^P-Core #(\d+)$"), "Núcleo P #$1 (desempenho)"),
        (new Regex(@"^E-Core #(\d+)$"), "Núcleo E #$1 (eficiência)"),
        (new Regex(@"^CCD ?(\d+) \((.+)\)$"), "Chiplet CCD$1 ($2)"),
        (new Regex(@"^CCD ?(\d+)$"), "Chiplet CCD$1"),
        (new Regex(@"^Fan #(\d+)$"), "Ventoinha #$1"),
        (new Regex(@"^GPU Fan ?(\d*)$"), "Ventoinha da GPU $1"),
        (new Regex(@"^Temperature #?(\d+)$"), "Temperatura #$1"),
        (new Regex(@"^DIMM #?(\d+)$"), "Pente de memória #$1"),
    };

    /// <summary>Nome do sensor em português, quando conhecido. Senão, o original.</summary>
    public static string Translate(string raw)
    {
        if (Exact.TryGetValue(raw, out var pt)) return pt;
        foreach (var (pattern, replacement) in Patterns)
            if (pattern.IsMatch(raw))
                return pattern.Replace(raw, replacement).Trim();
        return raw;
    }

    /// <summary>Nome do tipo em português (o <see cref="SensorHub"/> já traduz os mais comuns).</summary>
    public static string TypeTitle(string type) => type switch
    {
        "Level" => "Nível",
        "Factor" => "Fator",
        "Energy" => "Energia",
        "Noise" => "Ruído",
        "Current" => "Corrente",
        "TimeSpan" => "Tempo",
        "Timing" => "Latência",
        "Humidity" => "Umidade",
        "Flow" => "Fluxo",
        "Conductivity" => "Condutividade",
        _ => type,
    };

    public static int TypeOrder(string type) => type switch
    {
        "Temperatura" => 0,
        "Uso" => 1,
        "Clock" => 2,
        "Consumo" => 3,
        "Voltagem" => 4,
        "Ventoinha" => 5,
        "Controle" => 6,
        "Dados" => 7,
        "Taxa" => 8,
        "Level" => 9,
        "Frequência" => 10,
        _ => 20,
    };

    public static string TypeDotKey(string type) => type switch
    {
        "Temperatura" => "Bad",
        "Uso" => "Cpu",
        "Clock" => "Accent",
        "Consumo" => "Warn",
        "Voltagem" => "Gpu",
        "Ventoinha" or "Controle" => "Cpu",
        _ => "TextDim",
    };

    public static string Explain(string type) => type switch
    {
        "Temperatura" => "Quão quente a peça está, em graus Celsius. Processadores aguentam até uns 95 °C, placas de vídeo uns 85–90 °C e SSDs preferem ficar abaixo de 60 °C. Passou disso, a peça reduz a velocidade sozinha para se proteger (thermal throttling) e o FPS cai.",
        "Uso" => "Porcentagem do tempo em que a peça está trabalhando. Em jogos, GPU perto de 100% é ótimo. Já um núcleo da CPU em 100% com a GPU baixa indica gargalo no processador.",
        "Clock" => "Velocidade em que a peça está rodando, em megahertz (MHz). 1000 MHz = 1 GHz. O clock sobe e desce o tempo todo para economizar energia, e isso é normal.",
        "Consumo" => "Quanta energia a peça está puxando, em watts (W). Some CPU e GPU em carga máxima e deixe uns 30% de folga para escolher a fonte.",
        "Voltagem" => "Tensão elétrica que a peça recebe, em volts (V). Interessa mais a quem faz overclock; pequenas variações são normais.",
        "Ventoinha" => "Rotação dos ventiladores, em rotações por minuto (RPM). Muitas placas de vídeo param a ventoinha (0 RPM) quando estão frias: é o modo silencioso, não defeito.",
        "Controle" => "Quanto da força máxima o ventilador está usando, em %. Mais alto significa mais resfriamento e mais barulho.",
        "Dados" => "Quantidade de dados: memória ocupada, espaço usado ou total já lido e gravado no disco.",
        "Taxa" => "Velocidade de transferência de dados neste momento, como leitura e escrita do disco.",
        "Frequência" => "Quantas vezes por segundo algo acontece, em hertz (Hz).",
        "Level" => "Nível em porcentagem. Em SSDs, mostra o desgaste: 0% é novo e 100% é o fim da vida útil prevista pelo fabricante.",
        "Factor" => "Um multiplicador ou proporção informado pela peça.",
        "Energy" => "Energia acumulada consumida pela peça.",
        "Current" => "Corrente elétrica, em ampères (A).",
        "Timing" or "TimeSpan" => "Medida de tempo informada pela peça.",
        _ => "Leitura direta do sensor da peça.",
    };

    /// <summary>Itens do guia rápido no topo da página.</summary>
    public static IReadOnlyList<GuideItem> Guide { get; } = new[]
    {
        new GuideItem("Temperatura (°C)", "Quanto a peça está quente. Calor demais derruba o desempenho.", "Bad"),
        new GuideItem("Uso (%)", "Quanto da peça está ocupada. A que fica em 100% é o gargalo.", "Cpu"),
        new GuideItem("Clock (MHz)", "A velocidade da peça agora. Sobe em carga e desce em repouso.", "Accent"),
        new GuideItem("Consumo (W)", "Energia que a peça puxa. Ajuda a escolher a fonte.", "Warn"),
        new GuideItem("Voltagem (V)", "Tensão elétrica. Útil para quem faz overclock.", "Gpu"),
        new GuideItem("Ventoinha (RPM)", "Giro dos ventiladores. 0 RPM na GPU fria é normal.", "Cpu"),
    };

    public static string KindText(HwKind k) => k switch
    {
        HwKind.Cpu => "CPU",
        HwKind.Gpu => "GPU",
        HwKind.Ram => "RAM",
        HwKind.Board => "PLACA-MÃE",
        HwKind.Disk => "DISCO",
        _ => "OUTRO",
    };

    public static string KindKey(HwKind k) => k switch
    {
        HwKind.Cpu => "Cpu",
        HwKind.Gpu => "Gpu",
        HwKind.Ram => "Accent",
        HwKind.Disk => "Warn",
        _ => "TextDim",
    };

    /// <summary>Descobre o tipo da peça pelos nomes dos sensores (a leitura só traz o nome do hardware).</summary>
    public static HwKind InferKind(string hardware, IReadOnlyCollection<SensorReading> readings)
    {
        bool Any(Func<SensorReading, bool> f) => readings.Any(f);

        if (Any(r => r.Name == "CPU Total" || r.Name.StartsWith("CPU Core #"))) return HwKind.Cpu;
        if (Any(r => r.Name.StartsWith("GPU ") || r.Name.StartsWith("D3D "))) return HwKind.Gpu;
        if (Any(r => r.Name is "Memory Used" or "Memory Available")) return HwKind.Ram;
        if (Any(r => r.Name is "Used Space" or "Data Read" or "Data Written" or "Read Rate" or "Total Activity")) return HwKind.Disk;
        if (Any(r => r.Type == "Ventoinha") || readings.Count(r => r.Type == "Voltagem") >= 3) return HwKind.Board;

        var h = hardware.ToLowerInvariant();
        if (h.Contains("ryzen") || h.Contains("core(tm)") || h.Contains("intel core") || h.Contains("xeon")) return HwKind.Cpu;
        if (h.Contains("nvidia") || h.Contains("geforce") || h.Contains("radeon") || h.Contains("arc")) return HwKind.Gpu;
        if (h.Contains("memory")) return HwKind.Ram;
        return HwKind.Other;
    }

    /// <summary>Faixas de atenção e de perigo. Nulo quando o sensor não tem um "valor ruim" claro.</summary>
    public static SensorRule? RuleFor(HwKind kind, string type, string raw)
    {
        const string HotWarn = "Esquentando. Vale conferir a poeira nos ventiladores e o fluxo de ar do gabinete.";
        const string HotBad = "Muito quente! A peça pode cortar o próprio desempenho para se proteger (thermal throttling).";
        const string Cool = "Temperatura tranquila.";

        if (type == "Temperatura")
        {
            return kind switch
            {
                HwKind.Cpu => new SensorRule(85, 95, Cool, HotWarn, HotBad),
                HwKind.Gpu when raw.Contains("Hot Spot") || raw.Contains("Junction") || raw.Contains("Memory")
                    => new SensorRule(95, 105, Cool, HotWarn, HotBad),
                HwKind.Gpu => new SensorRule(80, 88, Cool, HotWarn, HotBad),
                HwKind.Disk => new SensorRule(60, 70, Cool,
                    "O disco está quente. SSDs NVMe gostam de um dissipador ou de um fluxo de ar por cima.",
                    "Disco muito quente! Ele pode ficar mais lento para esfriar. Melhore a ventilação."),
                _ => new SensorRule(70, 85, Cool, HotWarn, HotBad),
            };
        }

        if (type == "Uso")
        {
            if (kind == HwKind.Ram && raw is "Memory" or "Virtual Memory")
                return new SensorRule(90, 95, "Memória com folga.",
                    "Memória quase cheia: feche programas em segundo plano.",
                    "Memória lotada: o Windows passa a usar o disco como memória e tudo fica lento.");
            if (kind == HwKind.Disk && raw == "Used Space")
                return new SensorRule(90, 97, "Espaço livre suficiente.",
                    "Disco quase cheio: libere espaço para o Windows funcionar bem.",
                    "Disco lotado! Atualizações e jogos podem falhar. Libere espaço já.");
            if (kind == HwKind.Gpu && raw == "GPU Memory")
                return new SensorRule(90, 97, "Memória de vídeo com folga.",
                    "Memória de vídeo quase cheia: se o jogo engasgar, baixe a qualidade das texturas.",
                    "Memória de vídeo lotada: as texturas vão para a RAM e o jogo trava. Baixe as texturas.");
        }

        if (type == "Level" && raw == "Percentage Used")
            return new SensorRule(80, 95, "SSD com bastante vida útil.",
                "O SSD já gastou boa parte da vida útil. Mantenha seus backups em dia.",
                "O SSD está perto do fim da vida útil prevista. Faça backup e planeje a troca.");

        return null;
    }

    /// <summary>Texto curto com as faixas, mostrado no destaque.</summary>
    public static string RangeHint(SensorRule? rule, string unit)
    {
        if (rule == null) return "";
        string u = unit == "°C" ? " °C" : unit == "%" ? "%" : " " + unit;
        return $"Atenção a partir de {rule.Warn:0}{u}  ·  crítico a partir de {rule.Bad:0}{u}";
    }

    public static string Format(double? value, string unit)
    {
        if (value is not { } v || !double.IsFinite(v)) return "—";
        var c = CultureInfo.CurrentCulture;
        return unit switch
        {
            "°C" => v.ToString("0", c) + " °C",
            "%" => v.ToString("0.0", c) + " %",
            "MHz" => v >= 1000 ? (v / 1000).ToString("0.00", c) + " GHz" : v.ToString("0", c) + " MHz",
            "V" => v.ToString("0.000", c) + " V",
            "W" => v.ToString(v < 10 ? "0.0" : "0", c) + " W",
            "RPM" => v.ToString("0", c) + " RPM",
            "GB" => v >= 1024 ? (v / 1024).ToString("0.00", c) + " TB" : v.ToString("0.0", c) + " GB",
            "MB" => v >= 1024 ? (v / 1024).ToString("0.0", c) + " GB" : v.ToString("0", c) + " MB",
            "B/s" => Rate(v, c),
            "Hz" => v.ToString("0.0", c) + " Hz",
            "" => v.ToString("0.##", c),
            _ => v.ToString("0.##", c) + " " + unit,
        };
    }

    private static string Rate(double bytes, CultureInfo c)
    {
        if (bytes >= 1024d * 1024 * 1024) return (bytes / (1024d * 1024 * 1024)).ToString("0.00", c) + " GB/s";
        if (bytes >= 1024d * 1024) return (bytes / (1024d * 1024)).ToString("0.0", c) + " MB/s";
        if (bytes >= 1024) return (bytes / 1024).ToString("0", c) + " KB/s";
        return bytes.ToString("0", c) + " B/s";
    }

    /// <summary>Escala do gráfico: valores muito grandes ou pequenos viram uma unidade legível nos eixos.</summary>
    public static (double Factor, string Label) ChartScale(string unit, double max) => unit switch
    {
        "°C" => (1, "°"),
        "%" => (1, "%"),
        "V" => (1000, " mV"),
        "B/s" when max >= 1024d * 1024 => (1 / (1024d * 1024), " MB/s"),
        "B/s" => (1 / 1024d, " KB/s"),
        "" => (1, ""),
        _ => (1, " " + unit),
    };

    /// <summary>Arredonda para cima até um valor "redondo" (bom para o topo do gráfico).</summary>
    public static double NiceCeiling(double v)
    {
        if (v <= 0 || !double.IsFinite(v)) return 1;
        double pow = Math.Pow(10, Math.Floor(Math.Log10(v)));
        foreach (var m in new[] { 1.0, 2, 4, 6, 8, 10 })
            if (m * pow >= v) return m * pow;
        return 10 * pow;
    }
}
