using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BottleneckHunter.Controls;
using BottleneckHunter.Core;
using Microsoft.Win32;

namespace BottleneckHunter.Views.Sensors;

/// <summary>
/// Lista ao vivo de todos os sensores, agrupados por peça e por tipo, com busca, filtros,
/// mínimo/máximo, sensores fixados e um gráfico do sensor em destaque.
/// </summary>
public partial class SensorsView : UserControl
{
    private readonly Dictionary<string, SensorRow> _rows = new();
    private readonly ObservableCollection<HardwareGroup> _groups = new();
    private readonly ObservableCollection<SensorRow> _pinned = new();
    private readonly SensorsSettings _settings = SensorsSettings.Load();

    private int _signature;
    private DateTime _lastSeen = DateTime.MinValue;
    private DateTime _lastRender;
    private string _typeFilter = "";
    private string[] _searchTokens = Array.Empty<string>();
    private bool _alertsOnly, _paused, _mergeHardwareMinMax = true;
    private int _rangeSeconds = 60;
    private int _shown;
    private SensorRow? _selected;

    public SensorsView()
    {
        InitializeComponent();
        GroupsList.ItemsSource = _groups;
        PinnedList.ItemsSource = _pinned;
        GuidePanel.ItemsSource = SensorText.Guide;

        Loaded += (_, _) =>
        {
            App.Sensors.Updated += OnUpdated;
            CatchUp();
            if (_rows.Count == 0) StartWaitingAnimation();
        };
        Unloaded += (_, _) => App.Sensors.Updated -= OnUpdated;
        PreviewKeyDown += OnKeys;
    }

    // ===================== Dados =====================

    private void OnUpdated(HwSnapshot s) => Dispatcher.BeginInvoke(() =>
    {
        Ingest(s);
        if (!_paused) Render();
    });

    /// <summary>
    /// Ao voltar para a página, recupera o que aconteceu enquanto ela estava fechada
    /// (o SensorHub guarda 15 min), para o gráfico e o mín./máx. não ficarem com buracos.
    /// </summary>
    private void CatchUp()
    {
        var missed = App.Sensors.HistorySince(TimeSpan.FromMinutes(15)).Where(h => h.Time > _lastSeen).ToList();
        foreach (var snap in missed) Ingest(snap);
        if (missed.Count == 0 && App.Sensors.Latest is { } latest && latest.Time > _lastSeen) Ingest(latest);
        if (!_paused || ContentRoot.Visibility != Visibility.Visible) Render();
    }

    private void Ingest(HwSnapshot snap)
    {
        var all = snap.All;
        if (all.Count == 0 || snap.Time <= _lastSeen) return;
        _lastSeen = snap.Time;

        var keys = BuildKeys(all);
        var hash = new HashCode();
        foreach (var k in keys) hash.Add(k);
        int sig = hash.ToHashCode() ^ keys.Length;
        if (sig != _signature || _rows.Count != keys.Length)
        {
            _signature = sig;
            Rebuild(all, keys);
        }

        for (int i = 0; i < all.Count; i++)
            if (_rows.TryGetValue(keys[i], out var row))
                row.Apply(all[i], _mergeHardwareMinMax);
    }

    /// <summary>Uma chave estável por sensor. Se dois sensores tiverem o mesmo nome, o segundo ganha "#1".</summary>
    private static string[] BuildKeys(IReadOnlyList<SensorReading> all)
    {
        var seen = new Dictionary<string, int>();
        var keys = new string[all.Count];
        for (int i = 0; i < all.Count; i++)
        {
            var r = all[i];
            var baseKey = $"{r.Hardware}|{r.Type}|{r.Unit}|{r.Name}";
            seen.TryGetValue(baseKey, out int n);
            seen[baseKey] = n + 1;
            keys[i] = n == 0 ? baseKey : $"{baseKey}#{n}";
        }
        return keys;
    }

    /// <summary>Monta os cartões quando a lista de sensores muda (na abertura ou se uma peça aparece/some).</summary>
    private void Rebuild(IReadOnlyList<SensorReading> all, string[] keys)
    {
        var byHardware = new Dictionary<string, List<(string Key, SensorReading R)>>();
        var order = new List<string>();
        for (int i = 0; i < all.Count; i++)
        {
            if (!byHardware.TryGetValue(all[i].Hardware, out var list))
            {
                byHardware[all[i].Hardware] = list = new();
                order.Add(all[i].Hardware);
            }
            list.Add((keys[i], all[i]));
        }

        var newRows = new Dictionary<string, SensorRow>();
        var groups = new List<HardwareGroup>();
        foreach (var hw in order)
        {
            var items = byHardware[hw];
            var kind = SensorText.InferKind(hw, items.Select(x => x.R).ToList());
            var rows = new List<SensorRow>();
            foreach (var (key, r) in items)
            {
                if (newRows.ContainsKey(key)) continue;
                var row = _rows.TryGetValue(key, out var old) ? old : new SensorRow(key, r, kind);
                newRows[key] = row;
                rows.Add(row);
            }

            var sections = rows
                .GroupBy(r => r.Type)
                .OrderBy(g => SensorText.TypeOrder(g.Key))
                .Select(g => new SensorSection(g.Key, g));
            groups.Add(new HardwareGroup(hw, kind, sections, !_settings.Collapsed!.Contains(hw)));
        }

        _rows.Clear();
        foreach (var (k, v) in newRows) _rows[k] = v;

        _groups.Clear();
        foreach (var g in groups.OrderBy(g => (int)g.Kind)) _groups.Add(g);

        RestorePins();
        RestoreSelection();
        BuildTypeChips();
        ApplyFilter();
        UpdateExpandButton();
    }

    private void RestorePins()
    {
        // Primeira vez: já deixa fixadas as temperaturas e usos que mais importam para gargalo.
        if (!_settings.Initialized && _rows.Values.Any(r => r.Kind is HwKind.Cpu or HwKind.Gpu))
        {
            var defaults = new (HwKind Kind, string Type, string[] Names)[]
            {
                (HwKind.Cpu, "Temperatura", new[] { "CPU Package", "Core (Tctl/Tdie)", "Core Max", "Core (Tctl)" }),
                (HwKind.Cpu, "Uso", new[] { "CPU Total" }),
                (HwKind.Gpu, "Temperatura", new[] { "GPU Core" }),
                (HwKind.Gpu, "Uso", new[] { "GPU Core", "D3D 3D" }),
            };
            foreach (var (kind, type, names) in defaults)
            {
                var match = names
                    .Select(n => _rows.Values.FirstOrDefault(r => r.Kind == kind && r.Type == type && r.RawName == n))
                    .FirstOrDefault(r => r != null);
                if (match != null && !_settings.Pinned!.Contains(match.Key)) _settings.Pinned.Add(match.Key);
            }
            _settings.Initialized = true;
            _settings.Save();
        }

        _pinned.Clear();
        foreach (var row in _rows.Values) row.IsPinned = false;
        foreach (var key in _settings.Pinned!)
        {
            if (!_rows.TryGetValue(key, out var row)) continue;
            row.IsPinned = true;
            _pinned.Add(row);
        }
    }

    private void RestoreSelection()
    {
        if (_selected != null && _rows.ContainsValue(_selected)) return;
        var row = (_settings.Selected != null && _rows.TryGetValue(_settings.Selected, out var saved) ? saved : null)
                  ?? _pinned.FirstOrDefault()
                  ?? _rows.Values.FirstOrDefault(r => r.Type == "Temperatura")
                  ?? _rows.Values.FirstOrDefault();
        if (row != null) Select(row, remember: false);
    }

    // ===================== Tela =====================

    private void Render()
    {
        bool hasRows = _rows.Count > 0;
        if (hasRows && ContentRoot.Visibility != Visibility.Visible)
        {
            EmptyCard.Visibility = Visibility.Collapsed;
            ContentRoot.Visibility = Visibility.Visible;
            ContentRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)));
        }
        if (!hasRows)
        {
            if (App.Sensors.Error is { } err)
            {
                EmptyTitle.Text = "Não foi possível ler os sensores";
                EmptyHint.Text = $"Detalhe: {err}\nConfira se o app foi aberto como administrador e tente de novo.";
            }
            return;
        }

        foreach (var row in _rows.Values) row.Refresh();
        foreach (var g in _groups) g.RefreshSummary();
        if (_alertsOnly) ApplyFilter();

        var cpu = _groups.FirstOrDefault(g => g.Kind == HwKind.Cpu);
        CpuTempWarning.Visibility = App.Sensors.IsReady && cpu != null && !cpu.Rows.Any(r => r.Type == "Temperatura" && r.Value != null)
            ? Visibility.Visible : Visibility.Collapsed;

        PinnedHint.Visibility = _pinned.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _lastRender = DateTime.Now;
        UpdateDetail();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        int hardware = _groups.Count(g => g.HasVisible);
        string time = _lastRender == default ? "" : $"  ·  atualizado às {_lastRender:HH:mm:ss}";
        StatusText.Text = _paused
            ? $"⏸ Pausado: os números estão congelados. A leitura continua em segundo plano.{time}"
            : $"Mostrando {_shown} de {_rows.Count} sensores em {hardware} {(hardware == 1 ? "peça" : "peças")}{time}";
        NoResults.Visibility = _shown == 0 && _rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoResultsText.Text = _alertsOnly && _searchTokens.Length == 0 && _typeFilter.Length == 0
            ? "✅ Nenhum alerta agora: todos os sensores com faixa de risco estão dentro do normal."
            : "Nenhum sensor combina com a busca e os filtros. Tente outra palavra ou escolha \"Todos\".";
    }

    /// <summary>Atualiza o cartão em destaque: valor, média e gráfico do sensor selecionado.</summary>
    private void UpdateDetail()
    {
        if (_selected is not { } row || Chart == null)
        {
            DetailCard.Visibility = Visibility.Collapsed;
            return;
        }
        DetailCard.Visibility = Visibility.Visible;

        var vals = row.Last(_rangeSeconds);
        var finite = vals.Where(float.IsFinite).ToList();
        double max = finite.Count > 0 ? finite.Max() : 0;
        var (factor, label) = SensorText.ChartScale(row.Unit, max);

        Chart.Capacity = _rangeSeconds;
        Chart.YUnit = label;
        Chart.MaxY = row.Unit == "%" ? 100 : SensorText.NiceCeiling(Math.Max(max * factor * 1.15, row.Rule?.Warn ?? 0));
        Chart.SetSeries(new ChartSeries(row.Name, row.BarKey,
            vals.Select(v => float.IsFinite(v) ? v * factor : (double?)null).ToList()));

        AvgText.Text = finite.Count > 0 ? SensorText.Format(finite.Average(), row.Unit) : "—";
        StatusLine.Visibility = row.StatusText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RangeLine.Visibility = row.RangeHint.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        DetailPin.Content = row.IsPinned ? "★  Fixado (clique para soltar)" : "☆  Fixar no topo";
    }

    private void Select(SensorRow row, bool remember = true)
    {
        if (_selected != null) _selected.IsSelected = false;
        _selected = row;
        row.IsSelected = true;
        DetailCard.DataContext = row;

        if (remember)
        {
            _settings.Selected = row.Key;
            _settings.Save();
        }

        // O destaque entra deslizando da direita.
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Detail.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(300)));
        Detail.RenderTransform.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(350)) { EasingFunction = ease });
        UpdateDetail();
    }

    private void TogglePin(SensorRow row)
    {
        if (row.IsPinned)
        {
            row.IsPinned = false;
            _pinned.Remove(row);
            _settings.Pinned!.Remove(row.Key);
        }
        else
        {
            if (_pinned.Count >= SensorsSettings.MaxPinned)
            {
                ShowNote($"Dá para fixar até {SensorsSettings.MaxPinned} sensores. Solte um para fixar outro.");
                return;
            }
            row.IsPinned = true;
            row.Refresh();
            _pinned.Add(row);
            _settings.Pinned!.Add(row.Key);
        }
        _settings.Save();
        PinnedHint.Visibility = _pinned.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateDetail();
    }

    // ===================== Filtros =====================

    private void BuildTypeChips()
    {
        TypeChips.Children.Clear();
        var types = _rows.Values.GroupBy(r => r.Type).OrderBy(g => SensorText.TypeOrder(g.Key)).ToList();
        if (_typeFilter.Length > 0 && types.All(t => t.Key != _typeFilter)) _typeFilter = "";

        AddChip("", $"Todos  {_rows.Count}", "Mostra todos os sensores.");
        foreach (var t in types)
            AddChip(t.Key, $"{SensorText.TypeTitle(t.Key)}  {t.Count()}", SensorText.Explain(t.Key));
    }

    private void AddChip(string type, string text, string tip)
    {
        var chip = new RadioButton
        {
            Content = text,
            Tag = type,
            GroupName = "sensorType",
            Style = (Style)FindResource("Segment"),
            Margin = new Thickness(0, 0, 6, 6),
            ToolTip = tip,
            IsChecked = type == _typeFilter,
        };
        chip.Checked += (_, _) =>
        {
            _typeFilter = type;
            ApplyFilter();
            UpdateStatus();
        };
        TypeChips.Children.Add(chip);
    }

    private void ApplyFilter()
    {
        bool searching = _searchTokens.Length > 0;
        int shown = 0;
        foreach (var g in _groups)
        {
            bool groupVisible = false;
            foreach (var s in g.Sections)
            {
                bool any = false;
                foreach (var r in s.Rows)
                {
                    bool vis = Matches(r);
                    r.IsVisible = vis;
                    if (vis) { any = true; shown++; }
                }
                s.HasVisible = any;
                groupVisible |= any;
            }
            g.HasVisible = groupVisible;
            g.ForceOpen = searching || _alertsOnly;
        }
        _shown = shown;
    }

    private bool Matches(SensorRow r)
    {
        if (_typeFilter.Length > 0 && r.Type != _typeFilter) return false;
        if (_alertsOnly && r.Severity == Severity.Normal) return false;
        var cmp = CultureInfo.InvariantCulture.CompareInfo;
        foreach (var token in _searchTokens)
            if (cmp.IndexOf(r.SearchText, token, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) < 0)
                return false;
        return true;
    }

    // ===================== Eventos =====================

    private void OnRowClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SensorRow row) Select(row);
    }

    private void OnPinClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SensorRow row) return;
        Select(row);
        // Leva o destaque para a vista, caso esteja fora da tela.
        DetailCard.BringIntoView();
    }

    private void OnStarClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SensorRow row) TogglePin(row);
        e.Handled = true;
    }

    private void OnDetailPin(object sender, RoutedEventArgs e)
    {
        if (_selected != null) TogglePin(_selected);
    }

    private void OnGroupHeaderClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not HardwareGroup g) return;
        if (g.ForceOpen) return; // durante a busca, os cartões ficam abertos
        g.IsExpanded = !g.IsExpanded;
        SaveCollapsed();
        UpdateExpandButton();
    }

    private void OnExpandAll(object sender, RoutedEventArgs e)
    {
        bool expand = _groups.All(g => !g.IsExpanded);
        foreach (var g in _groups) g.IsExpanded = expand;
        SaveCollapsed();
        UpdateExpandButton();
    }

    private void UpdateExpandButton()
    {
        bool allClosed = _groups.All(g => !g.IsExpanded);
        ExpandText.Text = allClosed ? "Expandir tudo" : "Recolher tudo";
        ExpandGlyph.Text = allClosed ? "" : "";
    }

    private void SaveCollapsed()
    {
        _settings.Collapsed = _groups.Where(g => !g.IsExpanded).Select(g => g.Name).Take(64).ToList();
        _settings.Save();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _searchTokens = SearchBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        ClearSearch.Visibility = SearchBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter();
        UpdateStatus();
    }

    private void OnClearSearch(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    private void OnAlertsToggle(object sender, RoutedEventArgs e)
    {
        _alertsOnly = AlertsOnly.IsChecked == true;
        ApplyFilter();
        UpdateStatus();
    }

    private void OnRange(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && int.TryParse(tag, out int seconds))
            _rangeSeconds = seconds;
        if (ChartTitle != null)
            ChartTitle.Text = _rangeSeconds switch { 60 => "ÚLTIMO MINUTO", 300 => "ÚLTIMOS 5 MINUTOS", _ => "ÚLTIMOS 15 MINUTOS" };
        if (_selected != null) UpdateDetail();
    }

    private void OnPause(object sender, RoutedEventArgs e)
    {
        _paused = !_paused;
        PauseText.Text = _paused ? "Continuar" : "Pausar";
        PauseGlyph.Text = _paused ? "" : "";
        if (!_paused) Render();
        else UpdateStatus();
    }

    private void OnResetMinMax(object sender, RoutedEventArgs e)
    {
        _mergeHardwareMinMax = false;
        foreach (var row in _rows.Values) row.ResetMinMax();
        Render();
        ShowNote($"Mínimos e máximos zerados às {DateTime.Now:HH:mm:ss}.");
    }

    private void OnToggleGuide(object sender, RoutedEventArgs e)
    {
        bool show = GuidePanel.Visibility != Visibility.Visible;
        GuideButton.Content = show ? "Esconder guia ▴" : "Entenda as leituras ▾";
        if (show)
        {
            GuidePanel.Visibility = Visibility.Visible;
            int i = 0;
            foreach (var item in GuidePanel.Items)
            {
                if (GuidePanel.ItemContainerGenerator.ContainerFromItem(item) is not UIElement el) continue;
                el.Opacity = 0;
                el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
                {
                    BeginTime = TimeSpan.FromMilliseconds(60 * i++),
                });
            }
            GuidePanel.UpdateLayout();
        }
        else GuidePanel.Visibility = Visibility.Collapsed;
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Salvar sensores",
            FileName = $"sensores-{DateTime.Now:yyyy-MM-dd-HHmm}.csv",
            Filter = "Planilha CSV (*.csv)|*.csv",
            AddExtension = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            var c = CultureInfo.CurrentCulture;
            string sep = c.TextInfo.ListSeparator is { Length: 1 } ls ? ls : ";";
            string Cell(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
            string Num(float? v) => v is { } x ? x.ToString("0.###", c) : "";

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(sep, "Peça", "Tipo de peça", "Tipo", "Sensor", "Nome original", "Atual", "Mínimo", "Máximo", "Unidade"));
            foreach (var g in _groups)
                foreach (var r in g.Rows)
                    sb.AppendLine(string.Join(sep, Cell(r.Hardware), Cell(r.KindText), Cell(r.TypeTitle), Cell(r.Name), Cell(r.RawName),
                        Num(r.Value), Num(r.Min), Num(r.Max), Cell(r.Unit)));

            File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
            ShowNote($"Planilha salva: {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex)
        {
            ShowNote("Não foi possível salvar: " + ex.Message);
        }
    }

    private void OnKeys(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SearchBox.IsKeyboardFocused && SearchBox.Text.Length > 0)
        {
            SearchBox.Clear();
            e.Handled = true;
        }
    }

    // ===================== Animações =====================

    /// <summary>Os cartões das peças entram um depois do outro, subindo.</summary>
    private void OnGroupLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not HardwareGroup g) return;
        int index = Math.Max(0, _groups.IndexOf(g));
        var delay = TimeSpan.FromMilliseconds(Math.Min(index, 8) * 70);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var move = new TranslateTransform(0, 16);
        fe.RenderTransform = move;
        fe.Opacity = 0;
        fe.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)) { BeginTime = delay });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(400)) { BeginTime = delay, EasingFunction = ease });
    }

    /// <summary>Um cartão fixado "brota" com um pequeno quique.</summary>
    private void OnPinLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        var scale = new ScaleTransform(0.85, 0.85);
        fe.RenderTransformOrigin = new Point(0.5, 0.5);
        fe.RenderTransform = scale;
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
        var grow = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        fe.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
    }

    /// <summary>Recado rápido no canto (aparece e some sozinho).</summary>
    private void ShowNote(string text)
    {
        StatusNote.Text = text;
        var anim = new DoubleAnimationUsingKeyFrames();
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.5))));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4))));
        StatusNote.BeginAnimation(OpacityProperty, anim);
    }

    /// <summary>Três bolinhas pulsando enquanto os sensores não respondem.</summary>
    private void StartWaitingAnimation()
    {
        var dots = new[] { Dot1, Dot2, Dot3 };
        for (int i = 0; i < dots.Length; i++)
        {
            dots[i].BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.2, TimeSpan.FromMilliseconds(500))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromMilliseconds(160 * i),
            });
        }
    }
}
