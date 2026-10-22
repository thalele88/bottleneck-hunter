using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BottleneckHunter.Core.Optimize;

namespace BottleneckHunter.Views.Optimize;

public partial class OptimizeView : UserControl
{
    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    private readonly List<TweakItem> _tweaks;
    private readonly ObservableCollection<StartupRow> _startup = new();
    private readonly DispatcherTimer _memTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private MemoryBreakdown? _lastMemory;
    private bool _memBusy;
    private bool _startupLoaded;
    private int _memTicks;

    /// <summary>Número animado do resultado da limpeza (conta de 0 até o valor liberado).</summary>
    private static readonly DependencyProperty FreedCounterProperty = DependencyProperty.Register(
        "FreedCounter", typeof(double), typeof(OptimizeView),
        new PropertyMetadata(0.0, (d, e) => ((OptimizeView)d).FreedValue.Text = FormatMb((double)e.NewValue)));

    public OptimizeView()
    {
        InitializeComponent();

        int index = 0;
        _tweaks = TweakCatalog.All.Select(t => new TweakItem(t, index++)).ToList();
        TweakGroups.ItemsSource = _tweaks
            .GroupBy(t => t.Tweak.Category)
            .Select(g => new TweakGroup(g.Key.ToUpperInvariant(), g.ToList()))
            .ToList();
        StartupList.ItemsSource = _startup;

        _memTimer.Tick += (_, _) => RefreshMemory();
        _toastTimer.Tick += (_, _) => HideToast();

        Loaded += (_, _) =>
        {
            AnimateTabsIn();
            RefreshMemory();
            RefreshProcesses();
            RefreshTweaks();
            if (!_startupLoaded) LoadStartup();
            _memTimer.Start();
        };
        Unloaded += (_, _) => _memTimer.Stop();
    }

    // ================= Abas =================

    private void AnimateTabsIn()
    {
        int i = 0;
        foreach (UIElement tab in Tabs.Children)
        {
            var tt = new TranslateTransform(0, 16);
            tab.RenderTransform = tt;
            tab.Opacity = 0;
            var begin = TimeSpan.FromMilliseconds(70 * i++);
            tab.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)) { BeginTime = begin });
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(420)) { BeginTime = begin, EasingFunction = Ease });
        }
    }

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        if (PanelHost == null) return; // ainda montando a tela

        MemoryPanel.Visibility = sender == TabMemory ? Visibility.Visible : Visibility.Collapsed;
        TweaksPanel.Visibility = sender == TabTweaks ? Visibility.Visible : Visibility.Collapsed;
        StartupPanel.Visibility = sender == TabStartup ? Visibility.Visible : Visibility.Collapsed;

        if (sender == TabMemory) RefreshProcesses();
        if (sender == TabTweaks) RefreshTweaks();

        // O conteúdo novo entra deslizando de leve para o lado.
        PanelHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
        PanelHost.RenderTransform.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(360)) { EasingFunction = Ease });
    }

    /// <summary>Cada cartão de lista aparece em sequência, um pouco depois do anterior.</summary>
    private void OnItemLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el) return;
        int index = el.DataContext switch { TweakItem t => t.Index, StartupRow s => s.Index, _ => 0 };
        var begin = TimeSpan.FromMilliseconds(Math.Min(index, 10) * 45);

        var tt = new TranslateTransform(0, 14);
        el.RenderTransform = tt;
        el.Opacity = 0;
        el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { BeginTime = begin });
        tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(380)) { BeginTime = begin, EasingFunction = Ease });
    }

    // ================= Memória =================

    private async void RefreshMemory()
    {
        if (_memBusy) return;
        _memBusy = true;
        try
        {
            var m = await Task.Run(MemoryCleaner.Read);
            RenderMemory(m);
            if (++_memTicks % 5 == 0 && MemoryPanel.IsVisible) RefreshProcesses(animate: false);
        }
        catch
        {
            // Leitura falhou desta vez; tenta de novo no próximo ciclo.
        }
        finally
        {
            _memBusy = false;
        }
    }

    private void RenderMemory(MemoryBreakdown m)
    {
        _lastMemory = m;
        RamGauge.Value = m.UsedPercent;
        RamText.Text = $"{m.InUseGb:0.0} de {m.TotalGb:0} GB";
        MemorySummary.Text = $"{m.UsedPercent:0}% em uso  ·  {m.TotalGb - m.InUseGb:0.0} GB disponíveis";

        LegInUse.Text = $"{m.InUseGb:0.0} GB";
        LegModified.Text = $"{m.ModifiedGb:0.00} GB";
        LegStandby.Text = $"{m.StandbyGb:0.0} GB";
        LegFree.Text = $"{m.FreeGb:0.0} GB";
        ListsWarning.Visibility = m.HasLists ? Visibility.Collapsed : Visibility.Visible;

        LayoutMemBar(animate: true);
    }

    private void OnMemBarSize(object sender, SizeChangedEventArgs e) => LayoutMemBar(animate: false);

    /// <summary>A barra empilhada: cada pedaço cresce ou encolhe suavemente até o novo tamanho.</summary>
    private void LayoutMemBar(bool animate)
    {
        double width = MemBarHost.ActualWidth;
        if (width <= 0) return;
        MemBarHost.Clip = new RectangleGeometry(new Rect(0, 0, width, MemBarHost.ActualHeight), 8, 8);
        if (_lastMemory is not { TotalGb: > 0 } m) return;

        void Seg(Border seg, double gb)
        {
            double target = Math.Max(0, gb / m.TotalGb * width);
            if (animate)
                seg.BeginAnimation(WidthProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(600)) { EasingFunction = Ease });
            else
            {
                seg.BeginAnimation(WidthProperty, null);
                seg.Width = target;
            }
        }

        Seg(SegInUse, m.InUseGb);
        Seg(SegModified, m.ModifiedGb);
        Seg(SegStandby, m.StandbyGb);
        Seg(SegFree, m.FreeGb);
    }

    private void OnQuickClean(object sender, RoutedEventArgs e) => Clean(full: false);
    private void OnFullClean(object sender, RoutedEventArgs e) => Clean(full: true);

    private async void Clean(bool full)
    {
        QuickCleanButton.IsEnabled = FullCleanButton.IsEnabled = false;
        Spinner.Visibility = Visibility.Visible;
        SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });

        try
        {
            var r = await Task.Run(() => MemoryCleaner.Clean(full));
            ShowCleanResult(r, full);
            RenderMemory(r.After);
            RefreshProcesses();
            if (r.Error != null) ShowToast("⚠ " + r.Error);
        }
        catch (Exception ex)
        {
            ShowToast("⚠ Não foi possível limpar a memória: " + ex.Message);
        }
        finally
        {
            SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            Spinner.Visibility = Visibility.Collapsed;
            QuickCleanButton.IsEnabled = FullCleanButton.IsEnabled = true;
        }
    }

    private void ShowCleanResult(MemoryCleanResult r, bool full)
    {
        CleanResult.Visibility = Visibility.Visible;
        var scale = (ScaleTransform)CleanResult.RenderTransform;
        var pop = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(450)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        CleanResult.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));

        BeginAnimation(FreedCounterProperty, new DoubleAnimation(0, r.FreedMb, TimeSpan.FromMilliseconds(900)) { EasingFunction = Ease });

        if (r.FreedMb < 50)
        {
            FreedLabel.Text = "quase nada para liberar";
            CleanDetail.Text = "Os programas já estavam usando só o necessário. Sinal de que a memória está bem cuidada pelo Windows.";
        }
        else
        {
            FreedLabel.Text = "saíram da memória em uso";
            CleanDetail.Text = $"Em uso: {r.Before.InUseGb:0.0} GB → {r.After.InUseGb:0.0} GB. " +
                               "Parte disso volta aos poucos conforme os programas precisarem; é normal.";
        }
        if (full && r.StandbyPurged)
            CleanDetail.Text += $" O cache do Windows também foi esvaziado ({FormatMb(r.CacheClearedMb)}).";

        ShowToast(r.FreedMb >= 50 ? $"✓ Memória limpa: {FormatMb(r.FreedMb)} liberados." : "✓ Limpeza feita. A memória já estava em ordem.");
    }

    private void OnRefreshProcesses(object sender, RoutedEventArgs e) => RefreshProcesses();

    private async void RefreshProcesses(bool animate = true)
    {
        List<ProcessMemory> top;
        try
        {
            top = await Task.Run(() => MemoryCleaner.TopProcesses(8));
        }
        catch
        {
            return;
        }

        double max = top.Count > 0 ? top.Max(p => p.Mb) : 1;
        ProcList.Children.Clear();
        int i = 0;
        foreach (var p in top)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

            var name = new TextBlock
            {
                Text = p.Count > 1 ? $"{p.Name}  ×{p.Count}" : p.Name,
                Style = (Style)FindResource("Body"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            row.Children.Add(name);

            var track = new Border { Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(8, 0, 12, 0) };
            track.SetResourceReference(Border.BackgroundProperty, "Track");
            var fillScale = new ScaleTransform(animate ? 0 : p.Mb / max, 1);
            var fill = new Border { CornerRadius = new CornerRadius(5), RenderTransformOrigin = new Point(0, 0.5), RenderTransform = fillScale };
            fill.SetResourceReference(Border.BackgroundProperty, i == 0 ? "Accent" : "Cpu");
            track.Child = fill;
            Grid.SetColumn(track, 1);
            row.Children.Add(track);

            var value = new TextBlock { Text = FormatMb(p.Mb), Style = (Style)FindResource("Dim"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(value, 2);
            row.Children.Add(value);

            if (animate)
                fillScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(0, p.Mb / max, TimeSpan.FromMilliseconds(550)) { BeginTime = TimeSpan.FromMilliseconds(50 * i), EasingFunction = Ease });

            ProcList.Children.Add(row);
            i++;
        }
    }

    private static string FormatMb(double mb) => mb >= 1024 ? $"{mb / 1024:0.0} GB" : $"{mb:0} MB";

    // ================= Ajustes do Windows =================

    private void RefreshTweaks()
    {
        foreach (var t in _tweaks) t.Refresh();
        UpdateTweakSummary();
    }

    private void UpdateTweakSummary()
    {
        int available = _tweaks.Count(t => t.IsAvailable);
        int on = _tweaks.Count(t => t.IsOn);
        TweaksCount.Text = $"{on} de {available} ajustes ligados";
        TweaksSummary.Text = $"{on} de {available} ligados";
        double frac = available > 0 ? (double)on / available : 0;
        TweaksProgress.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(frac, TimeSpan.FromMilliseconds(500)) { EasingFunction = Ease });
    }

    private void OnTweakToggle(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: TweakItem item } cb) return;
        bool wantOn = cb.IsChecked == true;
        try
        {
            if (wantOn)
            {
                TweakManager.Apply(item.Tweak);
                ShowToast(item.NeedsRestart
                    ? $"✓ \"{item.Title}\" aplicado. Reinicie o PC para valer."
                    : $"✓ \"{item.Title}\" aplicado.");
            }
            else
            {
                RestoreTweak(item);
            }
        }
        catch (Exception ex)
        {
            ShowToast($"⚠ Não deu para mudar \"{item.Title}\": {ex.Message}");
        }
        item.Refresh();
        UpdateTweakSummary();
    }

    private void OnTweakRestore(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TweakItem item }) return;
        try
        {
            RestoreTweak(item);
        }
        catch (Exception ex)
        {
            ShowToast($"⚠ Não deu para restaurar \"{item.Title}\": {ex.Message}");
        }
        item.Refresh();
        UpdateTweakSummary();
    }

    private void RestoreTweak(TweakItem item)
    {
        bool hadBackup = TweakManager.HasBackup(item.Tweak);
        TweakManager.Restore(item.Tweak);
        bool stillOn = item.Tweak.IsApplied();
        if (stillOn && !hadBackup)
            ShowToast($"\"{item.Title}\" já é o comportamento padrão do Windows, por isso continua ligado.");
        else if (stillOn)
            ShowToast($"↺ \"{item.Title}\" voltou ao original (que já era assim antes).");
        else
            ShowToast(item.NeedsRestart ? $"↺ \"{item.Title}\" restaurado. Reinicie o PC para valer." : $"↺ \"{item.Title}\" restaurado ao original.");
    }

    private void OnApplyRecommended(object sender, RoutedEventArgs e)
    {
        int applied = 0, failed = 0;
        bool restart = false;
        foreach (var item in _tweaks.Where(t => t.Recommended && t.IsAvailable && !t.IsOn))
        {
            try
            {
                TweakManager.Apply(item.Tweak);
                applied++;
                restart |= item.NeedsRestart;
            }
            catch
            {
                failed++;
            }
            item.Refresh();
        }
        UpdateTweakSummary();

        var msg = applied == 0 ? "Os ajustes recomendados já estavam todos ligados. 👍" : $"✓ {applied} ajuste(s) aplicado(s).";
        if (restart) msg += " Reinicie o PC para todos valerem.";
        if (failed > 0) msg += $" {failed} não puderam ser aplicados.";
        ShowToast(msg);
    }

    private void OnRestoreAllTweaks(object sender, RoutedEventArgs e)
    {
        var changed = _tweaks.Where(t => TweakManager.HasBackup(t.Tweak)).ToList();
        if (changed.Count == 0)
        {
            ShowToast("Nada para restaurar: este app ainda não mudou nenhum ajuste.");
            return;
        }
        int failed = 0;
        foreach (var item in changed)
        {
            try { TweakManager.Restore(item.Tweak); }
            catch { failed++; }
            item.Refresh();
        }
        UpdateTweakSummary();
        ShowToast(failed == 0
            ? $"↺ {changed.Count} ajuste(s) voltaram ao original."
            : $"↺ {changed.Count - failed} ajuste(s) restaurados; {failed} falharam.");
    }

    // ================= Inicialização =================

    private void OnRefreshStartup(object sender, RoutedEventArgs e) => LoadStartup();

    private async void LoadStartup()
    {
        _startupLoaded = true;
        StartupCount.Text = "Lendo a lista...";
        List<StartupItem> items;
        try
        {
            items = await Task.Run(StartupManager.List);
        }
        catch (Exception ex)
        {
            StartupCount.Text = "Não foi possível ler a lista: " + ex.Message;
            return;
        }

        _startup.Clear();
        int i = 0;
        foreach (var it in items) _startup.Add(new StartupRow(it, i++));
        StartupEmpty.Visibility = _startup.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateStartupSummary();

        // Ícones depois, sem travar a tela.
        foreach (var row in _startup.ToList())
        {
            row.Icon = FileIcon.Get(row.Item.ExePath);
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
    }

    private void UpdateStartupSummary()
    {
        int on = _startup.Count(s => s.IsOn);
        StartupCount.Text = _startup.Count == 0 ? "Nenhum programa na inicialização" : $"{on} de {_startup.Count} programas abrem com o Windows";
        StartupSummary.Text = $"{on} programas abrem sozinhos";
    }

    private void OnStartupToggle(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: StartupRow row } cb) return;
        bool wantOn = cb.IsChecked == true;
        try
        {
            StartupManager.SetEnabled(row.Item, wantOn);
            ShowToast(wantOn
                ? $"✓ \"{row.Name}\" vai abrir com o Windows."
                : $"✓ \"{row.Name}\" não vai mais abrir sozinho. Vale a partir do próximo login.");
        }
        catch (Exception ex)
        {
            ShowToast($"⚠ Não deu para mudar \"{row.Name}\": {ex.Message}");
        }
        row.Refresh();
        UpdateStartupSummary();
    }

    private void OnStartupRestore(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: StartupRow row }) return;
        try
        {
            StartupManager.Restore(row.Item);
            ShowToast($"↺ \"{row.Name}\" voltou ao estado original.");
        }
        catch (Exception ex)
        {
            ShowToast($"⚠ Não deu para restaurar \"{row.Name}\": {ex.Message}");
        }
        row.Refresh();
        UpdateStartupSummary();
    }

    private void OnRestoreAllStartup(object sender, RoutedEventArgs e)
    {
        var changed = _startup.Where(s => StartupManager.HasBackup(s.Item)).ToList();
        if (changed.Count == 0)
        {
            ShowToast("Nada para restaurar: este app ainda não mudou nenhum programa da inicialização.");
            return;
        }
        int failed = 0;
        foreach (var row in changed)
        {
            try { StartupManager.Restore(row.Item); }
            catch { failed++; }
            row.Refresh();
        }
        UpdateStartupSummary();
        ShowToast(failed == 0
            ? $"↺ {changed.Count} programa(s) voltaram ao estado original."
            : $"↺ {changed.Count - failed} restaurado(s); {failed} falharam.");
    }

    // ================= Aviso flutuante =================

    private void ShowToast(string text)
    {
        ToastText.Text = text;
        Toast.Visibility = Visibility.Visible;
        Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));
        Toast.RenderTransform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } });
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
        fade.Completed += (_, _) =>
        {
            if (!_toastTimer.IsEnabled) Toast.Visibility = Visibility.Collapsed;
        };
        Toast.BeginAnimation(OpacityProperty, fade);
        Toast.RenderTransform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(20, TimeSpan.FromMilliseconds(300)) { EasingFunction = Ease });
    }
}
