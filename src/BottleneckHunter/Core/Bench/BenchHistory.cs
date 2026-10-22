namespace BottleneckHunter.Core.Bench;

/// <summary>Um resultado salvo: tipo do teste, quando foi feito e os números principais.</summary>
public sealed class BenchRecord
{
    /// <summary>"cpu", "ram", "disk", "gpu" ou "stress".</summary>
    public string Kind { get; set; } = "";
    public DateTime When { get; set; }
    /// <summary>Frase curta para a lista (ex.: "1 núcleo 1.120 · todos 4.380").</summary>
    public string Summary { get; set; } = "";
    /// <summary>Número principal para comparar com os anteriores (nota, GB/s, MB/s, FPS).</summary>
    public double Main { get; set; }
    public string MainUnit { get; set; } = "";
}

/// <summary>
/// Histórico dos testes em %AppData%\BottleneckHunter\bench.json, para comparar antes e depois de um ajuste.
/// O arquivo é tratado como não confiável: tipos desconhecidos, números absurdos e textos longos são descartados.
/// </summary>
public static class BenchHistory
{
    private const string File = "bench";
    private const int MaxRecords = 200;
    private static readonly HashSet<string> Kinds = new() { "cpu", "ram", "disk", "gpu", "stress" };

    public sealed class Data
    {
        public List<BenchRecord> Records { get; set; } = new();
    }

    public static List<BenchRecord> Load()
    {
        var data = JsonStore.Load<Data>(File);
        return (data.Records ?? new())
            .Where(Valid)
            .OrderByDescending(r => r.When)
            .Take(MaxRecords)
            .ToList();
    }

    public static void Add(BenchRecord record)
    {
        var list = Load();
        list.Insert(0, record);
        JsonStore.Save(File, new Data { Records = list.Take(MaxRecords).ToList() });
    }

    public static void Clear() => JsonStore.Save(File, new Data());

    /// <summary>Último resultado salvo do mesmo tipo, para mostrar "x% melhor que da última vez".</summary>
    public static BenchRecord? Last(string kind) => Load().FirstOrDefault(r => r.Kind == kind);

    private static bool Valid(BenchRecord? r) =>
        r != null
        && r.Kind != null && Kinds.Contains(r.Kind)
        && r.Summary != null && r.Summary.Length <= 300
        && r.MainUnit != null && r.MainUnit.Length <= 20
        && double.IsFinite(r.Main) && r.Main >= 0 && r.Main < 1e9
        && r.When > new DateTime(2020, 1, 1) && r.When < DateTime.Now.AddDays(2);
}
