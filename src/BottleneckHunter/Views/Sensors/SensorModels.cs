using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using BottleneckHunter.Core;

namespace BottleneckHunter.Views.Sensors;

public abstract class Bindable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public enum Severity { Normal, Warn, Bad }

/// <summary>
/// Uma linha da lista: guarda o histórico (15 min), o mínimo e o máximo do sensor,
/// e expõe os textos e cores já prontos para a tela.
/// </summary>
public sealed class SensorRow : Bindable
{
    public const int HistoryCapacity = 900;

    private readonly float[] _history = new float[HistoryCapacity];
    private int _start, _count;
    private float? _value, _min, _max;

    public SensorRow(string key, SensorReading r, HwKind kind)
    {
        Key = key;
        Hardware = r.Hardware;
        RawName = r.Name;
        Type = r.Type;
        Unit = r.Unit;
        Kind = kind;
        Name = SensorText.Translate(r.Name);
        TypeTitle = SensorText.TypeTitle(r.Type);
        Explanation = SensorText.Explain(r.Type);
        Rule = SensorText.RuleFor(kind, r.Type, r.Name);
        RangeHint = SensorText.RangeHint(Rule, Unit);
        Tip = Name != RawName ? $"Nome original: {RawName}\nClique para ver o gráfico." : "Clique para ver o gráfico.";
        SearchText = string.Join(" ", Name, RawName, Hardware, TypeTitle, SensorText.KindText(kind));
    }

    public string Key { get; }
    public string Hardware { get; }
    public string RawName { get; }
    public string Name { get; }
    public string Type { get; }
    public string TypeTitle { get; }
    public string Unit { get; }
    public HwKind Kind { get; }
    public string KindText => SensorText.KindText(Kind);
    public string KindKey => SensorText.KindKey(Kind);
    public string Explanation { get; }
    public string RangeHint { get; }
    public string Tip { get; }
    public string SearchText { get; }
    public SensorRule? Rule { get; }

    public float? Value => _value;
    public float? Min => _min;
    public float? Max => _max;

    private string _valueText = "—", _minText = "—", _maxText = "—";
    public string ValueText { get => _valueText; private set => Set(ref _valueText, value); }
    public string MinText { get => _minText; private set => Set(ref _minText, value); }
    public string MaxText { get => _maxText; private set => Set(ref _maxText, value); }

    private double _fraction;
    public double Fraction { get => _fraction; private set => Set(ref _fraction, value); }

    private string _barKey = "Accent", _valueKey = "Text", _trendKey = "TextDim", _trendGlyph = "";
    public string BarKey { get => _barKey; private set => Set(ref _barKey, value); }
    public string ValueKey { get => _valueKey; private set => Set(ref _valueKey, value); }
    public string TrendKey { get => _trendKey; private set => Set(ref _trendKey, value); }
    public string TrendGlyph { get => _trendGlyph; private set => Set(ref _trendGlyph, value); }

    private Severity _severity;
    public Severity Severity { get => _severity; private set => Set(ref _severity, value); }

    private string _statusText = "";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    private bool _isVisible = true, _isSelected, _isPinned;
    public bool IsVisible { get => _isVisible; set => Set(ref _isVisible, value); }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public bool IsPinned { get => _isPinned; set => Set(ref _isPinned, value); }

    private double[] _spark = Array.Empty<double>();
    /// <summary>Último minuto, para o mini gráfico dos cartões fixados.</summary>
    public double[] Spark { get => _spark; private set => Set(ref _spark, value); }

    /// <summary>Registra uma leitura. Não mexe na tela: chame <see cref="Refresh"/> depois.</summary>
    public void Apply(SensorReading r, bool mergeHardwareMinMax)
    {
        float? v = r.Value is { } x && float.IsFinite(x) ? x : null;
        _value = v;
        Push(v ?? float.NaN);
        if (v is { } val)
        {
            _min = _min is { } m ? Math.Min(m, val) : val;
            _max = _max is { } M ? Math.Max(M, val) : val;
        }
        // Antes de o usuário zerar, aproveitamos o mín./máx. que a biblioteca guarda desde que o app abriu.
        if (mergeHardwareMinMax)
        {
            if (r.Min is { } hm && float.IsFinite(hm)) _min = _min is { } m ? Math.Min(m, hm) : hm;
            if (r.Max is { } hM && float.IsFinite(hM)) _max = _max is { } M ? Math.Max(M, hM) : hM;
        }
    }

    public void ResetMinMax()
    {
        _min = _value;
        _max = _value;
    }

    /// <summary>Atualiza textos, cores e barra a partir da última leitura.</summary>
    public void Refresh()
    {
        ValueText = SensorText.Format(_value, Unit);
        MinText = SensorText.Format(_min, Unit);
        MaxText = SensorText.Format(_max, Unit);

        double v = _value ?? 0;
        double scale = Unit switch
        {
            "%" => 100,
            "°C" => Math.Max(100, (Rule?.Bad ?? 0) + 5),
            _ => _max is > 0 ? _max.Value : 1,
        };
        Fraction = Math.Clamp(v / scale, 0, 1);

        var sev = Severity.Normal;
        if (Rule != null && _value is { } cur)
            sev = cur >= Rule.Bad ? Severity.Bad : cur >= Rule.Warn ? Severity.Warn : Severity.Normal;
        Severity = sev;

        if (Rule != null)
        {
            BarKey = sev switch { Severity.Bad => "Bad", Severity.Warn => "Warn", _ => "Good" };
            ValueKey = sev switch { Severity.Bad => "Bad", Severity.Warn => "Warn", _ => "Text" };
            StatusText = _value == null ? "" : sev switch { Severity.Bad => Rule.BadText, Severity.Warn => Rule.WarnText, _ => Rule.OkText };
        }
        else
        {
            BarKey = Unit == "%" && Kind is HwKind.Cpu or HwKind.Gpu ? KindKey : "Accent";
            ValueKey = "Text";
            StatusText = "";
        }

        // Tendência: compara com 3 segundos atrás.
        float old = Back(3);
        if (_value is { } now && float.IsFinite(old))
        {
            double delta = now - old, threshold = scale * 0.02;
            bool up = delta > threshold, down = delta < -threshold;
            TrendGlyph = up ? "▲" : down ? "▼" : "";
            TrendKey = Type == "Temperatura" ? (up ? "Warn" : "Good") : "TextDim";
        }
        else TrendGlyph = "";

        if (IsPinned) Spark = Last(60).Select(f => (double)f).ToArray();
    }

    /// <summary>Os últimos <paramref name="n"/> valores, do mais antigo ao mais novo (NaN = sem leitura).</summary>
    public float[] Last(int n)
    {
        n = Math.Min(n, _count);
        var result = new float[n];
        for (int i = 0; i < n; i++)
            result[i] = _history[(_start + _count - n + i) % HistoryCapacity];
        return result;
    }

    private float Back(int k) => k < _count ? _history[(_start + _count - 1 - k) % HistoryCapacity] : float.NaN;

    private void Push(float v)
    {
        if (_count < HistoryCapacity)
        {
            _history[(_start + _count) % HistoryCapacity] = v;
            _count++;
        }
        else
        {
            _history[_start] = v;
            _start = (_start + 1) % HistoryCapacity;
        }
    }
}

/// <summary>Sensores de um mesmo tipo dentro de uma peça (ex.: todas as temperaturas da CPU).</summary>
public sealed class SensorSection : Bindable
{
    public SensorSection(string type, IEnumerable<SensorRow> rows)
    {
        Type = type;
        Title = SensorText.TypeTitle(type).ToUpper(CultureInfo.CurrentCulture);
        Explanation = SensorText.Explain(type);
        DotKey = SensorText.TypeDotKey(type);
        Rows = new ObservableCollection<SensorRow>(rows);
    }

    public string Type { get; }
    public string Title { get; }
    public string Explanation { get; }
    public string DotKey { get; }
    public ObservableCollection<SensorRow> Rows { get; }

    private bool _hasVisible = true;
    public bool HasVisible { get => _hasVisible; set => Set(ref _hasVisible, value); }
}

/// <summary>Um cartão da lista: uma peça do PC com seus sensores agrupados por tipo.</summary>
public sealed class HardwareGroup : Bindable
{
    public HardwareGroup(string name, HwKind kind, IEnumerable<SensorSection> sections, bool expanded)
    {
        Name = name;
        Kind = kind;
        Sections = new ObservableCollection<SensorSection>(sections);
        Rows = Sections.SelectMany(s => s.Rows).ToList();
        _isExpanded = expanded;
        CountText = Rows.Count == 1 ? "1 sensor" : $"{Rows.Count} sensores";
    }

    public string Name { get; }
    public HwKind Kind { get; }
    public string KindText => SensorText.KindText(Kind);
    public string KindKey => SensorText.KindKey(Kind);
    public ObservableCollection<SensorSection> Sections { get; }
    public IReadOnlyList<SensorRow> Rows { get; }

    public string CountText { get; }

    private bool _isExpanded, _forceOpen, _hasVisible = true;

    /// <summary>Escolha do usuário (clicar no cabeçalho).</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (Set(ref _isExpanded, value)) Raise(nameof(IsOpen)); }
    }

    /// <summary>Durante uma busca, todos os cartões com resultado ficam abertos.</summary>
    public bool ForceOpen
    {
        get => _forceOpen;
        set { if (Set(ref _forceOpen, value)) Raise(nameof(IsOpen)); }
    }

    public bool IsOpen => _isExpanded || _forceOpen;
    public bool HasVisible { get => _hasVisible; set => Set(ref _hasVisible, value); }

    private string _summary = "";
    /// <summary>Resumo no cabeçalho: temperatura mais alta e uso principal.</summary>
    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public void RefreshSummary()
    {
        var parts = new List<string>();
        var hottest = Rows.Where(r => r.Type == "Temperatura" && r.Value != null).MaxBy(r => r.Value);
        if (hottest != null) parts.Add(SensorText.Format(hottest.Value, "°C"));

        string? loadName = Kind switch
        {
            HwKind.Cpu => "CPU Total",
            HwKind.Gpu => "GPU Core",
            HwKind.Ram => "Memory",
            HwKind.Disk => "Used Space",
            _ => null,
        };
        var load = Rows.FirstOrDefault(r => r.Type == "Uso" && r.RawName == loadName && r.Value != null)
                   ?? (Kind == HwKind.Gpu ? Rows.FirstOrDefault(r => r.Type == "Uso" && r.RawName == "D3D 3D" && r.Value != null) : null);
        if (load != null) parts.Add((Kind == HwKind.Disk ? "ocupado " : "uso ") + SensorText.Format(load.Value, "%"));

        Summary = string.Join("   ·   ", parts);
    }
}

/// <summary>Preferências da página, salvas em %AppData%\BottleneckHunter\sensors.json.</summary>
public sealed class SensorsSettings
{
    public const int MaxPinned = 12;

    public List<string>? Pinned { get; set; }
    public List<string>? Collapsed { get; set; }
    public string? Selected { get; set; }
    public bool Initialized { get; set; }

    public static SensorsSettings Load()
    {
        // O arquivo vem do disco: limpamos tudo antes de usar.
        var s = JsonStore.Load<SensorsSettings>("sensors");
        s.Pinned = Clean(s.Pinned, MaxPinned);
        s.Collapsed = Clean(s.Collapsed, 64);
        if (s.Selected is { Length: > 300 }) s.Selected = null;
        return s;
    }

    private static List<string> Clean(List<string>? list, int max) =>
        (list ?? new()).Where(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 300).Distinct().Take(max).ToList();

    public void Save() => JsonStore.Save("sensors", this);
}
