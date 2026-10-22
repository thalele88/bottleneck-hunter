namespace BottleneckHunter.Core.SystemInfo;

/// <summary>
/// Olha o relatório e aponta, em linguagem simples, o que pode estar segurando o desempenho
/// ou colocando seus arquivos em risco, e como resolver.
/// </summary>
public static class HealthAdvisor
{
    public static List<Finding> Analyze(SystemReport r)
    {
        var f = new List<Finding>();
        Memory(r, f);
        Disks(r, f);
        Video(r, f);
        Displays(r, f);
        Processor(r, f);
        Windows(r, f);
        return f.OrderBy(x => x.Level switch { Level.Bad => 0, Level.Warn => 1, Level.Info => 2, _ => 3 }).ToList();
    }

    private static void Memory(SystemReport r, List<Finding> f)
    {
        var m = r.Memory;
        if (m.TotalGb <= 0) return;
        string gb = Fmt.Gb(m.TotalGb);

        if (m.TotalGb < 7.5)
            f.Add(new(Level.Bad, "Memória", "Pouca memória RAM",
                $"Com {gb}, o Windows e um jogo atual não cabem juntos na memória. O PC passa a usar o disco como memória extra, que é muito mais lento, e tudo engasga.",
                "Para jogos, 16 GB é o recomendado hoje. Veja abaixo quantos slots livres sua placa-mãe tem."));
        else if (m.TotalGb < 15)
            f.Add(new(Level.Warn, "Memória", "8 GB de RAM é o mínimo para jogos",
                $"Você tem {gb}. Dá para jogar, mas jogos recentes com o navegador ou o Discord abertos já enchem a memória e causam travadinhas.",
                "Se puder, suba para 16 GB (dois pentes de 8 GB, ou um segundo pente igual ao seu)."));
        else
            f.Add(new(Level.Good, "Memória", "Quantidade de RAM boa", $"{gb} é suficiente para jogos e uso pesado do dia a dia."));

        if (m.Modules.Count == 1)
        {
            bool igpuOnly = r.Gpus.Count > 0 && r.Gpus.All(g => g.IsIntegrated);
            f.Add(new(Level.Warn, "Memória", "Memória em single channel (só 1 pente)",
                "Com um pente só, o processador conversa com a memória por um único caminho. Com dois pentes iguais (dual channel), são dois caminhos ao mesmo tempo: "
                + (igpuOnly ? "como seu PC usa vídeo integrado, isso pode quase dobrar o FPS." : "isso costuma dar de 10% a 30% mais FPS em jogos que dependem do processador."),
                "Coloque um segundo pente igual ao atual (mesma capacidade e velocidade)."));
        }
        else if (m.ChannelMode == "Single channel" && m.Modules.Count >= 2)
        {
            f.Add(new(Level.Warn, "Memória", "Pentes no mesmo canal",
                "Você tem mais de um pente, mas eles parecem estar em slots do mesmo canal. Assim a memória não trabalha em dual channel.",
                "No manual da placa-mãe, veja os slots indicados para 2 pentes (quase sempre o 2º e o 4º, chamados A2 e B2) e mude os pentes de lugar."));
        }

        int configured = m.Modules.Count > 0 ? m.Modules.Min(x => x.ConfiguredSpeedMts) : 0;
        bool desktop = m.Modules.Any(x => x.FormFactor.StartsWith("DIMM"));
        string type = m.Modules.FirstOrDefault()?.Type ?? "";
        if (m.RatedSpeedFromPartNumber is { } rated && configured > 0 && rated > configured + 150)
        {
            f.Add(new(Level.Warn, "Memória", "Memória abaixo da velocidade de fábrica",
                $"Pelo código dos pentes, eles são de {rated} MT/s, mas estão rodando a {configured} MT/s. Isso acontece quando o perfil XMP (Intel) ou EXPO (AMD) está desligado na BIOS.",
                "Ao ligar o PC, aperte Del ou F2 para entrar na BIOS e ative XMP, EXPO ou DOCP. É seguro: é a velocidade que o fabricante do pente garante."));
        }
        else if (desktop && configured > 0 && ((type == "DDR4" && configured <= 2666) || (type == "DDR5" && configured <= 4800)))
        {
            f.Add(new(Level.Info, "Memória", "Memória na velocidade padrão",
                $"Sua memória {type} está a {configured} MT/s, a velocidade básica. Muitos pentes aguentam bem mais.",
                $"Se o pente for de {(type == "DDR4" ? "3200" : "6000")} MT/s ou mais (veja a etiqueta), ative o XMP/EXPO na BIOS."));
        }

        if (m.Modules.Count >= 2 && m.Modules.Select(x => (Math.Round(x.SizeGb), x.ConfiguredSpeedMts, x.PartNumber)).Distinct().Count() > 1)
            f.Add(new(Level.Info, "Memória", "Pentes diferentes entre si",
                "Pentes de modelos ou tamanhos diferentes funcionam, mas todos rodam na velocidade do mais lento e, com tamanhos diferentes, parte da memória fica fora do dual channel.",
                "Num upgrade, prefira um kit com pentes iguais."));
    }

    private static void Disks(SystemReport r, List<Finding> f)
    {
        bool anyProblem = false;
        foreach (var d in r.Disks)
        {
            string name = d.Model.Length > 0 ? d.Model : $"Disco {d.Number}";

            if (d.WindowsHealth == "Com defeito" || d.SmartPredictsFailure == true)
            {
                anyProblem = true;
                f.Add(new(Level.Bad, "Discos", $"{name}: risco de falha",
                    "O próprio disco está avisando que pode parar de funcionar. Isso pode significar perda de arquivos.",
                    "Faça backup dos seus arquivos importantes agora (nuvem ou HD externo) e planeje a troca do disco."));
            }
            else if (d.WindowsHealth == "Atenção")
            {
                anyProblem = true;
                f.Add(new(Level.Warn, "Discos", $"{name}: o Windows marcou atenção",
                    "O Windows detectou sinais de desgaste ou erros neste disco.",
                    "Mantenha backup em dia e acompanhe os dados do S.M.A.R.T. abaixo."));
            }

            var bad = d.Smart.Where(a => a.Level == Level.Bad).ToList();
            var warn = d.Smart.Where(a => a.Level == Level.Warn).ToList();
            if (bad.Count > 0)
            {
                anyProblem = true;
                f.Add(new(Level.Bad, "Discos", $"{name}: S.M.A.R.T. em alerta",
                    "Atributos em estado crítico: " + string.Join(", ", bad.Select(a => a.Name)) + ".",
                    "Faça backup já. Esses números indicam que a superfície ou a memória do disco está se deteriorando."));
            }
            else if (warn.Count > 0)
            {
                anyProblem = true;
                bool cable = warn.All(a => a.Id is 199 or 183 or 188);
                f.Add(new(Level.Warn, "Discos", $"{name}: S.M.A.R.T. pede atenção",
                    "Atributos fora do ideal: " + string.Join(", ", warn.Select(a => a.Name)) + ".",
                    cable ? "Esses erros costumam vir do cabo SATA: troque o cabo ou mude de porta." : "Mantenha backup em dia. Se esses números crescerem nas próximas semanas, troque o disco."));
            }

            if ((d.ReadErrorsUncorrected ?? 0) + (d.WriteErrorsUncorrected ?? 0) > 0)
            {
                anyProblem = true;
                f.Add(new(Level.Warn, "Discos", $"{name}: erros de leitura/gravação",
                    $"O disco registrou {d.ReadErrorsUncorrected ?? 0} erros de leitura e {d.WriteErrorsUncorrected ?? 0} de gravação que não conseguiu corrigir.",
                    "Mantenha backup em dia e acompanhe se o número cresce."));
            }

            if (d.WearPercent is >= 70 and var wear)
            {
                anyProblem = true;
                f.Add(new(wear >= 90 ? Level.Bad : Level.Warn, "Discos", $"{name}: SSD desgastado",
                    $"O SSD já usou {wear}% da vida útil estimada pelo fabricante. SSDs costumam funcionar além disso, mas a chance de falha aumenta.",
                    "Mantenha backup em dia e planeje a troca."));
            }

            int hot = d.Media == "HD" ? 50 : d.Bus == "NVMe" ? 70 : 60;
            if (d.TemperatureC is { } t && t >= hot)
            {
                anyProblem = true;
                f.Add(new(Level.Warn, "Discos", $"{name}: esquentando ({t} °C)",
                    "Disco quente demais perde velocidade (o SSD se protege reduzindo o desempenho) e dura menos.",
                    d.Bus == "NVMe" ? "Use um dissipador no SSD NVMe (muitas placas-mãe já vêm com um) e melhore a ventilação do gabinete."
                                    : "Melhore o fluxo de ar do gabinete na região dos discos."));
            }

            foreach (var v in d.Volumes.Where(v => v.TotalGb > 1))
            {
                double freePct = v.FreeGb / v.TotalGb * 100;
                bool system = v.IsSystem;
                if ((freePct < 10 && (system || v.FreeGb < 50)) || (system && v.FreeGb < 15))
                {
                    anyProblem = true;
                    f.Add(new(system ? Level.Bad : Level.Warn, "Discos", $"Pouco espaço livre em {v.Letter}",
                        $"Restam só {Fmt.Gb(v.FreeGb)} ({freePct:0}%). " + (system
                            ? "O Windows precisa de espaço para a memória virtual e as atualizações; sem ele, o PC fica lento e jogos podem fechar."
                            : d.Media == "SSD" ? "SSD muito cheio grava mais devagar." : "Disco cheio não tem espaço para instalar e atualizar jogos."),
                        "Use a página Otimizar o PC para limpar arquivos temporários, ou desinstale jogos e programas que não usa."));
                }
                else if (system && freePct < 15)
                {
                    f.Add(new(Level.Info, "Discos", $"Espaço ficando curto em {v.Letter}",
                        $"Restam {Fmt.Gb(v.FreeGb)} ({freePct:0}%). Mantenha pelo menos 15% livres no disco do Windows."));
                }
            }

            if (d.HoldsWindows && d.Media == "HD")
            {
                anyProblem = true;
                f.Add(new(Level.Warn, "Discos", "Windows instalado num HD",
                    "O HD é mecânico (tem um disco girando) e é de 50 a 100 vezes mais lento que um SSD para abrir arquivos pequenos. Por isso o PC demora para ligar e os jogos travam ao carregar.",
                    "Um SSD, mesmo barato, é o upgrade que mais muda a sensação de velocidade do PC."));
            }
        }

        if (r.Disks.Count > 0 && !anyProblem)
            f.Add(new(Level.Good, "Discos", "Discos saudáveis", "Nenhum sinal de desgaste, erro ou falta de espaço nos discos."));
    }

    private static void Video(SystemReport r, List<Finding> f)
    {
        foreach (var g in r.Gpus)
        {
            if (g.IsBasicDriver)
            {
                f.Add(new(Level.Bad, "Vídeo", "Placa de vídeo sem driver",
                    "O Windows está usando um driver genérico. Sem o driver certo, os jogos rodam muito mal ou nem abrem.",
                    "Instale o driver do fabricante: NVIDIA (nvidia.com/drivers), AMD (amd.com/support) ou Intel (intel.com.br, suporte)."));
                continue;
            }
            if (g.DriverDate is { } date && (DateTime.Now - date).TotalDays > 365 && !g.IsIntegrated)
            {
                f.Add(new(Level.Warn, "Vídeo", "Driver de vídeo com mais de 1 ano",
                    $"O driver da {g.Name} é de {date:MM/yyyy}. Drivers novos trazem otimizações para jogos recentes e correções de travamentos.",
                    g.Maker switch
                    {
                        "NVIDIA" => "Atualize pelo aplicativo NVIDIA App ou em nvidia.com/drivers.",
                        "AMD" => "Atualize pelo AMD Software: Adrenalin Edition ou em amd.com/support.",
                        _ => "Atualize pelo site do fabricante da placa.",
                    }));
            }
        }

        if (r.Gpus.Count > 0 && r.Gpus.All(g => g.IsIntegrated))
            f.Add(new(Level.Info, "Vídeo", "Só vídeo integrado",
                "Seu PC usa o vídeo que vem dentro do processador. Ele não tem memória própria e usa a RAM, por isso dual channel e memória rápida fazem muita diferença aqui.",
                "Para jogos pesados, uma placa de vídeo dedicada é o upgrade principal."));
    }

    private static void Displays(SystemReport r, List<Finding> f)
    {
        foreach (var d in r.Displays)
        {
            if (d.MaxRefreshHz - d.RefreshHz >= 15)
                f.Add(new(Level.Warn, "Monitor", $"{d.Name} abaixo da taxa máxima",
                    $"O monitor está em {d.RefreshHz} Hz, mas aceita {d.MaxRefreshHz} Hz. Mesmo com o jogo a 200 FPS, você só enxerga {d.RefreshHz} imagens por segundo.",
                    $"Configurações > Sistema > Vídeo > Vídeo avançado > Escolher uma taxa de atualização: {d.MaxRefreshHz} Hz. Se a opção não aparecer, use cabo DisplayPort ou HDMI 2.0 ou superior."));
            else if (d.RefreshHz >= 100)
                f.Add(new(Level.Good, "Monitor", $"{d.Name} na taxa máxima", $"Rodando a {d.RefreshHz} Hz, o máximo que ele aceita nesta resolução."));
        }
    }

    private static void Processor(SystemReport r, List<Finding> f)
    {
        var c = r.Cpu;
        if (c.Threads is > 0 and <= 4)
            f.Add(new(Level.Warn, "Processador", "Processador com poucos núcleos",
                $"Com {c.Cores} núcleos e {c.Threads} threads, o processador tende a ser o gargalo: jogos atuais aproveitam 6 núcleos ou mais.",
                "Feche programas em segundo plano enquanto joga. Num upgrade, priorize o processador."));

        if (r.Os.PowerPlan is { } plan && (plan.Contains("Econom", StringComparison.OrdinalIgnoreCase) || plan.Contains("saver", StringComparison.OrdinalIgnoreCase)))
            f.Add(new(Level.Warn, "Processador", "Plano de energia em Economia",
                $"O plano \"{plan}\" limita a velocidade do processador para gastar menos energia.",
                "Painel de Controle > Opções de Energia: escolha Equilibrado ou Alto desempenho."));
    }

    private static void Windows(SystemReport r, List<Finding> f)
    {
        var os = r.Os;
        if (!os.IsWindows11 && os.Name.Contains("Windows 10"))
            f.Add(new(Level.Warn, "Windows", "Windows 10 sem suporte",
                "O suporte gratuito do Windows 10 terminou em outubro de 2025: ele não recebe mais correções de segurança, e jogos novos são testados primeiro no Windows 11.",
                "Se o PC for compatível, atualize para o Windows 11 de graça pelo Windows Update."));

        if (os.LastBoot is { } boot && (DateTime.Now - boot).TotalDays >= 7)
            f.Add(new(Level.Info, "Windows", $"PC sem reiniciar há {(int)(DateTime.Now - boot).TotalDays} dias",
                "Com a Inicialização Rápida, desligar não zera o Windows de verdade. Reiniciar de vez em quando libera memória e aplica atualizações.",
                "Use Iniciar > Energia > Reiniciar pelo menos uma vez por semana."));

        if (r.Board.BiosDate is { } bios && (DateTime.Now - bios).TotalDays > 3 * 365)
            f.Add(new(Level.Info, "Placa-mãe", "BIOS antiga",
                $"Sua BIOS é de {bios:yyyy}. Atualizações de BIOS melhoram a estabilidade da memória e corrigem falhas de segurança e de desempenho.",
                "Procure o modelo da sua placa-mãe no site do fabricante. Siga o passo a passo dele com calma e não desligue o PC durante a atualização."));
    }
}
