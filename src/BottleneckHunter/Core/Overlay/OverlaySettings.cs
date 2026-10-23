namespace BottleneckHunter.Core.Overlay;

/// <summary>Canto da tela onde o overlay fica.</summary>
public enum OverlayCorner { TopLeft, TopRight, BottomLeft, BottomRight }

/// <summary>
/// Preferências do overlay, salvas em %AppData%\BottleneckHunter\overlay.json.
/// O arquivo vem do disco, então tudo é conferido em <see cref="Load"/>.
/// </summary>
public sealed class OverlaySettings
{
    private const string Name = "overlay";

    // O que mostrar
    public bool ShowFps { get; set; } = true;
    public bool ShowFrametime { get; set; } = true;
    public bool ShowLows { get; set; } = true;
    public bool ShowGraph { get; set; } = true;
    public bool ShowCpu { get; set; } = true;
    public bool ShowCpuTemp { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowGpuTemp { get; set; } = true;
    public bool ShowGpuPower { get; set; }
    public bool ShowRam { get; set; } = true;
    public bool ShowVram { get; set; } = true;
    public bool ShowGame { get; set; }
    public bool ShowClock { get; set; }

    // Aparência
    public OverlayCorner Corner { get; set; } = OverlayCorner.TopLeft;
    /// <summary>0 = pequeno, 1 = médio, 2 = grande.</summary>
    public int Size { get; set; } = 1;
    /// <summary>Opacidade do fundo, de 0 (só texto) a 0.9.</summary>
    public double BackgroundOpacity { get; set; } = 0.55;
    /// <summary>Tudo numa linha só, no topo ou na base da tela.</summary>
    public bool Compact { get; set; }

    /// <summary>Mostra o overlay assim que o app abre.</summary>
    public bool StartWithApp { get; set; }

    public double Scale => Size switch { 0 => 0.85, 2 => 1.3, _ => 1.0 };

    public static OverlaySettings Load()
    {
        var s = JsonStore.Load<OverlaySettings>(Name);
        if (!Enum.IsDefined(s.Corner)) s.Corner = OverlayCorner.TopLeft;
        s.Size = Math.Clamp(s.Size, 0, 2);
        s.BackgroundOpacity = double.IsFinite(s.BackgroundOpacity) ? Math.Clamp(s.BackgroundOpacity, 0, 0.9) : 0.55;
        return s;
    }

    public void Save() => JsonStore.Save(Name, this);
}
