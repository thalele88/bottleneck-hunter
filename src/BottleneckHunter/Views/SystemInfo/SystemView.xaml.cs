using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BottleneckHunter.Core.SystemInfo;
using Microsoft.Win32;

namespace BottleneckHunter.Views.SystemInfo;

public partial class SystemView : UserControl
{
    public const string DiskIntro =
        "Cada disco mostra o tipo (SSD NVMe é o mais rápido, depois SSD SATA e, por último, HD), quanto espaço resta e a saúde. " +
        "Saúde em amarelo ou vermelho é motivo para fazer backup.";

    private SystemReport? _report;
    private bool _loading;
    private string? _lastSaved;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    public SystemView()
    {
        InitializeComponent();
        _toastTimer.Tick += (_, _) => HideToast();
        Body.SizeChanged += (_, e) => Relayout(e.NewSize.Width);
        Loaded += (_, _) =>
        {
            if (_report == null && !_loading) _ = LoadAsync();
        };
        Unloaded += (_, _) => _toastTimer.Stop();
    }

    // ───────────────────────── Leitura ─────────────────────────

    private async Task LoadAsync()
    {
        _loading = true;
        SetBusy(true);
        var progress = new Progress<string>(text => LoadingText.Text = text);
        var sensors = App.Sensors.Latest.All;
        try
        {
            var report = await Task.Run(() => SystemInfoCollector.Collect(progress, sensors));
            _report = report;
            Render(report);
        }
        catch (Exception ex)
        {
            LoadingText.Text = "Não foi possível ler as informações: " + ex.Message;
        }
        finally
        {
            _loading = false;
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        CopyBtn.IsEnabled = HtmlBtn.IsEnabled = JsonBtn.IsEnabled = !busy && _report != null;
        RefreshBtn.IsEnabled = !busy;

        var spin = (RotateTransform)Spinner.RenderTransform;
        if (busy)
        {
            LoadingCard.Visibility = Visibility.Visible;
            LoadingCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
            spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
            // Numa releitura, os dados antigos ficam esmaecidos até os novos chegarem.
            if (Body.Visibility == Visibility.Visible)
                Body.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, TimeSpan.FromMilliseconds(250)));
        }
        else
        {
            spin.BeginAnimation(RotateTransform.AngleProperty, null);
            LoadingCard.Visibility = _report == null ? Visibility.Visible : Visibility.Collapsed;
            Body.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
        }
    }

    // ───────────────────────── Montagem da tela ─────────────────────────

    private void Render(SystemReport r)
    {
        PcName.Text = r.ComputerName;
        PcSub.Text = $"{r.Os.Name} {r.Os.DisplayVersion} · lido às {r.CreatedAt:HH:mm}".Replace("  ", " ");
        Chips.ItemsSource = BuildChips(r);

        RenderFindings(r);

        LeftCol.Children.Clear();
        RightCol.Children.Clear();
        var sections = ReportSections.All(r);
        foreach (var s in sections)
        {
            var vm = SectionVm.From(s, s.Key == "ram" ? BuildSlots(r.Memory) : null);
            bool left = s.Key is "cpu" or "ram" or "os";
            (left ? LeftCol : RightCol).Children.Add(Host(vm, "SectionTemplate"));
        }

        DiskList.Children.Clear();
        foreach (var d in r.Disks) DiskList.Children.Add(Host(DiskVm.From(d), "DiskTemplate"));
        if (r.Disks.Count == 0)
            DiskList.Children.Add(new TextBlock { Text = "Nenhum disco encontrado.", Style = (Style)FindResource("Dim") });

        ErrorsText.Visibility = r.Errors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ErrorsText.Text = "Algumas partes não puderam ser lidas: " + string.Join(" · ", r.Errors);

        Body.Visibility = Visibility.Visible;
        Body.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
        Relayout(Body.ActualWidth);
        AnimateIn();
    }

    private void RenderFindings(SystemReport r)
    {
        FindingsList.Children.Clear();
        foreach (var f in r.Findings) FindingsList.Children.Add(Host(FindingVm.From(f), "FindingTemplate"));

        int bad = r.Findings.Count(f => f.Level == Level.Bad);
        int warn = r.Findings.Count(f => f.Level == Level.Warn);
        int good = r.Findings.Count(f => f.Level == Level.Good);
        FindingsHeadline.Text = bad > 0 ? $"{Plural(bad, "problema importante", "problemas importantes")} para resolver"
            : warn > 0 ? $"{Plural(warn, "ponto", "pontos")} para melhorar o desempenho"
            : "Tudo certo por aqui! Nenhum problema encontrado.";

        Counters.Children.Clear();
        AddCounter(bad, "Bad", "crítico", "críticos");
        AddCounter(warn, "Warn", "atenção", "atenção");
        AddCounter(good, "Good", "ok", "ok");

        void AddCounter(int n, string brush, string one, string many)
        {
            if (n == 0) return;
            var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(Border.BackgroundProperty, brush);
            var text = new TextBlock { Text = $"{n} {(n == 1 ? one : many)}", VerticalAlignment = VerticalAlignment.Center };
            text.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            var pill = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
            pill.SetResourceReference(Border.BackgroundProperty, "Track");
            pill.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, text } };
            Counters.Children.Add(pill);
        }
    }

    private static string Plural(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    private ContentPresenter Host(object vm, string template) => new()
    {
        Content = vm,
        ContentTemplate = (DataTemplate)FindResource(template),
        RenderTransform = new TranslateTransform(),
    };

    private static List<ChipVm> BuildChips(SystemReport r)
    {
        var chips = new List<ChipVm>();
        if (r.Cpu.Name.Length > 0) chips.Add(new("CPU", ShortName(r.Cpu.Name)));
        if (r.Gpus.FirstOrDefault() is { } g) chips.Add(new("GPU", ShortName(g.Name)));
        if (r.Memory.TotalGb > 0) chips.Add(new("RAM", ReportSections.Ram(r).Headline));
        if ((r.Disks.FirstOrDefault(d => d.HoldsWindows) ?? r.Disks.FirstOrDefault()) is { } d)
            chips.Add(new(d.Media is "SSD" or "HD" ? d.Media : "DISCO", $"{Fmt.Gb(d.SizeGb)} {d.Bus}".Trim()));
        if (r.Displays.FirstOrDefault() is { } m) chips.Add(new("TELA", $"{m.Width}×{m.Height} · {m.RefreshHz} Hz"));
        return chips;
    }

    /// <summary>"AMD Ryzen 5 5600X 6-Core Processor" → "AMD Ryzen 5 5600X"; "NVIDIA GeForce RTX 3060" → "GeForce RTX 3060".</summary>
    private static string ShortName(string name)
    {
        var s = Regex.Replace(name, @"\((R|TM|C)\)|®|™", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\b\d+-Core Processor\b|\bProcessor\b|\bCPU\b|@.*$|with Radeon.*$|\bNVIDIA\b", "", RegexOptions.IgnoreCase);
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    /// <summary>Um desenho por slot: ocupados primeiro, na ordem dos nomes, depois os vazios.</summary>
    private static List<RamSlotVm> BuildSlots(MemoryInfo m)
    {
        var list = new List<RamSlotVm>();
        int i = 0;
        foreach (var mod in m.Modules.OrderBy(x => x.Slot, StringComparer.OrdinalIgnoreCase))
        {
            var detail = $"{Fmt.Or(mod.Slot, "Slot")}: {Fmt.Ram(mod.SizeGb)} {mod.Type} a {mod.ConfiguredSpeedMts} MT/s"
                         + (mod.Maker.Length > 0 || mod.PartNumber.Length > 0 ? $"\n{mod.Maker} {mod.PartNumber}".TrimEnd() : "")
                         + (mod.FormFactor.Length > 0 ? $"\n{mod.FormFactor}" : "");
            list.Add(new(i, true, ShortSlot(mod.Slot, i), Fmt.Ram(mod.SizeGb), detail));
            i++;
        }
        for (; i < m.Slots; i++)
            list.Add(new(i, false, "livre", "—", "Slot vazio: dá para colocar mais memória aqui."));
        return list;
    }

    private static string ShortSlot(string slot, int index)
    {
        if (slot.Length == 0) return $"Slot {index + 1}";
        // "Controller0-ChannelA-DIMM0" → "A-DIMM0"; "DIMM_A2" fica igual.
        var s = Regex.Replace(slot, @"Controller\d-|Channel", "", RegexOptions.IgnoreCase);
        return s.Length > 10 ? s[..10] : s;
    }

    private void Relayout(double width)
    {
        bool narrow = width > 0 && width < 900;
        Grid.SetColumn(RightCol, narrow ? 0 : 1);
        Grid.SetRow(RightCol, narrow ? 1 : 0);
        Columns.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
    }

    /// <summary>Os cartões entram um após o outro, subindo e aparecendo.</summary>
    private void AnimateIn()
    {
        var items = new List<UIElement> { FindingsCard };
        items.AddRange(FindingsList.Children.OfType<UIElement>().Take(8));
        int count = Math.Max(LeftCol.Children.Count, RightCol.Children.Count);
        for (int i = 0; i < count; i++)
        {
            if (i < LeftCol.Children.Count) items.Add(LeftCol.Children[i]);
            if (i < RightCol.Children.Count) items.Add(RightCol.Children[i]);
        }
        items.AddRange(DiskList.Children.OfType<UIElement>());

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        for (int i = 0; i < items.Count; i++)
        {
            var el = items[i];
            var delay = TimeSpan.FromMilliseconds(Math.Min(i, 14) * 55);
            el.Opacity = 0;
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { BeginTime = delay });
            if (el.RenderTransform is not TranslateTransform tt || tt.IsFrozen)
                el.RenderTransform = tt = new TranslateTransform();
            bool sideways = FindingsList.Children.Contains(el);
            tt.BeginAnimation(sideways ? TranslateTransform.XProperty : TranslateTransform.YProperty,
                new DoubleAnimation(sideways ? -14 : 18, 0, TimeSpan.FromMilliseconds(450)) { BeginTime = delay, EasingFunction = ease });
        }
    }

    // ───────────────────────── Ações ─────────────────────────

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (!_loading) _ = LoadAsync();
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (_report == null) return;
        var text = ReportSections.Summary(_report);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                ShowToast("Resumo copiado. Cole com Ctrl+V no fórum, grupo ou Discord.", ok: true, folder: false);
                return;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // Outro programa está usando a área de transferência; tenta de novo.
                Thread.Sleep(60);
            }
        }
        ShowToast("A área de transferência está ocupada por outro programa. Tente de novo.", ok: false, folder: false);
    }

    private void OnExportHtml(object sender, RoutedEventArgs e) => Export(json: false);
    private void OnExportJson(object sender, RoutedEventArgs e) => Export(json: true);

    private void Export(bool json)
    {
        if (_report == null) return;
        var safeName = Regex.Replace(_report.ComputerName, @"[^A-Za-z0-9_-]", "");
        var dlg = new SaveFileDialog
        {
            Title = json ? "Salvar dados do PC (JSON)" : "Salvar relatório do PC",
            Filter = json ? "Dados JSON (*.json)|*.json" : "Página da web (*.html)|*.html",
            DefaultExt = json ? ".json" : ".html",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"Relatorio-PC-{(safeName.Length > 0 ? safeName : "meu-pc")}-{DateTime.Now:yyyy-MM-dd}",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            ReportExporter.Save(_report, dlg.FileName, json);
            _lastSaved = dlg.FileName;
            ShowToast($"Salvo: {Path.GetFileName(dlg.FileName)}", ok: true, folder: true);
        }
        catch (Exception ex)
        {
            ShowToast("Não foi possível salvar: " + ex.Message, ok: false, folder: false);
        }
    }

    private void OnShowInFolder(object sender, RoutedEventArgs e)
    {
        if (_lastSaved == null || !File.Exists(_lastSaved)) return;
        try
        {
            // Caminho absoluto do Explorer, nunca resolvido pelo PATH.
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            Process.Start(new ProcessStartInfo(explorer, $"/select,\"{_lastSaved}\"") { UseShellExecute = false });
        }
        catch { }
        HideToast();
    }

    // ───────────────────────── Aviso flutuante ─────────────────────────

    private void ShowToast(string text, bool ok, bool folder)
    {
        ToastText.Text = text;
        ToastGlyph.Text = ok ? "✓" : "!";
        ToastIcon.SetResourceReference(Border.BackgroundProperty, ok ? "Good" : "Bad");
        ToastAction.Visibility = folder ? Visibility.Visible : Visibility.Collapsed;

        Toast.Visibility = Visibility.Visible;
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
        Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        ((TranslateTransform)Toast.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });

        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(folder ? 8 : 4);
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
        fade.Completed += (_, _) =>
        {
            if (!_toastTimer.IsEnabled) Toast.Visibility = Visibility.Collapsed;
        };
        Toast.BeginAnimation(OpacityProperty, fade);
        ((TranslateTransform)Toast.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(20, TimeSpan.FromMilliseconds(250)));
    }
}
