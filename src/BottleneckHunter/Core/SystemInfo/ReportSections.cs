using System.Globalization;

namespace BottleneckHunter.Core.SystemInfo;

/// <summary>Uma linha "rótulo: valor" com explicação opcional (vira dica ao passar o mouse).</summary>
public sealed record InfoRow(string Label, string Value, string? Hint = null);

/// <summary>Um cartão da tela e uma seção do relatório: mesmo texto nos dois lugares.</summary>
public sealed record ReportSection(string Key, string Badge, string Title, string Headline, string Explain, IReadOnlyList<InfoRow> Rows);

/// <summary>Formatação em pt-BR (vírgula decimal, datas dd/mm/aaaa).</summary>
public static class Fmt
{
    public static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    public static string Gb(double gb) => gb >= 1000
        ? (gb / 1000).ToString(gb >= 10000 ? "0" : "0.0", Br) + " TB"
        : gb >= 10 || Math.Abs(gb - Math.Round(gb)) < 0.05 ? gb.ToString("0", Br) + " GB" : gb.ToString("0.0", Br) + " GB";

    /// <summary>Memória usa potências de 2: 8 GB, 16 GB, 32 GB.</summary>
    public static string Ram(double gb) => gb >= 1 ? Math.Round(gb).ToString("0", Br) + " GB" : (gb * 1024).ToString("0", Br) + " MB";

    public static string Date(DateTime? d) => d?.ToString("dd/MM/yyyy", Br) ?? "—";

    public static string Hours(long h)
    {
        double days = h / 24.0;
        return days >= 365 ? $"{h.ToString("N0", Br)} h (≈ {(days / 365).ToString("0.0", Br)} anos)"
             : days >= 2 ? $"{h.ToString("N0", Br)} h (≈ {days:0} dias)"
             : $"{h} h";
    }

    public static string Uptime(DateTime boot)
    {
        var t = DateTime.Now - boot;
        return t.TotalDays >= 1 ? $"{(int)t.TotalDays} d {t.Hours} h" : $"{t.Hours} h {t.Minutes} min";
    }

    public static string YesNo(bool? b, string yes = "Ativado", string no = "Desativado") => b switch { true => yes, false => no, _ => "—" };

    public static string Or(string s, string fallback = "—") => string.IsNullOrWhiteSpace(s) ? fallback : s;
}

public static class ReportSections
{
    public const string CpuExplain = "O processador (CPU) é o \"cérebro\" do PC: roda a lógica do jogo, a física e prepara cada quadro para a placa de vídeo. Núcleos ajudam em tarefas pesadas; clock alto e cache grande ajudam em jogos.";
    public const string GpuExplain = "A placa de vídeo (GPU) desenha a imagem. A VRAM é a memória própria dela, onde ficam as texturas: se faltar, o jogo engasga mesmo com FPS alto.";
    public const string RamExplain = "A memória RAM é a \"mesa de trabalho\" do PC: guarda o que está aberto agora. Pouca RAM obriga o Windows a usar o disco, que é muito mais lento. Dois pentes iguais (dual channel) dobram a largura do caminho até o processador.";
    public const string BoardExplain = "A placa-mãe liga todas as peças. A BIOS (ou UEFI) é o programa dela que roda antes do Windows; é lá que se ativam o XMP da memória e outras opções.";
    public const string OsExplain = "O Windows é o sistema que conversa com as peças. Mantê-lo atualizado traz correções de segurança e de desempenho.";
    public const string DisplayExplain = "A taxa de atualização (Hz) é quantas imagens por segundo o monitor mostra. Se o jogo roda a 140 FPS num monitor a 60 Hz, você só vê 60.";
    public const string DiskExplain = "SSD não tem partes móveis e é muito mais rápido que HD. O S.M.A.R.T. é um \"exame de saúde\" que o próprio disco faz: conta erros, horas ligado, temperatura e desgaste.";

    public static ReportSection Cpu(SystemReport r)
    {
        var c = r.Cpu;
        var rows = new List<InfoRow>
        {
            new("Fabricante", Fmt.Or(c.Maker)),
            new("Núcleos / threads", c.Cores > 0 ? $"{c.Cores} núcleos / {c.Threads} threads" : "—",
                "Núcleos são processadores independentes dentro do chip. Threads são as tarefas que ele consegue tocar ao mesmo tempo (com Hyper-Threading/SMT, 2 por núcleo)."),
            new("Clock base", c.BaseClockMhz > 0 ? $"{(c.BaseClockMhz / 1000.0).ToString("0.00", Fmt.Br)} GHz" : "—",
                "Velocidade garantida. Com o Turbo/Boost, o processador passa disso sozinho quando há folga de temperatura e energia."),
            new("Cache L2 / L3", $"{CacheText(c.L2CacheKb)} / {CacheText(c.L3CacheKb)}",
                "Memória ultrarrápida dentro do processador. Cache L3 grande ajuda muito em jogos (é o segredo dos Ryzen X3D)."),
            new("Soquete", Fmt.Or(c.Socket), "Encaixe do processador na placa-mãe. Define quais processadores servem num upgrade."),
            new("Virtualização", Fmt.YesNo(c.VirtualizationEnabled), "Necessária para WSL, Android no Windows e máquinas virtuais. Não afeta jogos."),
        };
        if (c.Sockets > 1) rows.Insert(0, new("Processadores", c.Sockets.ToString()));
        return new("cpu", "CPU", "PROCESSADOR", Fmt.Or(c.Name, "Processador"), CpuExplain, rows);
    }

    public static IEnumerable<ReportSection> Gpus(SystemReport r)
    {
        int i = 0;
        foreach (var g in r.Gpus)
        {
            var rows = new List<InfoRow>
            {
                new("Tipo", g.IsIntegrated ? "Integrada (dentro do processador)" : "Dedicada",
                    g.IsIntegrated ? "Usa parte da memória RAM como memória de vídeo." : "Tem chip e memória próprios, bem mais forte que a integrada."),
                new("Memória de vídeo", g.VramGb is { } v ? (g.IsIntegrated ? $"{Fmt.Ram(v)} reservada + compartilhada da RAM" : Fmt.Ram(v)) : "—",
                    "Onde ficam texturas e efeitos. 8 GB é o mínimo confortável para jogos atuais em 1080p."),
                new("Versão do driver", Fmt.Or(g.DriverVersion)),
                new("Data do driver", Fmt.Date(g.DriverDate), "Drivers novos trazem otimizações para jogos recentes."),
            };
            if (g.IsBasicDriver) rows.Insert(0, new("Atenção", "Sem driver do fabricante"));
            yield return new($"gpu{i++}", "GPU", r.Gpus.Count > 1 ? $"PLACA DE VÍDEO {i}" : "PLACA DE VÍDEO", Fmt.Or(g.Name), GpuExplain, rows);
        }
    }

    public static ReportSection Ram(SystemReport r)
    {
        var m = r.Memory;
        var first = m.Modules.FirstOrDefault();
        int speed = m.Modules.Count > 0 ? m.Modules.Min(x => x.ConfiguredSpeedMts) : 0;
        string headline = m.TotalGb > 0
            ? $"{Fmt.Ram(m.TotalGb)} {first?.Type} {(speed > 0 ? $"a {speed} MT/s" : "")}".Trim()
            : "Memória";
        var rows = new List<InfoRow>
        {
            new("Pentes instalados", m.Modules.Count > 0 ? string.Join(" + ", m.Modules.Select(x => Fmt.Ram(x.SizeGb))) : "—"),
            new("Modo", m.ChannelMode, "Dual channel = dois caminhos até o processador ao mesmo tempo. Precisa de 2 (ou 4) pentes nos slots certos."),
            new("Velocidade atual", speed > 0 ? $"{speed} MT/s" : "—", "Milhões de transferências por segundo. Quanto maior, mais rápido o processador recebe os dados."),
            new("Slots usados", m.Slots > 0 ? $"{m.Modules.Count} de {m.Slots}" : "—", "Slots livres permitem colocar mais memória sem trocar os pentes atuais."),
        };
        if (m.RatedSpeedFromPartNumber is { } rated && rated != speed)
            rows.Insert(3, new("Velocidade de fábrica", $"{rated} MT/s (pelo código do pente)", "Para chegar nela, ative XMP/EXPO na BIOS."));
        if (m.MaxCapacityGb >= 4) rows.Add(new("Máximo da placa", Fmt.Ram(m.MaxCapacityGb), "Valor informado pela placa-mãe; às vezes é menor que o real."));
        return new("ram", "RAM", "MEMÓRIA RAM", headline, RamExplain, rows);
    }

    public static ReportSection Board(SystemReport r)
    {
        var b = r.Board;
        string headline = $"{b.BoardMaker} {b.BoardModel}".Trim();
        if (headline.Length == 0) headline = $"{b.SystemMaker} {b.SystemModel}".Trim();
        var rows = new List<InfoRow>();
        if (b.SystemModel.Length > 0 && !headline.Contains(b.SystemModel)) rows.Add(new("Computador", $"{b.SystemMaker} {b.SystemModel}".Trim()));
        rows.Add(new("BIOS", $"{Fmt.Or(b.BiosVersion)} ({Fmt.Date(b.BiosDate)})", "Versão e data do programa da placa-mãe."));
        rows.Add(new("Fabricante da BIOS", Fmt.Or(b.BiosMaker)));
        rows.Add(new("Modo de inicialização", Fmt.Or(b.FirmwareMode), "UEFI é o modo moderno, exigido pelo Windows 11."));
        rows.Add(new("Secure Boot", Fmt.YesNo(b.SecureBoot), "Impede que programas maliciosos rodem antes do Windows. Alguns jogos com antitrapaça exigem."));
        rows.Add(new("TPM", b.TpmVersion is { } tpm ? $"Versão {tpm}" : "Não encontrado", "Chip de segurança exigido pelo Windows 11 (TPM 2.0)."));
        return new("board", "BIOS", "PLACA-MÃE E BIOS", Fmt.Or(headline, "Placa-mãe"), BoardExplain, rows);
    }

    public static ReportSection Os(SystemReport r)
    {
        var o = r.Os;
        var rows = new List<InfoRow>
        {
            new("Versão", Fmt.Or(o.DisplayVersion), "Atualização anual do Windows (ex.: 24H2 = segundo semestre de 2024)."),
            new("Build", Fmt.Or(o.Build)),
            new("Arquitetura", Fmt.Or(o.Architecture)),
            new("Instalado em", Fmt.Date(o.InstallDate)),
            new("Ligado há", o.LastBoot is { } boot ? Fmt.Uptime(boot) : "—", "Tempo desde o último reinício. Desligar com a Inicialização Rápida não conta."),
            new("Plano de energia", Fmt.Or(o.PowerPlan ?? "")),
            new("Nome do PC", Fmt.Or(r.ComputerName)),
        };
        return new("os", "WIN", "WINDOWS", Fmt.Or(o.Name, "Windows"), OsExplain, rows);
    }

    public static ReportSection? Displays(SystemReport r)
    {
        if (r.Displays.Count == 0) return null;
        var rows = r.Displays.Select(d => new InfoRow(
            d.Name + (d.IsPrimary && r.Displays.Count > 1 ? " (principal)" : ""),
            $"{d.Width}×{d.Height} a {d.RefreshHz} Hz" + (d.MaxRefreshHz > d.RefreshHz ? $" (aceita até {d.MaxRefreshHz} Hz)" : ""),
            "Resolução e taxa de atualização atuais.")).ToList();
        var main = r.Displays[0];
        return new("displays", "Hz", r.Displays.Count > 1 ? $"MONITORES ({r.Displays.Count})" : "MONITOR",
            $"{main.Width}×{main.Height} a {main.RefreshHz} Hz", DisplayExplain, rows);
    }

    public static List<InfoRow> DiskRows(DiskInfo d)
    {
        var rows = new List<InfoRow>
        {
            new("Tipo", $"{d.Media} {d.Bus}".Trim(), d.Bus == "NVMe" ? "NVMe é o tipo mais rápido: liga direto no processador, até 5 a 10 vezes mais rápido que SATA."
                                                    : d.Media == "HD" ? "HD mecânico: tem discos girando, é barato e grande, mas lento para o Windows e jogos." : null),
            new("Capacidade", Fmt.Gb(d.SizeGb)),
            new("Saúde (Windows)", Fmt.Or(d.WindowsHealth)),
        };
        if (d.SmartPredictsFailure is { } pf) rows.Add(new("Previsão de falha (S.M.A.R.T.)", pf ? "SIM, faça backup" : "Nenhuma"));
        if (d.TemperatureC is { } t) rows.Add(new("Temperatura", $"{t} °C" + (d.TemperatureMaxC is { } tm ? $" (máx. registrada {tm} °C)" : "")));
        if (d.WearPercent is { } w) rows.Add(new("Vida útil restante", $"{100 - w}%", "Estimativa do fabricante com base em quanto já foi gravado no SSD."));
        if (d.PowerOnHours is { } h) rows.Add(new("Horas ligado", Fmt.Hours(h)));
        if (d.StartStopCycles is { } s) rows.Add(new("Vezes que ligou", s.ToString("N0", Fmt.Br)));
        if (d.SpindleRpm is { } rpm) rows.Add(new("Rotação", $"{rpm} RPM"));
        if (d.Firmware.Length > 0) rows.Add(new("Firmware", d.Firmware, "Atualizações de firmware do SSD corrigem bugs e às vezes melhoram o desempenho."));
        return rows;
    }

    /// <summary>Todas as seções na ordem da tela (sem os discos, que têm layout próprio).</summary>
    public static List<ReportSection> All(SystemReport r)
    {
        var list = new List<ReportSection> { Cpu(r) };
        list.AddRange(Gpus(r));
        list.Add(Ram(r));
        list.Add(Board(r));
        list.Add(Os(r));
        if (Displays(r) is { } d) list.Add(d);
        return list;
    }

    /// <summary>Resumo de uma linha por peça, para colar em fóruns e grupos.</summary>
    public static string Summary(SystemReport r)
    {
        var lines = new List<string>
        {
            $"Processador: {Fmt.Or(r.Cpu.Name)}" + (r.Cpu.Cores > 0 ? $" ({r.Cpu.Cores}C/{r.Cpu.Threads}T)" : ""),
        };
        foreach (var g in r.Gpus)
            lines.Add($"Placa de vídeo: {g.Name}" + (g.VramGb is { } v && !g.IsIntegrated ? $" ({Fmt.Ram(v)})" : "") + $", driver {Fmt.Or(g.DriverVersion)}");
        var ram = Ram(r);
        lines.Add($"Memória: {ram.Headline}, {r.Memory.ChannelMode}" + (r.Memory.Modules.Count > 0 ? $" ({string.Join(" + ", r.Memory.Modules.Select(x => Fmt.Ram(x.SizeGb)))})" : ""));
        lines.Add($"Placa-mãe: {Board(r).Headline}, BIOS {Fmt.Or(r.Board.BiosVersion)}");
        foreach (var d in r.Disks)
            lines.Add($"Disco: {d.Model} ({d.Media} {d.Bus}, {Fmt.Gb(d.SizeGb)})" + (d.WindowsHealth.Length > 0 ? $", saúde: {d.WindowsHealth.ToLowerInvariant()}" : ""));
        foreach (var m in r.Displays)
            lines.Add($"Monitor: {m.Name}, {m.Width}×{m.Height} a {m.RefreshHz} Hz");
        lines.Add($"Sistema: {r.Os.Name} {r.Os.DisplayVersion} (build {r.Os.Build})".Replace("  ", " "));
        lines.Add($"(gerado pelo Bottleneck Hunter {r.AppVersion} em {r.CreatedAt.ToString("dd/MM/yyyy HH:mm", Fmt.Br)})");
        return string.Join(Environment.NewLine, lines);
    }

    private static string CacheText(int kb) => kb <= 0 ? "—" : kb >= 1024 ? $"{(kb / 1024.0).ToString(kb % 1024 == 0 ? "0" : "0.0", Fmt.Br)} MB" : $"{kb} KB";
}
