namespace BottleneckHunter.Core.SystemInfo;

/// <summary>
/// Decodifica a tabela S.M.A.R.T. que o driver do Windows entrega para discos SATA
/// (MSStorageDriver_FailurePredictData) e explica cada atributo em português.
/// </summary>
internal static class Smart
{
    private sealed record Meta(string Name, string Explain);

    private static readonly Dictionary<int, Meta> Known = new()
    {
        [1] = new("Taxa de erros de leitura", "Erros ao ler a superfície. Em discos Seagate o valor bruto é enorme mesmo em discos bons; olhe o valor normalizado."),
        [3] = new("Tempo para girar", "Quanto o disco demora para atingir a velocidade. Só existe em HDs."),
        [4] = new("Vezes que o motor ligou", "Quantas vezes o disco girou do zero. Só um contador."),
        [5] = new("Setores realocados", "Pedaços do disco que estragaram e foram trocados por reservas. O ideal é zero; se o número cresce, faça backup e planeje a troca."),
        [7] = new("Erros de posicionamento", "Erros da cabeça de leitura ao achar a trilha (HD). O valor bruto varia por fabricante."),
        [9] = new("Horas ligado", "Total de horas em que o disco ficou ligado na vida toda."),
        [10] = new("Novas tentativas de girar", "O motor precisou tentar de novo para girar. Acima de zero indica problema mecânico ou de energia."),
        [12] = new("Vezes que o PC ligou", "Ciclos de liga/desliga. Só um contador."),
        [169] = new("Vida restante", "Estimativa do fabricante de quanto da vida útil ainda resta."),
        [170] = new("Blocos reservas", "Blocos de reserva do SSD que ainda estão disponíveis."),
        [171] = new("Falhas de gravação", "Falhas ao gravar na memória flash do SSD. O ideal é zero."),
        [172] = new("Falhas ao apagar", "Falhas ao apagar blocos da memória flash do SSD. O ideal é zero."),
        [173] = new("Contagem de desgaste", "Média de vezes que cada bloco do SSD já foi apagado."),
        [174] = new("Desligamentos inesperados", "Vezes que o PC foi desligado sem avisar o SSD (queda de energia, botão)."),
        [177] = new("Nivelamento de desgaste", "Em SSDs Samsung, o valor normalizado mostra quanto da vida ainda resta (100 = novo)."),
        [179] = new("Blocos reservas usados", "Blocos de reserva já gastos para substituir blocos ruins."),
        [181] = new("Falhas de programação", "Falhas ao gravar dados. O ideal é zero."),
        [182] = new("Falhas ao apagar", "Falhas ao apagar dados. O ideal é zero."),
        [183] = new("Redução de velocidade SATA", "Vezes que a conexão caiu de velocidade. Pode ser cabo ou porta ruim."),
        [184] = new("Erros de ponta a ponta", "Dados corrompidos no caminho interno do disco. O ideal é zero."),
        [187] = new("Erros não corrigidos", "Erros de leitura que nem a correção interna conseguiu resolver. Acima de zero é sinal de alerta."),
        [188] = new("Comandos que expiraram", "Comandos que demoraram demais. Pode indicar cabo ou fonte com problema."),
        [190] = new("Temperatura (fluxo de ar)", "Temperatura medida no fluxo de ar do disco."),
        [191] = new("Impactos detectados", "Batidas ou vibrações fortes sentidas pelo HD."),
        [192] = new("Desligamentos de emergência", "Vezes que a energia foi cortada de repente."),
        [193] = new("Ciclos de estacionar a cabeça", "Vezes que a cabeça de leitura foi estacionada (HD)."),
        [194] = new("Temperatura", "Temperatura atual do disco, em °C."),
        [195] = new("Erros corrigidos por ECC", "Erros que a correção interna resolveu sozinha. Normal ter alguns."),
        [196] = new("Eventos de realocação", "Quantas vezes o disco precisou mover dados de setores ruins. O ideal é zero."),
        [197] = new("Setores pendentes", "Setores instáveis esperando para serem realocados. Acima de zero é sinal de alerta: faça backup."),
        [198] = new("Setores incorrigíveis", "Setores que não puderam ser lidos nem corrigidos. Acima de zero, faça backup já."),
        [199] = new("Erros de cabo (CRC)", "Erros na transmissão pelo cabo SATA. Costuma ser cabo ruim ou mal encaixado: troque o cabo."),
        [200] = new("Erros de gravação", "Erros ao gravar na superfície do HD."),
        [202] = new("Vida restante", "Em SSDs Crucial/Micron, quanto da vida útil ainda resta (normalizado)."),
        [230] = new("Amplitude da cabeça", "Dado interno do fabricante."),
        [231] = new("Vida útil restante", "Quanto da vida útil do SSD ainda resta (normalizado, 100 = novo)."),
        [232] = new("Reserva disponível", "Espaço de reserva ainda disponível no SSD."),
        [233] = new("Indicador de desgaste", "Desgaste da memória flash (normalizado, 100 = novo)."),
        [235] = new("Desligamentos seguros", "Contador interno de desligamentos."),
        [241] = new("Total gravado", "Quanto já foi gravado no disco ao longo da vida (unidade varia por fabricante)."),
        [242] = new("Total lido", "Quanto já foi lido do disco ao longo da vida (unidade varia por fabricante)."),
    };

    /// <summary>Atributos cujo valor normalizado indica vida restante do SSD, em ordem de preferência.</summary>
    public static readonly int[] LifeAttributes = { 231, 233, 202, 169, 177 };

    public static List<SmartAttribute> Parse(byte[] data, byte[]? thresholds)
    {
        var limits = new Dictionary<int, int>();
        if (thresholds is { Length: >= 362 })
        {
            for (int i = 0; i < 30; i++)
            {
                int o = 2 + i * 12;
                if (thresholds[o] != 0) limits[thresholds[o]] = thresholds[o + 1];
            }
        }

        var list = new List<SmartAttribute>();
        if (data.Length < 362) return list;

        for (int i = 0; i < 30; i++)
        {
            int o = 2 + i * 12;
            int id = data[o];
            if (id == 0) continue;

            long raw = 0;
            for (int b = 5; b >= 0; b--) raw = (raw << 8) | data[o + 5 + b];
            // Alguns atributos guardam mais de um número no valor bruto; o principal fica nos bytes baixos.
            if (id is 194 or 190) raw &= 0xFF;
            else if (id is 9) raw &= 0xFFFFFFFF;

            var meta = Known.GetValueOrDefault(id) ?? new Meta($"Atributo {id}", "Atributo específico do fabricante.");
            var attr = new SmartAttribute
            {
                Id = id,
                Name = meta.Name,
                Explain = meta.Explain,
                Current = data[o + 3],
                Worst = data[o + 4],
                Threshold = limits.TryGetValue(id, out var t) ? t : null,
                Raw = raw,
            };
            attr.Level = Judge(attr);
            list.Add(attr);
        }
        return list;
    }

    private static Level Judge(SmartAttribute a)
    {
        // Abaixo do limite do fabricante = o próprio disco considera a peça em falha.
        if (a.Threshold is int t && t > 0 && a.Current <= t) return Level.Bad;

        return a.Id switch
        {
            5 or 196 => a.Raw switch { 0 => Level.Good, < 50 => Level.Warn, _ => Level.Bad },
            197 => a.Raw switch { 0 => Level.Good, < 10 => Level.Warn, _ => Level.Bad },
            198 => a.Raw == 0 ? Level.Good : Level.Bad,
            10 or 171 or 172 or 181 or 182 or 184 or 187 => a.Raw == 0 ? Level.Good : Level.Warn,
            199 => a.Raw == 0 ? Level.Good : Level.Warn,
            194 or 190 => a.Raw switch { 0 => Level.Info, < 55 => Level.Good, < 65 => Level.Warn, _ => Level.Bad },
            231 or 233 or 202 or 169 or 177 => a.Current switch { >= 30 => Level.Good, >= 10 => Level.Warn, _ => Level.Bad },
            _ => Level.Info,
        };
    }
}
