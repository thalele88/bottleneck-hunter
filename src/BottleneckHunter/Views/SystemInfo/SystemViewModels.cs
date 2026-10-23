using BottleneckHunter.Core.SystemInfo;

namespace BottleneckHunter.Views.SystemInfo;

// Objetos só de exibição: transformam o SystemReport em textos e níveis prontos para os modelos do XAML.

public sealed record ChipVm(string Badge, string Text);

public sealed record FindingVm(string Level, string Glyph, string Title, string Detail, string? HowToFix)
{
    public bool HasFix => HowToFix != null;

    public static FindingVm From(Finding f) => new(f.Level.ToString(), f.Level switch
    {
        Core.SystemInfo.Level.Good => "✓",
        Core.SystemInfo.Level.Warn => "!",
        Core.SystemInfo.Level.Bad => "✕",
        _ => "i",
    }, f.Title, f.Detail, f.HowToFix);
}

public sealed record RamSlotVm(int Index, bool Filled, string Name, string Size, string Detail);

public sealed class SectionVm
{
    public required string Badge { get; init; }
    public required string Title { get; init; }
    public required string Headline { get; init; }
    public required string Explain { get; init; }
    public required IReadOnlyList<InfoRow> Rows { get; init; }
    public IReadOnlyList<RamSlotVm>? Slots { get; init; }
    public bool HasSlots => Slots is { Count: > 0 };

    public static SectionVm From(ReportSection s, IReadOnlyList<RamSlotVm>? slots = null) => new()
    {
        Badge = s.Badge, Title = s.Title, Headline = s.Headline, Explain = s.Explain, Rows = s.Rows, Slots = slots,
    };
}

public sealed record VolumeVm(string Title, string Detail, double UsedPercent, string Level);

public sealed record SmartVm(string Id, string Name, int Current, int Worst, string Threshold, string Raw, string Level, string Explain);

public sealed class DiskVm
{
    public required string Badge { get; init; }
    public required string Title { get; init; }
    public required string Model { get; init; }
    public required string HealthText { get; init; }
    public required string HealthLevel { get; init; }
    public bool HoldsWindows { get; init; }
    public required IReadOnlyList<InfoRow> Rows { get; init; }
    public required IReadOnlyList<VolumeVm> Volumes { get; init; }
    public double? LifePercent { get; init; }
    public bool HasLife => LifePercent != null;
    public string LifeText => LifePercent is { } l ? $"{l:0}%" : "";
    public string LifeLevel => LifePercent switch { >= 30 => "Good", >= 10 => "Warn", _ => "Bad" };
    public double TempPercent { get; init; }
    public string TempText { get; init; } = "";
    public string TempLevel { get; init; } = "Good";
    public bool HasTemp => TempText.Length > 0;
    public required IReadOnlyList<SmartVm> Smart { get; init; }
    public bool HasSmart => Smart.Count > 0;
    public string SmartButton => $"Ver atributos S.M.A.R.T. ({Smart.Count})";
    public required string SmartNote { get; init; }

    public static DiskVm From(DiskInfo d)
    {
        var smartLevel = d.Smart.Any(a => a.Level == Level.Bad) ? Level.Bad : d.Smart.Any(a => a.Level == Level.Warn) ? Level.Warn : Level.Good;
        var (health, level) = d switch
        {
            { SmartPredictsFailure: true } or { WindowsHealth: "Com defeito" } => ("Risco de falha", "Bad"),
            _ when smartLevel == Level.Bad || d.WearPercent >= 90 => ("Crítico", "Bad"),
            { WindowsHealth: "Atenção" } => ("Atenção", "Warn"),
            _ when smartLevel == Level.Warn || d.WearPercent >= 70 => ("Atenção", "Warn"),
            { WindowsHealth: "Saudável" } => ("Saudável", "Good"),
            _ => ("Sem dados", "Info"),
        };

        int hot = d.Media == "HD" ? 50 : d.Bus == "NVMe" ? 70 : 60;
        return new DiskVm
        {
            Badge = d.Bus == "NVMe" ? "NVMe" : d.Media is "SSD" or "HD" ? d.Media : "?",
            Title = $"DISCO {d.Number}",
            Model = Fmt.Or(d.Model, $"Disco {d.Number}"),
            HealthText = health,
            HealthLevel = level,
            HoldsWindows = d.HoldsWindows,
            Rows = ReportSections.DiskRows(d).Where(r => r.Label is not ("Temperatura" or "Vida útil restante" or "Saúde (Windows)")).ToList(),
            Volumes = d.Volumes.Where(v => v.TotalGb > 0).Select(v => new VolumeVm(
                $"{v.Letter} {v.Label}".Trim() + (v.IsSystem ? "  (Windows)" : ""),
                $"{Fmt.Gb(v.FreeGb)} livres de {Fmt.Gb(v.TotalGb)}",
                v.UsedPercent,
                v.UsedPercent >= 90 ? "Bad" : v.UsedPercent >= 80 ? "Warn" : "Good")).ToList(),
            LifePercent = d.WearPercent is { } w ? 100 - w : null,
            TempText = d.TemperatureC is { } t ? $"{t} °C" : "",
            TempPercent = d.TemperatureC is { } t2 ? Math.Clamp(t2 / (hot + 20.0) * 100, 0, 100) : 0,
            TempLevel = d.TemperatureC is { } t3 ? (t3 >= hot + 10 ? "Bad" : t3 >= hot ? "Warn" : "Good") : "Good",
            Smart = d.Smart.Select(a => new SmartVm(
                a.Id.ToString(), a.Name, a.Current, a.Worst, a.Threshold?.ToString() ?? "—",
                a.Raw.ToString("N0", Fmt.Br), a.Level.ToString(), a.Explain)).ToList(),
            SmartNote = d.Smart.Count > 0 ? ""
                : d.Bus == "NVMe" ? "Discos NVMe não usam a tabela S.M.A.R.T. clássica: a saúde vem dos contadores acima (temperatura, vida útil e horas ligado)."
                : d.Bus == "USB" ? "Discos USB geralmente não repassam o S.M.A.R.T. pelo adaptador."
                : "Este disco não informou a tabela S.M.A.R.T. ao Windows.",
        };
    }
}
