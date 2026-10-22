# 🎯 Bottleneck Hunter

**Descubra qual peça está segurando o desempenho do seu PC, e resolva.**

O Bottleneck Hunter é um app gratuito para Windows que mostra, em linguagem simples, se o seu PC está limitado pelo
processador, pela placa de vídeo, pela memória ou pela temperatura. Ele também traz ajustes seguros do Windows (sempre com
botão de desfazer) e um overlay de FPS para jogos, no estilo RivaTuner.

> 🚧 Em desenvolvimento. Algumas telas ainda aparecem como "em construção".

## O que ele faz
- **Visão geral:** medidores animados de CPU, GPU, RAM e VRAM, mais um gráfico ao vivo e uma leitura rápida do gargalo.
- **Meu PC:** todas as peças do computador explicadas.
- **Sensores:** temperaturas, clocks, consumo e ventoinhas em tempo real.
- **Caçar gargalos:** grava uma sessão de jogo e diz quanto tempo cada peça limitou o FPS, com dicas do que fazer.
- **Benchmark e stress:** mede a força do PC e testa a estabilidade com limite de temperatura.
- **Otimizar o PC:** limpeza de memória, ajustes do Windows e programas de inicialização, tudo com "Restaurar".
- **Overlay de jogos:** FPS, frametime, 1% low e uso de CPU/GPU por cima do jogo (Ctrl+Shift+O).
- **Aprenda:** o que é gargalo, CPU, GPU, RAM, VRAM, frametime e mais.

## Como baixar
1. Abra a aba [Releases](../../releases) e baixe `BottleneckHunter-win-x64.zip` da versão **latest**.
2. Extraia a pasta e abra `BottleneckHunter.exe` (ele pede permissão de administrador para ler os sensores).
3. Para ver a temperatura da CPU, instale o driver PawnIO uma vez: `winget install namazso.PawnIO`.

## Para desenvolvedores
- Windows 10/11 + [.NET 8 SDK](https://dotnet.microsoft.com/download).
- `dotnet run --project src/BottleneckHunter`
- Estrutura e regras do código: veja [CLAUDE.md](CLAUDE.md).

## Bibliotecas usadas
- [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL 2.0), para os sensores.
- [TraceEvent](https://github.com/microsoft/perfview) (MIT), para medir o FPS via ETW.
- System.Management (MIT), para as informações do Windows (WMI).
