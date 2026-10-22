namespace BottleneckHunter.Views.Learn;

/// <summary>Um assunto da página Aprenda. <see cref="Key"/> casa com a ilustração "Art_{Key}" e a animação "Anim_{Key}" do XAML.</summary>
public sealed record LearnTopic(
    string Key, string Icon, string Title, string Lead, string Analogy,
    string[] Signs, string[] Fixes, string LinkKey, string LinkText);

public sealed record GlossaryTerm(string Term, string Category, string Definition);

public sealed record QuizQuestion(string Question, string[] Options, int Correct, string Explanation);

/// <summary>O que a página guarda entre uma abertura e outra (validado ao ler).</summary>
public sealed class LearnState
{
    public string? LastTopic { get; set; }
    public int BestQuiz { get; set; }
}

/// <summary>Todo o texto didático da página Aprenda.</summary>
public static class LearnContent
{
    public static readonly LearnTopic[] Topics =
    {
        new("gargalo", "🍾", "O que é gargalo?",
            "Gargalo é a peça que segura o desempenho de todas as outras. Num jogo, cada quadro (frame) passa por uma linha de montagem: " +
            "a CPU prepara a cena (física, personagens, o que vai aparecer) e a GPU desenha a imagem. O FPS final é sempre o da etapa mais lenta.",
            "Pense numa garrafa virada: não importa se ela é enorme, a água só sai na velocidade do gargalo. Trocar uma peça que não é o gargalo quase não muda nada.",
            new[]
            {
                "Uma peça fica perto de 100% enquanto as outras sobram.",
                "Baixar a qualidade gráfica não aumenta o FPS: o gargalo está na CPU.",
                "Baixar a resolução aumenta muito o FPS: o gargalo está na GPU.",
                "Travadinhas aparecem quando a RAM, a VRAM ou o disco enchem.",
            },
            new[]
            {
                "Todo PC tem um gargalo, e isso não é defeito. O ideal é que ele esteja na GPU, a peça mais fácil de ajustar e de trocar.",
                "Abra o jogo e use a página Caçar gargalos para descobrir qual é o seu.",
                "Antes de comprar peça nova, teste: drivers atualizados, memória livre e temperaturas boas já resolvem muita coisa.",
            },
            "bottleneck", "Caçar gargalos"),

        new("cpu", "🧠", "Processador (CPU)",
            "A CPU é o cérebro do PC. Nos jogos, ela calcula tudo que não é desenho: física, inteligência artificial, som, rede e a lista do que a GPU deve desenhar. " +
            "Ela tem vários núcleos, mas muitos jogos ainda dependem de um ou dois núcleos principais.",
            "É o chef da cozinha: ele escreve os pedidos. Se o chef demora, os cozinheiros (a GPU) ficam parados esperando.",
            new[]
            {
                "Uso da GPU abaixo de 90% com o FPS oscilando.",
                "Um núcleo perto de 100%, mesmo com o uso total baixo (ex.: 40%).",
                "Baixar resolução ou gráficos não muda o FPS.",
                "Quedas em cidades cheias, muitas unidades na tela ou partidas online grandes.",
            },
            new[]
            {
                "Feche programas em segundo plano: navegador, launchers, gravadores.",
                "Use o plano de energia Alto desempenho.",
                "Confira a temperatura: CPU quente reduz a velocidade sozinha (throttling).",
                "No jogo, reduza o que pesa na CPU: distância de visão, multidão e física.",
                "Se a GPU está sobrando, aumente a resolução ou a qualidade: a imagem melhora e o FPS quase não cai.",
            },
            "bench", "Benchmark e stress"),

        new("gpu", "🎨", "Placa de vídeo (GPU)",
            "A GPU desenha a imagem: milhões de pixels, texturas, luzes e sombras, dezenas de vezes por segundo. " +
            "Quanto maior a resolução e a qualidade gráfica, mais trabalho ela tem.",
            "São os pintores: recebem o rascunho da CPU e pintam cada quadro. Tela maior (resolução alta) é mais parede para pintar.",
            new[]
            {
                "Uso da GPU entre 95% e 100% no jogo. Isso é normal e até o ideal!",
                "Baixar a resolução ou a qualidade aumenta bastante o FPS.",
                "A temperatura da GPU sobe e as ventoinhas aceleram.",
            },
            new[]
            {
                "Use DLSS, FSR ou XeSS: o jogo desenha menor e a imagem é ampliada com pouca perda.",
                "Abaixe primeiro o que mais pesa: sombras, reflexos, ray tracing e oclusão de ambiente.",
                "Mantenha o driver de vídeo atualizado.",
                "Limite o FPS à taxa do monitor (ex.: 144) para a GPU não trabalhar à toa.",
            },
            "overlay", "Overlay de jogos"),

        new("ram", "📋", "Memória RAM",
            "A RAM é a mesa de trabalho do PC: guarda o que os programas estão usando agora, para a CPU pegar rápido. " +
            "Ela é muito mais rápida que o disco, mas se apaga quando o PC desliga.",
            "É a bancada da cozinha. Bancada pequena faz o chef ir à despensa (o disco) toda hora, e tudo atrasa.",
            new[]
            {
                "Uso de RAM acima de 90% durante o jogo.",
                "Travadinhas e quedas bruscas, principalmente ao entrar em áreas novas.",
                "O disco trabalhando muito enquanto você joga: o Windows está usando o disco como memória.",
            },
            new[]
            {
                "Para jogos atuais, 16 GB é o recomendado e 32 GB dá folga.",
                "Use dois pentes iguais (dual channel): dobra o caminho entre a RAM e a CPU.",
                "Ative o XMP ou EXPO na BIOS para a memória rodar na velocidade da caixa.",
                "Feche o navegador cheio de abas antes de jogar.",
            },
            "system", "Meu PC"),

        new("vram", "🖼", "Memória de vídeo (VRAM)",
            "A VRAM é a memória que fica dentro da placa de vídeo. Ela guarda texturas, modelos e efeitos que a GPU precisa ter à mão. " +
            "Quando ela enche, o que sobra vai para a RAM do PC, que é bem mais lenta para a GPU.",
            "É o estoque de tintas ao lado dos pintores. Se acaba o espaço, as tintas vão para outro prédio, e cada viagem atrasa o quadro.",
            new[]
            {
                "VRAM em uso perto do total da placa (ex.: 7,8 de 8 GB).",
                "Travadinhas fortes e texturas borradas que demoram a carregar.",
                "O FPS médio está bom, mas o 1% low (os piores momentos) está muito baixo.",
            },
            new[]
            {
                "Abaixe a qualidade das texturas: é a opção que mais usa VRAM.",
                "Ray tracing e geração de quadros também consomem bastante VRAM.",
                "Resolução mais alta precisa de mais VRAM.",
                "Feche o que usa a GPU em segundo plano (vídeo no navegador, gravação).",
            },
            "sensors", "Sensores"),

        new("disco", "💾", "Disco (SSD e HD)",
            "O disco é onde ficam o Windows, os jogos e seus arquivos. Ele quase não muda o FPS, mas decide quanto tempo leva para carregar " +
            "e se o jogo engasga ao buscar coisas novas no meio da partida.",
            "É a despensa. O HD fica no fim do corredor, com um funcionário que anda devagar. O SSD fica do lado, com vários funcionários pegando tudo ao mesmo tempo.",
            new[]
            {
                "Telas de carregamento muito longas.",
                "Objetos e texturas surgindo do nada e travadas ao andar pelo mapa.",
                "Disco em 100% de uso no Gerenciador de Tarefas.",
                "Alertas de saúde (S.M.A.R.T.) indicando desgaste ou setores ruins.",
            },
            new[]
            {
                "Instale o Windows e os jogos num SSD. O SSD NVMe é o mais rápido.",
                "Deixe pelo menos 15% do disco livre: SSD muito cheio fica lento.",
                "Confira a saúde dos discos na página Meu PC.",
            },
            "system", "Meu PC"),

        new("temperatura", "🌡", "Temperatura",
            "Toda peça esquenta quando trabalha. Para não se danificar, a CPU e a GPU baixam a própria velocidade quando passam do limite. " +
            "Isso se chama throttling e derruba o FPS sem nenhum aviso.",
            "É como um corredor num dia quente: ele diminui o ritmo para não passar mal.",
            new[]
            {
                "Faixa tranquila em jogo: até uns 80 °C para CPU e GPU.",
                "CPU acima de 90 °C ou GPU acima de 85 °C merece atenção.",
                "O FPS começa bom e cai depois de alguns minutos jogando.",
                "A frequência (GHz) cai enquanto a temperatura está no máximo.",
            },
            new[]
            {
                "Limpe a poeira do gabinete, das ventoinhas e dos filtros.",
                "Garanta ar frio entrando pela frente e ar quente saindo atrás e em cima.",
                "Troque a pasta térmica se o PC tem mais de 3 ou 4 anos.",
                "Notebook: use em superfície dura, nunca na cama ou no colo.",
            },
            "sensors", "Sensores"),

        new("fps", "🎞", "FPS e frametime",
            "FPS é quantos quadros (imagens) aparecem por segundo. Frametime é quanto tempo cada quadro levou, em milissegundos. " +
            "Para a sensação de fluidez, um frametime estável importa tanto quanto um FPS alto.",
            "É um flipbook: 60 páginas por segundo parecem movimento. Se uma página demora a virar, você sente o tranco, mesmo que a média esteja boa.",
            new[]
            {
                "60 FPS = um quadro a cada 16,7 ms. 144 FPS = um a cada 6,9 ms.",
                "Picos no gráfico de frametime = travadinhas (stutter).",
                "1% low é a média dos piores momentos: quanto mais perto do FPS médio, mais suave.",
            },
            new[]
            {
                "Limite o FPS um pouco abaixo da taxa do monitor para um frametime mais estável.",
                "Ative G-Sync ou FreeSync se o seu monitor tiver.",
                "Use o overlay para ver FPS e frametime dentro do jogo.",
            },
            "overlay", "Overlay de jogos"),
    };

    public static readonly GlossaryTerm[] Glossary =
    {
        new("Gargalo", "Básico", "A peça mais lenta, que limita o desempenho de todo o conjunto."),
        new("Benchmark", "Básico", "Teste padronizado que mede o desempenho, para comparar antes e depois de um ajuste ou com outros PCs."),
        new("Stress test", "Básico", "Teste que força uma peça a 100% por um tempo para ver se ela aguenta sem travar nem esquentar demais."),
        new("Driver", "Básico", "Programa que ensina o Windows a conversar com uma peça, como a placa de vídeo. Driver novo costuma trazer ganhos em jogos."),
        new("BIOS / UEFI", "Básico", "O painel de controle da placa-mãe, que roda antes do Windows. É lá que se ativa o XMP, por exemplo."),
        new("Sensor", "Básico", "Pecinha que mede temperatura, voltagem, velocidade ou uso de um componente. O app lê os sensores a cada segundo."),

        new("CPU (processador)", "CPU", "Executa as instruções dos programas. Nos jogos, cuida da lógica e da física e prepara o que a GPU vai desenhar."),
        new("Núcleo (core)", "CPU", "Uma mini CPU dentro do processador. Mais núcleos fazem mais coisas ao mesmo tempo."),
        new("Thread", "CPU", "Uma linha de trabalho. Com Hyper-Threading ou SMT, cada núcleo atende duas threads ao mesmo tempo."),
        new("Clock (frequência)", "CPU", "Quantos ciclos por segundo a peça faz, em GHz ou MHz. Mais alto é mais rápido, mas esquenta mais."),
        new("Cache", "CPU", "Memória minúscula e muito rápida dentro da CPU. Cache grande ajuda muito em jogos (ex.: processadores X3D)."),
        new("IPC", "CPU", "Instruções por ciclo: quanto trabalho a CPU faz a cada tique do clock. Explica por que uma CPU nova a 4 GHz ganha de uma antiga a 4 GHz."),
        new("Draw call", "CPU", "Um pedido da CPU para a GPU desenhar algo. Muitos objetos na tela geram muitas draw calls e mais trabalho para a CPU."),

        new("GPU (placa de vídeo)", "GPU", "Processador especializado em desenhar imagens, com milhares de núcleos pequenos trabalhando juntos."),
        new("VRAM", "GPU", "A memória da placa de vídeo, onde ficam texturas e modelos. Quando enche, aparecem travadinhas."),
        new("Ray tracing", "GPU", "Técnica que simula o caminho real da luz para reflexos e sombras realistas. Lindo, mas muito pesado."),
        new("DLSS / FSR / XeSS", "GPU", "Upscaling: o jogo desenha numa resolução menor e um algoritmo amplia a imagem, ganhando FPS."),
        new("Geração de quadros", "GPU", "Cria quadros intermediários por IA para aumentar o FPS exibido. Funciona melhor quando o FPS base já é bom."),
        new("Shader", "GPU", "Pequeno programa que roda na GPU para calcular cor, luz e efeitos. \"Compilando shaders\" é o jogo preparando esses programas."),

        new("RAM", "Memória", "Memória de trabalho rápida, que guarda o que está aberto agora. Apaga ao desligar o PC."),
        new("Dual channel", "Memória", "Usar dois (ou quatro) pentes iguais para a CPU acessar a memória por dois caminhos ao mesmo tempo."),
        new("XMP / EXPO", "Memória", "Perfil na BIOS que faz a RAM rodar na velocidade anunciada na caixa. Desligado, ela roda mais devagar."),
        new("Latência (CL)", "Memória", "O tempo que a memória leva para responder. Quanto menor, melhor."),
        new("Arquivo de paginação", "Memória", "Espaço no disco que o Windows usa como RAM extra quando ela acaba. Funciona, mas é muito mais lento."),

        new("SSD", "Disco", "Disco sem partes móveis, feito de chips de memória. Muito mais rápido que o HD."),
        new("NVMe", "Disco", "Tipo de SSD ligado direto na placa-mãe, várias vezes mais rápido que um SSD SATA."),
        new("HD (HDD)", "Disco", "Disco com pratos girando e uma agulha que lê os dados. Grande e barato, mas lento."),
        new("S.M.A.R.T.", "Disco", "Autodiagnóstico dos discos que avisa sobre desgaste e falhas antes que aconteçam."),

        new("FPS", "Jogos", "Quadros por segundo: quantas imagens o jogo mostra a cada segundo."),
        new("Frametime", "Jogos", "Tempo para gerar cada quadro, em milissegundos. 16,7 ms equivale a 60 FPS."),
        new("1% low", "Jogos", "A média do 1% dos piores quadros. Mostra a pior parte da experiência."),
        new("Stutter (travadinha)", "Jogos", "Um quadro que demora muito mais que os outros e dá um tranco na tela."),
        new("Resolução", "Jogos", "Quantidade de pixels da imagem (ex.: 1920×1080). Mais pixels deixam a imagem mais nítida e dão mais trabalho à GPU."),
        new("Taxa de atualização (Hz)", "Jogos", "Quantas vezes por segundo o monitor mostra uma imagem nova. Um monitor de 144 Hz exibe até 144 FPS."),
        new("V-Sync", "Jogos", "Sincroniza o FPS com o monitor para acabar com o tearing, mas pode adicionar atraso."),
        new("Tearing", "Jogos", "Imagem rasgada na horizontal, quando o FPS e o monitor estão fora de sincronia."),
        new("G-Sync / FreeSync", "Jogos", "O monitor acompanha o FPS do jogo: sem tearing e quase sem atraso."),
        new("Input lag", "Jogos", "O atraso entre apertar um botão e ver a reação na tela."),

        new("Throttling", "Energia e calor", "Quando a peça reduz a própria velocidade para não esquentar demais ou passar do limite de energia."),
        new("TDP", "Energia e calor", "Quanto calor, em watts, o cooler precisa tirar da peça. Dá uma ideia do consumo."),
        new("Pasta térmica", "Energia e calor", "Pasta entre o chip e o cooler que ajuda o calor a passar. Resseca com os anos."),
        new("Plano de energia", "Energia e calor", "Configuração do Windows que define se o PC prioriza economia ou desempenho."),
        new("Undervolt", "Energia e calor", "Reduzir a voltagem da peça mantendo a velocidade: ela esquenta menos e às vezes até rende mais."),
        new("Overclock", "Energia e calor", "Fazer a peça rodar acima da velocidade de fábrica. Ganha desempenho, mas esquenta e consome mais."),
        new("Fonte (PSU)", "Energia e calor", "Converte a energia da tomada para o PC. Fonte fraca ou ruim causa desligamentos em jogos pesados."),
    };

    public static readonly QuizQuestion[] Quiz =
    {
        new("No jogo, a GPU está em 99% de uso e a CPU em 45%. Onde está o gargalo?",
            new[] { "Na CPU", "Na GPU", "Na memória RAM", "Não existe gargalo" }, 1,
            "GPU perto de 100% é o limite. E isso é o normal em jogos: significa que a placa está dando tudo."),
        new("Você baixou todos os gráficos para o mínimo e o FPS não mudou. O que isso indica?",
            new[] { "Gargalo na CPU", "Gargalo na GPU", "Monitor com defeito" }, 0,
            "Gráficos mais leves aliviam a GPU. Se o FPS não sobe, quem está segurando é a CPU."),
        new("O que acontece quando a VRAM enche?",
            new[] { "O jogo fecha na hora", "As texturas vão para a RAM e aparecem travadinhas", "A GPU fica mais rápida" }, 1,
            "O que não cabe na VRAM vai para a RAM do PC, bem mais lenta para a GPU. Resultado: travadinhas e texturas borradas."),
        new("60 FPS equivalem a qual frametime?",
            new[] { "6,9 ms", "16,7 ms", "60 ms" }, 1,
            "1000 ms ÷ 60 quadros = 16,7 ms por quadro. Já 144 FPS dá 6,9 ms."),
        new("O FPS começa alto e vai caindo depois de 10 minutos de jogo. Qual a suspeita mais provável?",
            new[] { "Superaquecimento (throttling)", "Pouco espaço no disco", "Driver de áudio" }, 0,
            "Quando esquenta demais, a peça baixa a própria velocidade para se proteger. Confira as temperaturas na página Sensores."),
        new("Para que serve o XMP/EXPO na BIOS?",
            new[] { "Fazer a RAM rodar na velocidade anunciada", "Aumentar a VRAM", "Ligar o ray tracing" }, 0,
            "Sem o XMP/EXPO, a memória roda na velocidade mínima de segurança, que pode ser bem menor que a da caixa."),
        new("Uso total da CPU em 35%, mas um núcleo está em 100% e a GPU em 60%. O que está acontecendo?",
            new[] { "Gargalo na CPU: o jogo depende de um núcleo principal", "Está tudo folgado", "Gargalo na GPU" }, 0,
            "O uso total é a média de todos os núcleos. Se o núcleo principal do jogo está no talo, ele segura o FPS e a GPU fica esperando."),
        new("Qual troca deixa o carregamento dos jogos muito mais rápido?",
            new[] { "Mais RAM", "Trocar o HD por um SSD", "Um monitor de 144 Hz" }, 1,
            "Carregamento depende do disco. Um SSD lê os arquivos muitas vezes mais rápido que um HD."),
    };
}
