using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using BottleneckHunter.Core.Optimize;

namespace BottleneckHunter.Views.Optimize;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>Um ajuste na tela.</summary>
public sealed class TweakItem : Observable
{
    public TweakItem(Tweak tweak, int index)
    {
        Tweak = tweak;
        Index = index;
        IsAvailable = tweak.IsAvailable;
    }

    public Tweak Tweak { get; }
    public int Index { get; }
    public string Title => Tweak.Title;
    public string What => Tweak.What;
    public string Why => Tweak.Why;
    public string Caveat => Tweak.Caveat;
    public bool HasCaveat => Tweak.Caveat.Length > 0;
    public bool Recommended => Tweak.Recommended;
    public bool NeedsRestart => Tweak.NeedsRestart;
    public string Impact => Tweak.Impact.ToString();
    public string ImpactText => Tweak.Impact switch
    {
        TweakImpact.Grande => "Impacto grande",
        TweakImpact.Medio => "Impacto médio",
        _ => "Impacto pequeno",
    };

    public bool IsAvailable { get; }
    public bool IsUnavailable => !IsAvailable;
    public string? UnavailableReason => IsAvailable ? null : Tweak.UnavailableReason;

    private bool _isOn;
    public bool IsOn { get => _isOn; set => Set(ref _isOn, value); }

    private bool _canRestore;
    public bool CanRestore { get => _canRestore; set => Set(ref _canRestore, value); }

    /// <summary>Relê o estado real no Windows.</summary>
    public void Refresh()
    {
        try { IsOn = IsAvailable && Tweak.IsApplied(); }
        catch { IsOn = false; }
        CanRestore = TweakManager.HasBackup(Tweak);
    }
}

public sealed record TweakGroup(string Header, IReadOnlyList<TweakItem> Items);

/// <summary>Um programa de inicialização na tela.</summary>
public sealed class StartupRow : Observable
{
    public StartupRow(StartupItem item, int index)
    {
        Item = item;
        Index = index;
        _isOn = item.Enabled;
        _canRestore = StartupManager.HasBackup(item);
    }

    public StartupItem Item { get; }
    public int Index { get; }
    public string Name => Item.Name;
    public string Command => Item.Command;
    public string Subtitle => Item.Publisher is { } p ? $"{p}  ·  {Item.SourceLabel}" : Item.SourceLabel;
    public StartupAdvice Advice => Item.Advice;
    public string AdviceText => Item.AdviceText;
    public string AdviceLabel => Item.Advice switch
    {
        StartupAdvice.PodeDesativar => "Pode desativar",
        StartupAdvice.MelhorManter => "Melhor manter",
        _ => "Decida você",
    };

    private ImageSource? _icon;
    public ImageSource? Icon { get => _icon; set => Set(ref _icon, value); }

    private bool _isOn;
    public bool IsOn { get => _isOn; set => Set(ref _isOn, value); }

    private bool _canRestore;
    public bool CanRestore { get => _canRestore; set => Set(ref _canRestore, value); }

    public void Refresh()
    {
        IsOn = Item.Enabled;
        CanRestore = StartupManager.HasBackup(Item);
    }
}
