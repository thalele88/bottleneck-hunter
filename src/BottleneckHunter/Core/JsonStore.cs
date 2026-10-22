using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BottleneckHunter.Core;

/// <summary>
/// Guarda e lê arquivos JSON em %AppData%\BottleneckHunter.
/// Como o app roda como administrador, tudo que vem do disco é tratado como não confiável:
/// tamanho limitado, links simbólicos recusados e qualquer erro vira o valor padrão.
/// Quem lê deve validar os valores (faixas, chaves conhecidas) antes de usá-los.
/// </summary>
public static class JsonStore
{
    private const long MaxFileBytes = 512 * 1024;
    private static readonly Regex SafeName = new("^[a-z0-9-]{1,40}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BottleneckHunter");

    private static string PathFor(string name)
    {
        if (!SafeName.IsMatch(name)) throw new ArgumentException("Nome de arquivo inválido", nameof(name));
        return Path.Combine(Folder, name + ".json");
    }

    public static T Load<T>(string name) where T : new()
    {
        try
        {
            var info = new FileInfo(PathFor(name));
            if (!info.Exists || info.Length > MaxFileBytes || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                return new T();
            return JsonSerializer.Deserialize<T>(File.ReadAllText(info.FullName)) ?? new T();
        }
        catch
        {
            return new T();
        }
    }

    public static void Save<T>(string name, T value)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var path = PathFor(name);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            // Sem permissão ou disco cheio: a preferência simplesmente não é salva.
        }
    }
}
