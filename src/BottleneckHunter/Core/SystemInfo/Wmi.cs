using System.Management;

namespace BottleneckHunter.Core.SystemInfo;

/// <summary>Uma linha de resultado do WMI, com leitura tolerante: valor ausente ou de tipo estranho vira vazio/nulo.</summary>
internal sealed class WmiRow
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

    public WmiRow(ManagementBaseObject mo)
    {
        foreach (var p in mo.Properties)
        {
            try { _values[p.Name] = p.Value; } catch { }
        }
    }

    public object? this[string name] => _values.TryGetValue(name, out var v) ? v : null;

    public string Str(string name) => this[name] is { } v ? Clean(v.ToString()) : "";

    public long? Long(string name)
    {
        try { return this[name] is { } v ? Convert.ToInt64(v) : null; }
        catch { return null; }
    }

    public int Int(string name) => (int)Math.Clamp(Long(name) ?? 0, int.MinValue, int.MaxValue);

    public bool? Bool(string name) => this[name] is bool b ? b : null;

    public DateTime? Date(string name)
    {
        var s = Str(name);
        if (s.Length < 14) return null;
        try { return ManagementDateTimeConverter.ToDateTime(s); }
        catch { return null; }
    }

    public byte[]? Bytes(string name) => this[name] as byte[];

    /// <summary>Remove espaços extras, caracteres nulos e textos de preenchimento que alguns fabricantes deixam.</summary>
    public static string Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = string.Join(' ', s.Replace('\0', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));
        string[] junk = { "To be filled by O.E.M.", "Default string", "System Product Name", "System manufacturer",
                          "Not Applicable", "Not Specified", "None", "Undefined", "Unknown", "O.E.M.", "OEM" };
        return junk.Any(j => s.Equals(j, StringComparison.OrdinalIgnoreCase)) ? "" : s;
    }
}

/// <summary>Consultas WMI com tempo limite, para que um provedor travado não congele a tela.</summary>
internal static class Wmi
{
    public const string Cimv2 = @"\\.\root\cimv2";
    public const string Storage = @"\\.\root\Microsoft\Windows\Storage";
    public const string RootWmi = @"\\.\root\wmi";

    public static List<WmiRow> Query(string wql, string ns = Cimv2, int timeoutSec = 20)
    {
        var rows = new List<WmiRow>();
        Each(wql, mo => rows.Add(new WmiRow(mo)), ns, timeoutSec);
        return rows;
    }

    public static WmiRow? First(string wql, string ns = Cimv2) => Query(wql, ns).FirstOrDefault();

    /// <summary>Percorre os objetos crus (necessário para seguir associações com GetRelated).</summary>
    public static void Each(string wql, Action<ManagementObject> action, string ns = Cimv2, int timeoutSec = 20)
    {
        var options = new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(timeoutSec), ReturnImmediately = true, Rewindable = false };
        using var searcher = new ManagementObjectSearcher(new ManagementScope(ns), new ObjectQuery(wql), options);
        using var results = searcher.Get();
        foreach (ManagementObject mo in results)
        {
            using (mo) action(mo);
        }
    }
}
