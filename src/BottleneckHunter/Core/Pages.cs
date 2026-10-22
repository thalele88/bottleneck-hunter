using System.Windows.Controls;
using BottleneckHunter.Views.Bench;
using BottleneckHunter.Views.Bottleneck;
using BottleneckHunter.Views.Learn;
using BottleneckHunter.Views.Optimize;
using BottleneckHunter.Views.Overlay;
using BottleneckHunter.Views.Overview;
using BottleneckHunter.Views.Sensors;
using BottleneckHunter.Views.SystemInfo;

namespace BottleneckHunter.Core;

/// <summary>Uma página do menu lateral.</summary>
/// <param name="Icon">Glifo da fonte Segoe MDL2 Assets.</param>
public sealed record PageInfo(string Key, string Title, string Icon, string Group, Func<UserControl> Create);

/// <summary>
/// Lista de páginas do app. Cada módulo vive em Views/&lt;Módulo&gt; e Core/&lt;Módulo&gt;;
/// para trocar uma página basta substituir a classe da view, sem mexer aqui.
/// </summary>
public static class Pages
{
    public static readonly IReadOnlyList<PageInfo> All = new PageInfo[]
    {
        new("overview",   "Visão geral",      "", "PAINEL",          () => new OverviewView()),
        new("system",     "Meu PC",           "", "DIAGNÓSTICO",     () => new SystemView()),
        new("sensors",    "Sensores",         "", "DIAGNÓSTICO",     () => new SensorsView()),
        new("bottleneck", "Caçar gargalos",   "", "CAÇAR GARGALOS",  () => new BottleneckView()),
        new("bench",      "Benchmark e stress","", "CAÇAR GARGALOS", () => new BenchView()),
        new("optimize",   "Otimizar o PC",    "", "MELHORAR",        () => new OptimizeView()),
        new("overlay",    "Overlay de jogos", "", "JOGOS",           () => new OverlayView()),
        new("learn",      "Aprenda",          "", "APRENDA",         () => new LearnView()),
    };
}
