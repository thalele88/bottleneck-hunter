namespace BottleneckHunter.Core;

/// <summary>
/// Preferências gerais do app (tema etc.). Cada módulo guarda as suas próprias
/// em um arquivo separado via <see cref="JsonStore"/>, para não haver conflito.
/// </summary>
public sealed class AppSettings
{
    private const string Name = "settings";

    public bool DarkTheme { get; set; } = true;
    public string LastPage { get; set; } = "overview";

    public static AppSettings Load()
    {
        var s = JsonStore.Load<AppSettings>(Name);
        if (s.LastPage is not { Length: > 0 and < 40 }) s.LastPage = "overview";
        return s;
    }

    public void Save() => JsonStore.Save(Name, this);
}
