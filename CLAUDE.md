# Bottleneck Hunter: instruções para quem trabalha no código

App de desktop para Windows 10/11 (WPF, C#, .NET 8) que detecta gargalos e melhora o desempenho do PC.
Escrito do zero, com código próprio. Inspirado no conjunto de funções do Reset Explode, mas **sem copiar código dele**.

## Regras gerais
- **Tudo que o usuário vê é em português do Brasil**, em linguagem simples e didática: cada tela explica o que mostra e o que fazer.
- Animações suaves (200–600 ms, CubicEase/BackEase), sem exagero. Siga os estilos de `Themes/Styles.xaml`.
- Cores sempre por `DynamicResource` (o tema claro/escuro troca em tempo real). Chaves: `Bg Sidebar Card CardHover Border Text TextDim Accent AccentSoft Good Warn Bad Cpu Gpu Track`.
- O app roda como administrador. Trate arquivos lidos do disco como não confiáveis (use `JsonStore`), carregue DLLs nativas só por caminho absoluto confiável e não extraia nada para `%TEMP%`.
- Todo ajuste que muda o Windows precisa de um botão **Restaurar** que volta ao valor original.

## Compilar
- No Windows: `dotnet build src/BottleneckHunter -c Release`.
- No Linux (sessões na nuvem): rode `tools/setup-linux-build.sh` uma vez e depois o mesmo comando. Compila, mas não executa.
- O GitHub Actions (`.github/workflows/build.yml`) compila em todo PR e publica a release `latest` a cada push na `main`.

## Estrutura e donos de cada pasta
Cada módulo só mexe nas próprias pastas. Assim, várias pessoas trabalham ao mesmo tempo sem conflito.

| Módulo (chave) | Pastas | O quê |
|---|---|---|
| Base (shell) | `App.*`, `MainWindow.*`, `Themes/`, `Controls/`, `Core/*.cs` (raiz), `Views/Overview/` | Janela, menu, tema, sensores, controles comuns |
| Meu PC (`system`) | `Views/SystemInfo/`, `Core/SystemInfo/` | Informações via WMI, RAM por módulo, discos e S.M.A.R.T., relatório HTML/JSON |
| Sensores (`sensors`) | `Views/Sensors/` | Lista de sensores ao vivo com busca, filtros, mín./máx. |
| Caçar gargalos (`bottleneck`) | `Views/Bottleneck/`, `Core/Bottleneck/` | Análise ao vivo, sessão gravada, checagem do PC |
| Benchmark e stress (`bench`) | `Views/Bench/`, `Core/Bench/` | Benchmark de CPU, stress de CPU/RAM/GPU |
| Otimizar (`optimize`) | `Views/Optimize/`, `Core/Optimize/` | Limpeza de memória, ajustes do Windows, inicialização |
| Overlay (`overlay`) | `Views/Overlay/`, `Core/Overlay/` | FPS via ETW, janela de overlay, atalho global |
| Aprenda (`learn`) | `Views/Learn/` | Conteúdo didático, glossário, ilustrações animadas |

Exceções permitidas fora da pasta: adicionar `<PackageReference>` no `.csproj`.

Cada página começa como um *placeholder* (`Views/<Módulo>/<Nome>View.cs`). Para implementar: apague esse `.cs` e crie
`<Nome>View.xaml` + `<Nome>View.xaml.cs` com a **mesma classe e namespace**. `Core/Pages.cs` não precisa mudar.
Estilos específicos do módulo vão em `UserControl.Resources`, não em `Styles.xaml`.

## O que a base oferece
- `App.Sensors` (`Core/SensorHub.cs`): leitura a cada 1 s. `Latest`, evento `Updated` (chega em outra thread: use `Dispatcher.BeginInvoke`),
  `HistorySince(TimeSpan)` (até 15 min), `Pause(bool)`, e `HwSnapshot.All` com todos os sensores.
  Inscreva-se no `Loaded` e cancele no `Unloaded` da página.
- `JsonStore.Load<T>("nome")` / `JsonStore.Save("nome", valor)`: um arquivo por módulo em `%AppData%\BottleneckHunter`. Valide o que ler.
- Controles: `Controls/Gauge` (medidor animado), `Controls/LineChart` (gráfico de linhas), `Controls/PlaceholderView`.
- Estilos: `PageTitle PageSubtitle CardTitle Body Dim BigValue Card TipBox PrimaryButton GhostButton Switch Segment`.
- Navegar para outra página: `(Window.GetWindow(this) as MainWindow)?.Navigate("chave")`.
- Layout padrão de página: `ScrollViewer` > `StackPanel Margin="32,8,18,28"` > título, subtítulo, `TipBox` explicando o assunto, cartões.
- Serviços que precisam iniciar com o app (ex.: atalho do overlay) usam o gancho `Initialize()` já chamado em `App.OnStartup`.
