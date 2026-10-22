using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using BottleneckHunter.Core;

namespace BottleneckHunter.Views.Learn;

/// <summary>
/// Página Aprenda: assuntos com ilustrações animadas, laboratório do gargalo, quiz e glossário.
/// As animações só rodam enquanto a parte correspondente está visível na tela, para não gastar CPU/GPU à toa.
/// </summary>
public partial class LearnView : UserControl
{
    private const string StoreName = "learn";
    private const double LabFpsCap = 60;

    private readonly Dictionary<string, (FrameworkElement Art, Storyboard Anim)> _art = new();
    private readonly LearnState _state;
    private readonly bool _ready;
    private string _topicKey = LearnContent.Topics[0].Key;
    private Storyboard? _runningArt;
    private bool _artPaused;

    // Laboratório
    private readonly List<FlyingFrame> _frames = new();
    private bool _labRunning;
    private TimeSpan _lastRender;
    private double _labFps, _shownFps, _spawnAcc;

    // Quiz
    private int _quizIndex, _quizScore;

    // Glossário
    private string _glossCategory = "Todos";

    private sealed class FlyingFrame
    {
        public required Rectangle Shape;
        public double X;
        public bool Painted;
    }

    public LearnView()
    {
        InitializeComponent();
        _state = LoadState();

        foreach (var t in LearnContent.Topics)
            _art[t.Key] = ((FrameworkElement)FindName("Art_" + t.Key), (Storyboard)FindResource("Anim_" + t.Key));

        BuildFilmStrip();
        BuildTopicBar();
        BuildGlossaryFilters();
        _ready = true;

        ShowTopic(_state.LastTopic ?? _topicKey, animate: false);
        UpdateLab();
        StartQuiz();
        RefreshGlossary();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        StartArt();
        UpdateActivity();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopArt();
        SetLabRunning(false);
        JsonStore.Save(StoreName, _state);
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateActivity();

    /// <summary>Liga ou pausa as animações conforme elas aparecem ou somem da área visível.</summary>
    private void UpdateActivity()
    {
        if (!IsLoaded) return;

        var artVisible = IsInView(ArtHost);
        if (_runningArt != null && artVisible == _artPaused)
        {
            if (artVisible) _runningArt.Resume(this);
            else _runningArt.Pause(this);
            _artPaused = !artVisible;
        }

        SetLabRunning(IsInView(LabCard));
    }

    private bool IsInView(FrameworkElement el)
    {
        if (!el.IsVisible) return false;
        try
        {
            var r = el.TransformToAncestor(Scroller).TransformBounds(new Rect(el.RenderSize));
            return r.Bottom > 0 && r.Top < Scroller.ViewportHeight;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static LearnState LoadState()
    {
        var s = JsonStore.Load<LearnState>(StoreName);
        // Arquivo lido do disco: só aceita valores conhecidos.
        if (s.LastTopic is not null && LearnContent.Topics.All(t => t.Key != s.LastTopic)) s.LastTopic = null;
        s.BestQuiz = Math.Clamp(s.BestQuiz, 0, LearnContent.Quiz.Length);
        return s;
    }

    private void OnNavigate(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key })
            (Window.GetWindow(this) as MainWindow)?.Navigate(key);
    }

    private static readonly CubicEase EaseOut = new() { EasingMode = EasingMode.EaseOut };

    private static void Animate(IAnimatable target, DependencyProperty prop, double from, double to, int ms, IEasingFunction? ease = null, int delayMs = 0) =>
        target.BeginAnimation(prop, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = ease ?? EaseOut,
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
        });

    private static TextBlock Text(string text, string brushKey, double size = 13.5, FontWeight? weight = null)
    {
        var tb = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, FontWeight = weight ?? FontWeights.Normal };
        tb.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return tb;
    }

    // ======================= Assuntos =======================

    private void BuildTopicBar()
    {
        foreach (var t in LearnContent.Topics)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = t.Icon, FontSize = 15, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new TextBlock { Text = t.Title, VerticalAlignment = VerticalAlignment.Center });

            var chip = new RadioButton
            {
                Content = content,
                GroupName = "learnTopic",
                Style = (Style)FindResource("TopicChip"),
                Tag = t.Key,
                IsChecked = t.Key == (_state.LastTopic ?? _topicKey),
            };
            chip.Checked += (_, _) => { if (_ready) ShowTopic(t.Key, animate: true); };
            TopicBar.Children.Add(chip);
        }
    }

    private void ShowTopic(string key, bool animate)
    {
        var t = LearnContent.Topics.FirstOrDefault(x => x.Key == key) ?? LearnContent.Topics[0];
        StopArt();
        _topicKey = t.Key;
        _state.LastTopic = t.Key;
        foreach (var (k, a) in _art) a.Art.Visibility = k == t.Key ? Visibility.Visible : Visibility.Collapsed;
        if (IsLoaded) StartArt();

        TopicIcon.Text = t.Icon;
        TopicTitle.Text = t.Title;
        TopicLead.Text = t.Lead;
        TopicAnalogy.Text = t.Analogy;
        FillBullets(TopicSigns, t.Signs, "•", "Warn");
        FillBullets(TopicFixes, t.Fixes, "✓", "Good");
        TopicLink.Content = $"Abrir {t.LinkText} →";
        TopicLink.Tag = t.LinkKey;

        if (!animate) return;
        Animate(TopicText, OpacityProperty, 0, 1, 320);
        Animate(TopicShift, TranslateTransform.XProperty, 18, 0, 380);
        var back = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
        Animate(ArtScale, ScaleTransform.ScaleXProperty, 0.9, 1, 450, back);
        Animate(ArtScale, ScaleTransform.ScaleYProperty, 0.9, 1, 450, back);
        Animate(ArtHost, OpacityProperty, 0.2, 1, 300);
    }

    private static void FillBullets(Panel panel, IEnumerable<string> items, string mark, string brushKey)
    {
        panel.Children.Clear();
        foreach (var item in items)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
            var dot = Text(mark, brushKey, 13.5, FontWeights.Bold);
            dot.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
            row.Children.Add(Text(item, "Text", 13));
            panel.Children.Add(row);
        }
    }

    private void StartArt()
    {
        _runningArt = _art[_topicKey].Anim;
        _runningArt.Begin(this, true);
        _artPaused = false;
        UpdateActivity();
    }

    private void StopArt()
    {
        _runningArt?.Stop(this);
        _runningArt = null;
        _artPaused = false;
    }

    /// <summary>Quadros do filme da ilustração de FPS (repetitivos demais para escrever no XAML).</summary>
    private void BuildFilmStrip()
    {
        var strip = new Rectangle { Width = 600, Height = 56, Opacity = 0.35 };
        strip.SetResourceReference(Shape.FillProperty, "TextDim");
        FilmStrip.Children.Add(strip);
        for (var i = 0; i < 11; i++)
        {
            var frame = new Rectangle { Width = 44, Height = 40, RadiusX = 3, RadiusY = 3 };
            frame.SetResourceReference(Shape.FillProperty, "Track");
            Canvas.SetLeft(frame, 4 + i * 52);
            Canvas.SetTop(frame, 8);
            FilmStrip.Children.Add(frame);

            // A bolinha anda um pouco a cada quadro, como num flipbook.
            var ball = new Ellipse { Width = 10, Height = 10 };
            ball.SetResourceReference(Shape.FillProperty, "Accent");
            Canvas.SetLeft(ball, 4 + i * 52 + 4 + i % 4 * 8);
            Canvas.SetTop(ball, 23 + (i % 2 == 0 ? -4 : 4));
            FilmStrip.Children.Add(ball);
        }
    }

    private void OnTopicLink(object sender, RoutedEventArgs e) => OnNavigate(sender, e);

    // ======================= Laboratório =======================

    private void OnLabChanged(object sender, RoutedEventArgs e)
    {
        if (_ready) UpdateLab();
    }

    private void UpdateLab()
    {
        var cpu = CpuSlider.Value;
        var gpuBase = GpuSlider.Value;
        var (resFactor, resName) = Res4K.IsChecked == true ? (0.3, "4K") : Res1440.IsChecked == true ? (0.6, "1440p") : (1.0, "1080p");
        var gpu = gpuBase * resFactor;
        var capOn = CapSwitch.IsChecked == true;
        var raw = Math.Min(cpu, gpu);
        var capped = capOn && LabFpsCap < raw;
        var fps = capped ? LabFpsCap : raw;
        _labFps = fps;

        CpuValue.Text = $"{cpu:0} quadros/s";
        GpuValue.Text = resFactor < 1 ? $"{gpuBase:0} em 1080p → {gpu:0} em {resName}" : $"{gpuBase:0} quadros/s";
        LabFrametime.Text = $"Cada quadro leva {1000 / fps:0.0} ms";

        var cpuUse = fps / cpu;
        var gpuUse = fps / gpu;
        CpuUseText.Text = $"{cpuUse * 100:0}%";
        GpuUseText.Text = $"{gpuUse * 100:0}%";
        Animate(CpuBarScale, ScaleTransform.ScaleXProperty, CpuBarScale.ScaleX, cpuUse, 450);
        Animate(GpuBarScale, ScaleTransform.ScaleXProperty, GpuBarScale.ScaleX, gpuUse, 450);

        string title, text, brush;
        var gpuLimits = gpu < cpu;
        var balanced = Math.Abs(cpu - gpu) / Math.Max(cpu, gpu) < 0.1;
        if (capped)
        {
            (title, brush) = ("Sem gargalo: o limitador manda", "Good");
            text = $"As duas peças conseguiriam {raw:0} FPS, mas você pediu no máximo {LabFpsCap:0}. Elas trabalham folgadas, esquentam menos " +
                   "e o frametime fica estável. Por isso, quando CPU e GPU estão baixas ao mesmo tempo, procure um limitador ligado.";
        }
        else if (balanced)
        {
            (title, brush) = ("Equilibrado", "Good");
            text = "CPU e GPU estão no mesmo ritmo e nenhuma sobra muito. É o cenário de um PC bem montado para esse jogo.";
        }
        else if (gpuLimits)
        {
            (title, brush) = ("A GPU é o limite (e isso é bom!)", "Gpu");
            text = $"A placa desenha no máximo {gpu:0} quadros/s e a CPU poderia entregar {cpu:0}. GPU perto de 100% é o normal em jogos. " +
                   "Quer mais FPS? Baixe a resolução, a qualidade gráfica ou ative DLSS/FSR.";
        }
        else
        {
            (title, brush) = ("Gargalo na CPU", "Bad");
            text = $"A CPU só prepara {cpu:0} quadros/s e a GPU fica parada esperando ({gpuUse * 100:0}% de uso). Baixar os gráficos não ajuda aqui. " +
                   (resFactor < 0.5 ? "Repare que nem o 4K pesou na GPU o bastante." : "Experimente subir a resolução: o FPS quase não cai e a imagem melhora!");
        }
        LabVerdict.Text = title;
        LabVerdict.SetResourceReference(TextBlock.ForegroundProperty, brush);
        LabVerdictText.Text = text;

        // Quem está esperando quem na linha de montagem
        var cpuLimits = !capped && !balanced && !gpuLimits;
        var gpuIsLimit = !capped && !balanced && gpuLimits;
        CpuGlow.Visibility = cpuLimits || balanced && !capped ? Visibility.Visible : Visibility.Hidden;
        GpuGlow.Visibility = gpuIsLimit || balanced && !capped ? Visibility.Visible : Visibility.Hidden;
        MonGlow.Visibility = capped ? Visibility.Visible : Visibility.Hidden;
        CapLabel.Visibility = MonGlow.Visibility;
        CpuWait.Visibility = gpuIsLimit ? Visibility.Visible : Visibility.Hidden;
        GpuWait.Visibility = cpuLimits ? Visibility.Visible : Visibility.Hidden;
        LabQueue.Visibility = gpuIsLimit ? Visibility.Visible : Visibility.Hidden;
    }

    private void SetLabRunning(bool run)
    {
        if (run == _labRunning) return;
        _labRunning = run;
        if (run)
        {
            _lastRender = TimeSpan.Zero;
            CompositionTarget.Rendering += OnLabFrame;
        }
        else
        {
            CompositionTarget.Rendering -= OnLabFrame;
        }
    }

    private void OnLabFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastRender) return; // o evento pode vir mais de uma vez por quadro
        var dt = _lastRender == TimeSpan.Zero ? 0 : Math.Min((now - _lastRender).TotalSeconds, 0.1);
        _lastRender = now;

        // Número de FPS "rolando" até o valor novo
        _shownFps += (_labFps - _shownFps) * Math.Min(1, dt * 6);
        LabFps.Text = $"{_shownFps:0}";

        // Brilho pulsando na peça que limita
        var pulse = 0.45 + 0.55 * (0.5 + 0.5 * Math.Sin(now.TotalSeconds * 5));
        CpuGlow.Opacity = GpuGlow.Opacity = MonGlow.Opacity = pulse;

        // Quadros saindo da CPU numa taxa proporcional ao FPS
        _spawnAcc += dt * Math.Clamp(_labFps / 12, 0.8, 10);
        while (_spawnAcc >= 1)
        {
            _spawnAcc -= 1;
            var shape = new Rectangle { Width = 16, Height = 16, RadiusX = 3, RadiusY = 3 };
            shape.SetResourceReference(Shape.FillProperty, "Cpu");
            Canvas.SetTop(shape, 47);
            FrameLayer.Children.Add(shape);
            _frames.Add(new FlyingFrame { Shape = shape, X = 92 });
        }

        for (var i = _frames.Count - 1; i >= 0; i--)
        {
            var f = _frames[i];
            f.X += 230 * dt;
            if (!f.Painted && f.X > 350)
            {
                f.Painted = true;
                f.Shape.SetResourceReference(Shape.FillProperty, "Gpu");
            }
            if (f.X > 560)
            {
                FrameLayer.Children.Remove(f.Shape);
                _frames.RemoveAt(i);
                continue;
            }
            Canvas.SetLeft(f.Shape, f.X);
        }
    }

    // ======================= Quiz =======================

    private void StartQuiz()
    {
        _quizIndex = 0;
        _quizScore = 0;
        QuizResultPanel.Visibility = Visibility.Collapsed;
        QuizQuestionPanel.Visibility = Visibility.Visible;
        UpdateQuizBest();
        ShowQuestion();
    }

    private void UpdateQuizBest() =>
        QuizBest.Text = _state.BestQuiz > 0 ? $"Seu recorde: {_state.BestQuiz} de {LearnContent.Quiz.Length}" : "";

    private void ShowQuestion()
    {
        var q = LearnContent.Quiz[_quizIndex];
        QuizCounter.Text = $"PERGUNTA {_quizIndex + 1} DE {LearnContent.Quiz.Length}";
        Animate(QuizBarScale, ScaleTransform.ScaleXProperty, QuizBarScale.ScaleX, (double)_quizIndex / LearnContent.Quiz.Length, 400);
        QuizQuestionText.Text = q.Question;
        QuizFeedback.Visibility = Visibility.Collapsed;
        QuizNext.Visibility = Visibility.Collapsed;

        QuizOptions.Children.Clear();
        for (var i = 0; i < q.Options.Length; i++)
        {
            var index = i;
            var btn = new Button
            {
                Content = new TextBlock { Text = $"{(char)('A' + i)})  {q.Options[i]}", TextWrapping = TextWrapping.Wrap },
                Style = (Style)FindResource("QuizOption"),
                RenderTransform = new TranslateTransform(),
            };
            btn.Click += (_, _) => OnAnswer(index);
            QuizOptions.Children.Add(btn);
            Animate(btn, OpacityProperty, 0, 1, 300, delayMs: 60 * i);
            Animate((TranslateTransform)btn.RenderTransform, TranslateTransform.XProperty, 14, 0, 350, delayMs: 60 * i);
        }

        Animate(QuizQuestionPanel, OpacityProperty, 0, 1, 300);
        Animate(QuizShift, TranslateTransform.YProperty, 10, 0, 350);
    }

    private void OnAnswer(int chosen)
    {
        var q = LearnContent.Quiz[_quizIndex];
        var right = chosen == q.Correct;
        if (right) _quizScore++;

        for (var i = 0; i < QuizOptions.Children.Count; i++)
        {
            var btn = (Button)QuizOptions.Children[i];
            btn.IsHitTestVisible = false;
            if (i == q.Correct)
            {
                btn.SetResourceReference(Control.BackgroundProperty, "Good");
                btn.Foreground = Brushes.White;
            }
            else if (i == chosen)
            {
                btn.SetResourceReference(Control.BackgroundProperty, "Bad");
                btn.Foreground = Brushes.White;
            }
            else
            {
                btn.Opacity = 0.45;
            }
        }

        var chosenBtn = (Button)QuizOptions.Children[chosen];
        var shift = (TranslateTransform)chosenBtn.RenderTransform;
        if (right)
        {
            chosenBtn.RenderTransformOrigin = new Point(0.5, 0.5);
            var pop = new ScaleTransform();
            chosenBtn.RenderTransform = pop;
            var back = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.8 };
            Animate(pop, ScaleTransform.ScaleXProperty, 0.94, 1, 400, back);
            Animate(pop, ScaleTransform.ScaleYProperty, 0.94, 1, 400, back);
        }
        else
        {
            // Balança de leve para os lados
            var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(420) };
            double[] xs = { -8, 7, -5, 3, 0 };
            for (var i = 0; i < xs.Length; i++)
                shake.KeyFrames.Add(new LinearDoubleKeyFrame(xs[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(84 * (i + 1)))));
            shift.BeginAnimation(TranslateTransform.XProperty, shake);
        }

        QuizFeedbackText.Inlines.Clear();
        var head = new Run(right ? "✓ Isso mesmo! " : "✗ Não foi dessa vez. ") { FontWeight = FontWeights.Bold };
        head.SetResourceReference(TextElement.ForegroundProperty, right ? "Good" : "Bad");
        QuizFeedbackText.Inlines.Add(head);
        QuizFeedbackText.Inlines.Add(new Run(q.Explanation));
        QuizFeedback.Visibility = Visibility.Visible;
        Animate(QuizFeedback, OpacityProperty, 0, 1, 300);

        QuizNext.Content = _quizIndex + 1 < LearnContent.Quiz.Length ? "Próxima pergunta →" : "Ver resultado →";
        QuizNext.Visibility = Visibility.Visible;
        Animate(QuizNext, OpacityProperty, 0, 1, 300, delayMs: 150);
    }

    private void OnQuizNext(object sender, RoutedEventArgs e)
    {
        _quizIndex++;
        if (_quizIndex < LearnContent.Quiz.Length)
        {
            ShowQuestion();
            return;
        }
        ShowQuizResult();
    }

    private void ShowQuizResult()
    {
        var total = LearnContent.Quiz.Length;
        Animate(QuizBarScale, ScaleTransform.ScaleXProperty, QuizBarScale.ScaleX, 1, 400);
        QuizCounter.Text = "RESULTADO";
        QuizQuestionPanel.Visibility = Visibility.Collapsed;
        QuizResultPanel.Visibility = Visibility.Visible;

        QuizScoreText.Text = $"{_quizScore} de {total}";
        var ratio = (double)_quizScore / total;
        QuizResultMsg.Text = ratio switch
        {
            1 => "🏆 Gabaritou! Você já pensa como um caçador de gargalos. Agora é colocar em prática na página Caçar gargalos.",
            >= 0.75 => "🎯 Muito bem! Você entendeu o essencial. Releia o assunto das perguntas que errou e tente de novo.",
            >= 0.5 => "👍 Bom começo! Dê uma olhada nos assuntos lá em cima e no laboratório, depois refaça o quiz.",
            _ => "📚 Sem problema: é para isso que esta página existe. Passe pelos assuntos, brinque no laboratório e tente outra vez.",
        };
        QuizScoreText.SetResourceReference(TextBlock.ForegroundProperty, ratio >= 0.75 ? "Good" : ratio >= 0.5 ? "Warn" : "Bad");

        if (_quizScore > _state.BestQuiz)
        {
            _state.BestQuiz = _quizScore;
            JsonStore.Save(StoreName, _state);
            UpdateQuizBest();
        }

        var back = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
        Animate(QuizResultScale, ScaleTransform.ScaleXProperty, 0.85, 1, 500, back);
        Animate(QuizResultScale, ScaleTransform.ScaleYProperty, 0.85, 1, 500, back);
        Animate(QuizResultPanel, OpacityProperty, 0, 1, 350);
    }

    private void OnQuizRestart(object sender, RoutedEventArgs e) => StartQuiz();

    // ======================= Glossário =======================

    private void BuildGlossaryFilters()
    {
        var categories = new[] { "Todos" }.Concat(LearnContent.Glossary.Select(g => g.Category).Distinct());
        foreach (var cat in categories)
        {
            var rb = new RadioButton
            {
                Content = cat,
                GroupName = "glossCat",
                Style = (Style)FindResource("Segment"),
                IsChecked = cat == _glossCategory,
                Margin = new Thickness(0, 0, 6, 6),
            };
            rb.Checked += (_, _) =>
            {
                _glossCategory = cat;
                if (_ready) RefreshGlossary();
            };
            GlossaryFilters.Children.Add(rb);
        }
    }

    private void OnGlossarySearch(object sender, TextChangedEventArgs e)
    {
        if (_ready) RefreshGlossary();
    }

    private void RefreshGlossary()
    {
        var query = Normalize(GlossarySearch.Text.Trim());
        var items = LearnContent.Glossary
            .Where(g => _glossCategory == "Todos" || g.Category == _glossCategory)
            .Where(g => query.Length == 0 || Normalize(g.Term).Contains(query) || Normalize(g.Definition).Contains(query))
            .OrderBy(g => !Normalize(g.Term).Contains(query)) // quem tem a palavra no nome aparece primeiro
            .ThenBy(g => g.Term, StringComparer.Create(new CultureInfo("pt-BR"), true))
            .ToList();

        GlossaryPanel.Children.Clear();
        GlossaryCount.Text = items.Count == 1 ? "1 termo" : $"{items.Count} termos";
        GlossaryEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        for (var i = 0; i < items.Count; i++)
        {
            var g = items[i];
            var pill = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8, 2, 0, 0),
                Child = Text(g.Category, "Accent", 11, FontWeights.SemiBold),
            };
            pill.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
            DockPanel.SetDock(pill, Dock.Right);

            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            header.Children.Add(pill);
            header.Children.Add(Text(g.Term, "Text", 15, FontWeights.SemiBold));

            var body = new StackPanel();
            body.Children.Add(header);
            body.Children.Add(Text(g.Definition, "TextDim", 12.5));

            var card = new Border { Style = (Style)FindResource("Card"), Width = 300, Padding = new Thickness(16, 14, 16, 14), Child = body };
            GlossaryPanel.Children.Add(card);

            // Entrada em cascata só nos primeiros, para não atrasar a lista inteira
            var delay = Math.Min(i, 12) * 25;
            Animate(card, OpacityProperty, 0, 1, 280, delayMs: delay);
        }
    }

    /// <summary>Minúsculas e sem acento, para "memoria" achar "Memória".</summary>
    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}
