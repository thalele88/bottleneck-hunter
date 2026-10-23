using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using static BottleneckHunter.Core.Optimize.NativeMethods;

namespace BottleneckHunter.Core.Optimize;

public enum TweakImpact { Pequeno, Medio, Grande }

/// <summary>
/// Um ajuste do Windows que pode ser ligado e desfeito.
/// Cada ajuste sabe ler o estado atual (<see cref="Capture"/>), aplicar a mudança
/// e voltar a um estado anotado (<see cref="RestoreCore"/>), validando o que recebe.
/// </summary>
public abstract class Tweak
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Category { get; init; }
    /// <summary>O que o ajuste faz, em uma frase simples.</summary>
    public required string What { get; init; }
    /// <summary>Por que ajuda e quando faz diferença.</summary>
    public required string Why { get; init; }
    /// <summary>Do que abrir mão (vazio se não houver).</summary>
    public string Caveat { get; init; } = "";
    public TweakImpact Impact { get; init; } = TweakImpact.Medio;
    public bool NeedsRestart { get; init; }
    public bool Recommended { get; init; } = true;

    public virtual bool IsAvailable => true;
    /// <summary>Explica por que o ajuste não está disponível neste PC.</summary>
    public virtual string? UnavailableReason => null;

    public abstract bool IsApplied();
    public abstract Dictionary<string, string?> Capture();
    public abstract void ApplyCore();
    /// <summary>Volta ao estado anotado. Valores inválidos são trocados pelo padrão do Windows.</summary>
    public abstract void RestoreCore(IReadOnlyDictionary<string, string?> original);
    /// <summary>Estado padrão do Windows, usado se não houver anotação do valor original.</summary>
    public abstract Dictionary<string, string?> WindowsDefault();

    protected static int? ParseInt(IReadOnlyDictionary<string, string?> d, string slot, int min, int max) =>
        d.TryGetValue(slot, out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v >= min && v <= max ? v : null;

    protected static string Str(int v) => v.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Um valor DWORD do registro mexido por um ajuste.</summary>
/// <param name="AbsentMeans">Como o Windows se comporta quando o valor não existe.</param>
public sealed record RegDword(RegistryHive Hive, string Path, string Name, int Desired, int? AbsentMeans);

/// <summary>Ajuste feito só com valores DWORD do registro.</summary>
public sealed class RegistryTweak : Tweak
{
    public required RegDword[] Values { get; init; }

    private static RegistryKey Root(RegistryHive hive) => RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);

    private static int? Read(RegDword v)
    {
        using var key = Root(v.Hive).OpenSubKey(v.Path);
        return key?.GetValue(v.Name) is int i ? i : null;
    }

    public override bool IsApplied() => Values.All(v => (Read(v) ?? v.AbsentMeans) == v.Desired);

    public override Dictionary<string, string?> Capture()
    {
        var d = new Dictionary<string, string?>();
        for (int i = 0; i < Values.Length; i++)
            d[Str(i)] = Read(Values[i]) is { } cur ? Str(cur) : null;
        return d;
    }

    public override void ApplyCore()
    {
        foreach (var v in Values)
        {
            using var key = Root(v.Hive).CreateSubKey(v.Path, writable: true);
            key.SetValue(v.Name, v.Desired, RegistryValueKind.DWord);
        }
    }

    public override void RestoreCore(IReadOnlyDictionary<string, string?> original)
    {
        for (int i = 0; i < Values.Length; i++)
        {
            var v = Values[i];
            using var key = Root(v.Hive).CreateSubKey(v.Path, writable: true);
            // Todos os valores mexidos aqui são 0, 1 ou 2; qualquer outra coisa no arquivo é ignorada.
            if (ParseInt(original, Str(i), 0, 2) is { } old)
                key.SetValue(v.Name, old, RegistryValueKind.DWord);
            else
                key.DeleteValue(v.Name, throwOnMissingValue: false);
        }
    }

    public override Dictionary<string, string?> WindowsDefault()
    {
        // Sem anotação: apagar o valor devolve o comportamento padrão do Windows.
        var d = new Dictionary<string, string?>();
        for (int i = 0; i < Values.Length; i++) d[Str(i)] = null;
        return d;
    }
}

/// <summary>Troca o plano de energia para "Alto desempenho" (ou "Desempenho máximo", se existir).</summary>
public sealed class PowerPlanTweak : Tweak
{
    private static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private static readonly Guid Ultimate = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    private static List<Guid> Schemes()
    {
        var list = new List<Guid>();
        for (uint i = 0; i < 64; i++)
        {
            var g = Guid.Empty;
            uint size = (uint)Marshal.SizeOf<Guid>();
            if (PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ACCESS_SCHEME, i, ref g, ref size) != 0) break;
            list.Add(g);
        }
        return list;
    }

    private static Guid? Target()
    {
        var all = Schemes();
        if (all.Contains(Ultimate)) return Ultimate;
        if (all.Contains(HighPerformance)) return HighPerformance;
        return null;
    }

    private static Guid? Active()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0) return null;
        try { return Marshal.PtrToStructure<Guid>(ptr); }
        finally { LocalFree(ptr); }
    }

    private static void SetActive(Guid g)
    {
        if (PowerSetActiveScheme(IntPtr.Zero, ref g) != 0)
            throw new InvalidOperationException("O Windows recusou a troca do plano de energia.");
    }

    public override bool IsAvailable => Target() != null;

    public override string? UnavailableReason =>
        "Este PC só oferece o plano \"Equilibrado\" (comum em notebooks modernos). " +
        "Use Configurações > Sistema > Energia > Modo de energia > \"Melhor desempenho\".";

    public override bool IsApplied() => Active() is { } a && (a == Ultimate || a == HighPerformance);

    public override Dictionary<string, string?> Capture() => new() { ["0"] = Active()?.ToString() };

    public override void ApplyCore()
    {
        if (Target() is { } t) SetActive(t);
    }

    public override void RestoreCore(IReadOnlyDictionary<string, string?> original)
    {
        // Só aceitamos um plano que realmente exista neste PC.
        var target = Balanced;
        if (original.TryGetValue("0", out var s) && Guid.TryParse(s, out var g) && Schemes().Contains(g))
            target = g;
        SetActive(target);
    }

    public override Dictionary<string, string?> WindowsDefault() => new() { ["0"] = Balanced.ToString() };
}

/// <summary>Desliga a "aceleração do mouse" (Aumentar precisão do ponteiro).</summary>
public sealed class MouseAccelTweak : Tweak
{
    private static int[] Get()
    {
        var v = new int[3];
        SystemParametersInfo(SPI_GETMOUSE, 0, v, 0);
        return v;
    }

    private static void Set(int[] v)
    {
        if (!SystemParametersInfo(SPI_SETMOUSE, 0, v, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE))
            throw new InvalidOperationException("O Windows recusou a mudança do mouse.");
    }

    public override bool IsApplied() => Get()[2] == 0;

    public override Dictionary<string, string?> Capture()
    {
        var v = Get();
        return new() { ["0"] = Str(v[0]), ["1"] = Str(v[1]), ["2"] = Str(v[2]) };
    }

    public override void ApplyCore() => Set(new[] { 0, 0, 0 });

    public override void RestoreCore(IReadOnlyDictionary<string, string?> original) => Set(new[]
    {
        ParseInt(original, "0", 0, 20) ?? 6,
        ParseInt(original, "1", 0, 20) ?? 10,
        ParseInt(original, "2", 0, 2) ?? 1,
    });

    public override Dictionary<string, string?> WindowsDefault() => new() { ["0"] = "6", ["1"] = "10", ["2"] = "1" };
}

/// <summary>Desliga a animação de minimizar e maximizar janelas.</summary>
public sealed class WindowAnimationTweak : Tweak
{
    private static int Get()
    {
        var ai = new ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<ANIMATIONINFO>() };
        SystemParametersInfo(SPI_GETANIMATION, ai.cbSize, ref ai, 0);
        return ai.iMinAnimate;
    }

    private static void Set(int on)
    {
        var ai = new ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf<ANIMATIONINFO>(), iMinAnimate = on };
        if (!SystemParametersInfo(SPI_SETANIMATION, ai.cbSize, ref ai, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE))
            throw new InvalidOperationException("O Windows recusou a mudança das animações.");
    }

    public override bool IsApplied() => Get() == 0;
    public override Dictionary<string, string?> Capture() => new() { ["0"] = Str(Get()) };
    public override void ApplyCore() => Set(0);
    public override void RestoreCore(IReadOnlyDictionary<string, string?> original) => Set(ParseInt(original, "0", 0, 1) ?? 1);
    public override Dictionary<string, string?> WindowsDefault() => new() { ["0"] = "1" };
}

/// <summary>Desliga o atalho "aperte Shift 5 vezes" das Teclas de Aderência.</summary>
public sealed class StickyKeysTweak : Tweak
{
    private const uint SKF_STICKYKEYSON = 0x1;
    private const uint SKF_HOTKEYACTIVE = 0x4;
    private const uint SKF_CONFIRMHOTKEY = 0x8;
    private const uint DefaultFlags = 510;

    private static uint Get()
    {
        var sk = new STICKYKEYS { cbSize = (uint)Marshal.SizeOf<STICKYKEYS>() };
        SystemParametersInfo(SPI_GETSTICKYKEYS, sk.cbSize, ref sk, 0);
        return sk.dwFlags;
    }

    private static void Set(uint flags)
    {
        var sk = new STICKYKEYS { cbSize = (uint)Marshal.SizeOf<STICKYKEYS>(), dwFlags = flags };
        if (!SystemParametersInfo(SPI_SETSTICKYKEYS, sk.cbSize, ref sk, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE))
            throw new InvalidOperationException("O Windows recusou a mudança do teclado.");
    }

    public override bool IsApplied() => (Get() & SKF_HOTKEYACTIVE) == 0;
    public override Dictionary<string, string?> Capture() => new() { ["0"] = Str((int)Get()) };

    // Não mexe no recurso em si (quem usa continua usando): só tira o atalho.
    public override void ApplyCore() => Set(Get() & ~(SKF_HOTKEYACTIVE | SKF_CONFIRMHOTKEY));

    public override void RestoreCore(IReadOnlyDictionary<string, string?> original)
    {
        var flags = ParseInt(original, "0", 0, 0xFFFF) is { } f ? (uint)f : DefaultFlags;
        // Preserva o estado atual de "ligado/desligado"; restaura só os bits do atalho.
        var current = Get();
        Set((current & SKF_STICKYKEYSON) | (flags & ~SKF_STICKYKEYSON));
    }

    public override Dictionary<string, string?> WindowsDefault() => new() { ["0"] = Str((int)DefaultFlags) };
}

/// <summary>
/// Lista de ajustes, com o texto didático de cada um.
/// Só entram ajustes seguros, com efeito real e que voltam ao original.
/// </summary>
public static class TweakCatalog
{
    private const string Games = "Jogos";
    private const string Energy = "Energia";
    private const string Input = "Mouse e teclado";
    private const string Visual = "Visual";
    private const string Background = "Segundo plano e distrações";

    private static readonly bool IsLaptop = HasBattery();

    public static readonly IReadOnlyList<Tweak> All = new Tweak[]
    {
        new RegistryTweak
        {
            Id = "game-mode", Category = Games, Title = "Modo de Jogo do Windows",
            What = "Quando um jogo está aberto, o Windows dá prioridade a ele e adia atualizações e tarefas de fundo.",
            Why = "Evita que o Windows Update ou a manutenção automática roubem CPU e disco no meio da partida. Ajuda principalmente em PCs mais modestos.",
            Impact = TweakImpact.Medio,
            Values = new[] { new RegDword(RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1, 1) },
        },
        new RegistryTweak
        {
            Id = "game-dvr", Category = Games, Title = "Desligar a gravação em segundo plano (Xbox Game Bar)",
            What = "Para de gravar os últimos minutos de jogo o tempo todo, esperando você apertar \"gravar o que aconteceu\".",
            Why = "Essa gravação contínua usa a placa de vídeo e o disco sem você perceber. Desligar devolve alguns FPS e diminui travadinhas.",
            Caveat = "Você perde o recurso \"Gravar o que aconteceu\" (Win + Alt + G). Gravar manualmente continua funcionando.",
            Impact = TweakImpact.Medio,
            Values = new[]
            {
                new RegDword(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0, 1),
                new RegDword(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0, 1),
            },
        },
        new RegistryTweak
        {
            Id = "hags", Category = Games, Title = "Agendamento de GPU acelerado por hardware",
            What = "A própria placa de vídeo passa a organizar a fila de trabalho dela, em vez do processador fazer isso.",
            Why = "Tira um pouco de carga da CPU e pode reduzir a latência. É exigido pelo DLSS 3 (geração de quadros) das placas NVIDIA RTX 40 e mais novas.",
            Caveat = "Precisa de placa compatível (NVIDIA GTX 10 ou mais nova, AMD RX 5000 ou mais nova) e driver atualizado. Em placas antigas o Windows ignora.",
            Impact = TweakImpact.Pequeno, NeedsRestart = true,
            Values = new[] { new RegDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2, null) },
        },
        new PowerPlanTweak
        {
            Id = "power-plan", Category = Energy, Title = "Plano de energia de alto desempenho",
            What = "Troca o plano de energia para \"Alto desempenho\" (ou \"Desempenho máximo\", se existir no seu PC).",
            Why = "O processador para de economizar energia baixando a frequência e responde na hora quando o jogo pede mais. Reduz engasgos causados pela CPU \"acordando\".",
            Caveat = IsLaptop ? "Em notebook, a bateria dura menos e o PC esquenta um pouco mais. Use na tomada." : "O PC gasta um pouco mais de energia parado.",
            Impact = TweakImpact.Grande, Recommended = !IsLaptop,
        },
        new RegistryTweak
        {
            Id = "power-throttling", Category = Energy, Title = "Desligar o \"Power Throttling\"",
            What = "Impede o Windows de colocar programas em segundo plano no modo econômico (EcoQoS).",
            Why = "Programas que você deixa rodando atrás do jogo (Discord, gravação, streaming) não são desacelerados. Em processadores com núcleos P e E, evita que eles caiam nos núcleos lentos.",
            Caveat = "Em notebook, gasta mais bateria. Em PCs de mesa praticamente não há desvantagem.",
            Impact = TweakImpact.Pequeno, Recommended = !IsLaptop,
            Values = new[] { new RegDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1, 0) },
        },
        new MouseAccelTweak
        {
            Id = "mouse-accel", Category = Input, Title = "Desligar a aceleração do mouse",
            What = "Desliga a opção \"Aumentar precisão do ponteiro\": o cursor passa a andar sempre a mesma distância para o mesmo movimento da mão.",
            Why = "Com aceleração, mover o mouse rápido anda mais que mover devagar, e a mira fica imprevisível. Sem ela, a memória muscular funciona. Quase todo jogador profissional desliga.",
            Caveat = "No começo o mouse pode parecer \"lento\" na área de trabalho; ajuste a sensibilidade se precisar.",
            Impact = TweakImpact.Medio,
        },
        new StickyKeysTweak
        {
            Id = "sticky-keys", Category = Input, Title = "Desligar o atalho \"Shift 5 vezes\"",
            What = "Apertar Shift cinco vezes deixa de abrir a janela das Teclas de Aderência.",
            Why = "Em jogos que usam Shift para correr, essa janela aparece do nada e tira você do jogo (Alt+Tab forçado).",
            Caveat = "O recurso de acessibilidade continua disponível nas Configurações para quem precisa.",
            Impact = TweakImpact.Pequeno,
        },
        new WindowAnimationTweak
        {
            Id = "window-anim", Category = Visual, Title = "Sem animação ao minimizar e maximizar",
            What = "As janelas abrem, minimizam e maximizam na hora, sem o efeito de \"zoom\".",
            Why = "Deixa o Windows com sensação mais rápida, principalmente em PCs com placa de vídeo integrada ou fraca.",
            Caveat = "É só visual: em PCs fortes a diferença é apenas de gosto.",
            Impact = TweakImpact.Pequeno, Recommended = false,
        },
        new RegistryTweak
        {
            Id = "transparency", Category = Visual, Title = "Desligar os efeitos de transparência",
            What = "Menu Iniciar, barra de tarefas e janelas deixam de ter o fundo translúcido e desfocado.",
            Why = "O desfoque é desenhado pela placa de vídeo o tempo todo. Em GPUs integradas e notebooks isso economiza um pouco de desempenho e bateria.",
            Caveat = "O visual fica mais \"chapado\".",
            Impact = TweakImpact.Pequeno, Recommended = false,
            Values = new[] { new RegDword(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0, 1) },
        },
        new RegistryTweak
        {
            Id = "background-apps", Category = Background, Title = "Bloquear apps da Loja em segundo plano",
            What = "Apps da Microsoft Store (Clima, Notícias, Fotos e outros) deixam de rodar escondidos quando estão fechados.",
            Why = "Menos programas acordando sozinhos significa menos uso de CPU, RAM e internet enquanto você joga.",
            Caveat = "Apps da Loja param de mostrar notificações quando fechados. No Windows 11 o efeito é menor, porque cada app tem sua própria opção.",
            Impact = TweakImpact.Pequeno,
            Values = new[] { new RegDword(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1, 0) },
        },
        new RegistryTweak
        {
            Id = "tips", Category = Background, Title = "Desligar dicas e sugestões do Windows",
            What = "Para as notificações de \"dicas\", as sugestões no menu Iniciar e a tela de boas-vindas depois das atualizações.",
            Why = "Menos interrupções e menos processos do Windows consultando a internet para mostrar propaganda.",
            Impact = TweakImpact.Pequeno,
            Values = new[]
            {
                new RegDword(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338389Enabled", 0, 1),
                new RegDword(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", 0, 1),
                new RegDword(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-310093Enabled", 0, 1),
                new RegDword(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SoftLandingEnabled", 0, 1),
            },
        },
    };

    public static Tweak? Find(string id) => All.FirstOrDefault(t => t.Id == id);
}

/// <summary>Liga e desfaz ajustes, guardando o valor original antes da primeira mudança.</summary>
public static class TweakManager
{
    public static bool HasBackup(Tweak t) => OptimizeStore.Data.TweakBackups.ContainsKey(t.Id);

    public static void Apply(Tweak t)
    {
        if (!t.IsAvailable || t.IsApplied()) return;
        var data = OptimizeStore.Data;
        if (!data.TweakBackups.ContainsKey(t.Id))
        {
            data.TweakBackups[t.Id] = t.Capture();
            OptimizeStore.Save(); // anota ANTES de mudar: se algo der errado, dá para voltar
        }
        t.ApplyCore();
    }

    /// <summary>Volta ao valor original anotado; sem anotação, volta ao padrão do Windows.</summary>
    public static void Restore(Tweak t)
    {
        var data = OptimizeStore.Data;
        var original = data.TweakBackups.TryGetValue(t.Id, out var b) ? b : t.WindowsDefault();
        t.RestoreCore(original);
        data.TweakBackups.Remove(t.Id);
        OptimizeStore.Save();
    }
}
