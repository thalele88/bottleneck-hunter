using System.Text.RegularExpressions;

namespace BottleneckHunter.Core.Optimize;

/// <summary>
/// Anotações do módulo Otimizar: o valor ORIGINAL de tudo que o app mudou, para o botão Restaurar.
/// Fica em %AppData%\BottleneckHunter\optimize.json.
/// Segurança: o arquivo só guarda valores. Os lugares do Windows que podem ser alterados
/// (chaves do registro, nomes) vêm sempre do código, nunca deste arquivo.
/// </summary>
public sealed class OptimizeData
{
    /// <summary>Ajuste → (posição → valor original; null = não existia).</summary>
    public Dictionary<string, Dictionary<string, string?>> TweakBackups { get; set; } = new();

    /// <summary>Programa de inicialização → estado original em hexadecimal (null = não existia).</summary>
    public Dictionary<string, string?> StartupBackups { get; set; } = new();
}

public static class OptimizeStore
{
    private const string FileName = "optimize";
    private static readonly Regex SafeValue = new("^[0-9A-Za-z{}\\-]{0,64}$", RegexOptions.Compiled);
    private static OptimizeData? _data;
    private static readonly object Lock = new();

    public static OptimizeData Data
    {
        get
        {
            lock (Lock) return _data ??= Load();
        }
    }

    public static void Save()
    {
        lock (Lock) JsonStore.Save(FileName, Data);
    }

    /// <summary>Lê e descarta tudo que tiver formato estranho.</summary>
    private static OptimizeData Load()
    {
        var raw = JsonStore.Load<OptimizeData>(FileName);
        var clean = new OptimizeData();

        foreach (var (id, slots) in (raw.TweakBackups ?? new()).Take(64))
        {
            if (id is not { Length: > 0 and <= 40 } || slots == null) continue;
            var ok = new Dictionary<string, string?>();
            foreach (var (slot, value) in slots.Take(16))
                if (slot is { Length: > 0 and <= 4 } && (value == null || SafeValue.IsMatch(value)))
                    ok[slot] = value;
            clean.TweakBackups[id] = ok;
        }

        foreach (var (id, value) in (raw.StartupBackups ?? new()).Take(500))
            if (id is { Length: > 0 and <= 400 } && (value == null || SafeValue.IsMatch(value)))
                clean.StartupBackups[id] = value;

        return clean;
    }
}
