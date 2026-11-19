# Como o Miniplayer Barra funciona

Uma visão técnica curta, para quem quer ler ou mudar o código. O app é C# / .NET 8 com WinForms (`net8.0-windows10.0.19041.0`), sem serviço, sem rede e sem administrador. Para o uso do dia a dia, veja o [README](../README.md).

## Mapa do código

| Pasta / arquivo | O que tem |
|---|---|
| `src/MiniplayerBarra/Program.cs` | Início: instância única, log, `config.json`, cadastro na inicialização do Windows. Monta tudo quando a barra e as sessões de mídia estão prontas (se falhar, tenta de novo a cada 10 s). |
| `AppController.cs` | Liga as partes: a cada mudança nas sessões, escolhe a mídia e atualiza o player; os cliques viram comandos; as ondas só rodam enquanto a mídia mostrada toca. |
| `Core/` | Regras sem dependência do Windows: `SessionPicker` (qual mídia aparece), `SourceClassifier` (de que fonte é cada sessão), `PauseAll`, `Settings`, `Log`. |
| `Media/GsmtcMediaSessions.cs` | Acesso às sessões de mídia do Windows. |
| `Audio/` | Captura do som e análise de frequências para as ondas; `AppVolume.cs` controla o volume do app da fonte. |
| `Ui/` | Encaixe na barra (`TaskbarPlayer`, `TaskbarProbe`, `TaskbarSlot`), desenho (`PlayerLayout`, `PlayerRenderer`), cor das ondas (`CoverArt`), tema (`Theme`); `ChromeTabs.cs` acha e seleciona a aba que toca. |
| `tests/MiniplayerBarra.Tests` | Testes automáticos (xUnit) das regras, do layout e do controlador, com fakes no lugar do Windows. |

## 1. De onde vem a música: GSMTC

O Windows publica as mídias em reprodução pela API **Global System Media Transport Controls** (`Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager`). É a mesma fonte dos controles de mídia que aparecem no painel de volume do Windows.

- Cada app tocando vira uma **sessão**, com um `SourceAppUserModelId` (AUMID), título, artista, capa e estado (tocando, pausado…).
- A mesma API oferece tocar/pausar, avançar, voltar e parar.
- O app escuta os eventos `SessionsChanged`, `MediaPropertiesChanged` e `PlaybackInfoChanged`. Não há consulta periódica: a troca no player é imediata.
- Os eventos chegam em threads do sistema. Tudo que mexe na interface passa pela thread da interface (`SynchronizationContext` capturado no início).
- Toda chamada ao GSMTC tem limite de 3 s, para o app não travar se a fonte não responder.

**Como cada fonte é reconhecida** (`SourceClassifier`):

| Fonte | AUMID |
|---|---|
| YouTube Music (app do Chrome) | contém `cinhimbnkkghhklpknlkffjgod` (`Chrome._crx_cinhimbnkkghhklpknlkffjgod`) |
| Aba comum do Chrome | exatamente `Chrome` |
| WhatsApp Desktop | começa com `5319275A.WhatsAppDesktop` |
| Outros | qualquer outro, inclusive outros apps do Chrome (`Chrome._crx_*`) |

**Qual mídia aparece** (`SessionPicker`): a que está tocando ganha da pausada; no empate, vale a ordem de `prioridade` do `config.json`; "outros" fica sempre por último. Sem sessão nenhuma, o player some.

**Pausar tudo** (`PauseAll`): o Chrome expõe **uma única** sessão `Chrome`, mesmo com várias abas tocando (verificado na prática). Pausar essa sessão não faz o Chrome passar para a próxima aba que está tocando. Por isso o app repete, até 10 vezes, um ciclo: pausa tudo que está tocando, dá **Stop** na sessão `Chrome` (o Chrome então solta essa aba e expõe a próxima que toca), espera 400 ms e confere de novo. O YouTube Music tem sessão própria e só é pausado. Os detalhes e as medições estão em [verificações práticas](superpowers/plans/2026-11-17-verificacoes.md#1-pausar-tudo).

## 2. As ondas: captura do som por processo

As 12 barrinhas reagem ao som **da fonte mostrada**, e não ao som geral do PC. Assim, uma chamada no Discord ou um alerta do Windows não fazem as barras pularem.

- **Qual processo:** para YouTube Music e abas, o Chrome; para o WhatsApp, o `WhatsApp.Root`. O app procura a **raiz** da árvore desse processo (o `chrome.exe` cujo pai não é outro `chrome.exe`), porque o som do Chrome sai de um processo filho (o serviço de áudio).
- **Como captura:** com a API de *process loopback* do Windows (Windows 10 2004 ou mais novo). O app chama `ActivateAudioInterfaceAsync("VAD\\Process_Loopback", …)` com `PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE` e o PID da raiz. O NAudio 2.4.0 não tem essa ativação pública, então ela é feita por P/Invoke, e o `IAudioClient` resultante é usado pelo `AudioClient` do NAudio (`Audio/ProcessLoopbackCapture.cs`).
- **Plano B:** se a captura por processo falhar, ou se a fonte for "outros", o app usa o som geral do PC (loopback do dispositivo de saída). Se a captura cair (por exemplo, ao trocar de fone), ele tenta de novo em 1 s, dobrando a espera até 30 s.
- **Análise:** FFT de 2048 amostras com janela Hann, 12 bandas em escala logarítmica entre 60 Hz e 12 kHz, nível em dB entre −60 dB e −10 dB. As barras sobem na hora e descem devagar (`Audio/SpectrumAnalyzer.cs`). A animação roda a ~30 quadros por segundo.
- **Cor:** a cor mais viva e frequente da capa (pixels agrupados por matiz, pesados por saturação e brilho), clareada se estiver escura demais. Sem capa, usa a cor de destaque do Windows (`Ui/CoverArt.cs`).
- **Custo:** a captura e a animação **só rodam enquanto a mídia mostrada está tocando**. Pausado, nada disso roda.

O som não é gravado nem salvo: só os níveis das bandas ficam em memória, para desenhar as barras.

## 3. Encaixe na barra de tarefas

O player é uma janela WinForms **filha da `Shell_TrayWnd`** (a janela da barra de tarefas), colocada com `SetParent`. É a mesma técnica do projeto irmão Barra de Bateria (ainda não publicado).

- **Janela em camadas** (`WS_EX_LAYERED`) com fundo transparente: o desenho é feito num bitmap e enviado com `UpdateLayeredWindow`. Texto e botões seguem o tema da barra (claro ou escuro).
- **Sem roubar o foco:** a janela tem `WS_EX_NOACTIVATE` e responde `MA_NOACTIVATE` a `WM_MOUSEACTIVATE`. Os comandos vão direto para a sessão de mídia, sem trazer nenhuma janela para a frente.
- **Explorer reiniciou:** a janela filha morre junto com a barra. Uma janela oculta de nível superior escuta a mensagem `TaskbarCreated` e reencaixa o player.
- **Mudanças na barra:** a cada 3 s (só com o player visível), e também quando o Windows avisa que a tela ou as configurações mudaram, o app relê a barra e se reposiciona se precisar.

### Onde ficam as coisas: UI Automation

Para não cobrir nada, o app precisa saber onde estão os ícones. A barra do Windows 11 é desenhada em XAML, sem janelas Win32 para cada botão, então o app usa **UI Automation** (`System.Windows.Automation`, que vem do WPF; é por isso que o projeto tem `UseWPF`). Ver `Ui/TaskbarProbe.cs`:

- Caminho: `Shell_TrayWnd` → … → `TaskbarFrameRepeater` → botões. O botão Iniciar (`StartButton`) marca o começo dos ícones; o maior `BoundingRectangle.Right` entre os botões marca o fim. Pesquisa e Visão de Tarefas contam como ícones.
- O botão Widgets (`WidgetsButton`) também mora nesse repeater, mas fica de fora da conta dos ícones.
- O alinhamento vem do registro (`TaskbarAl` em `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced`: ausente ou 1 = centralizada, 0 = à esquerda) e os Widgets de `TaskbarDa`.
- A borda esquerda da bandeja vem da janela `TrayNotifyWnd`. O Barra de Bateria é achado como filha da `Shell_TrayWnd` que pertence ao processo `BarraBateria` (pelo processo, não pela classe da janela, que muda a cada build).
- Se a UI Automation falhar, o app usa posições estimadas e registra isso no log.

A UI Automation é lenta, então só é chamada ao reposicionar, nunca a cada quadro. Ela também é a principal razão de o app usar cerca de 40 MB de memória.

### A posição (`Ui/TaskbarSlot.cs`)

| Barra | Lugar |
|---|---|
| Centralizada, sem Widgets | Da ponta esquerda da barra até antes do Iniciar. |
| Centralizada, com Widgets | Do fim do botão Widgets (clima) até antes do Iniciar. |
| À esquerda | Do fim dos ícones até antes do menor entre: Barra de Bateria, botão Widgets (que nesse caso vai para a direita) e bandeja. O player encosta no limite da direita. |

Se faltar espaço, o texto encolhe (com "…"), depois some. Os botões e a capa nunca somem (`Ui/PlayerLayout.cs`).

## 4. Abrir a aba e volume do app

**Abrir a aba (`Ui/SourceWindow.cs` + `Ui/ChromeTabs.cs`).** Primeiro tenta o caminho rápido: uma janela cujo título contém o título da mídia. Isso só funciona quando a aba que toca é a ativa da janela. Se falhar, `ChromeTabs.TrySelect` lê pela UI Automation todas as abas (`TabItem` com `ClassName == "TabView"`) das janelas do processo `chrome`, escolhe a que casa com o título (preferindo a que tem "Audio playing" no nome), chama `SelectionItemPattern.Select()` e traz a janela dela para a frente. Leva algumas centenas de ms, por isso o `AppController` roda o "abrir fonte" fora da thread da interface.

**Volume (`Audio/AppVolume.cs`).** A rodinha vira "cliques" inteiros (o touchpad acumula deltas até 120). O `AppController` lê o volume da sessão de áudio do app (`SimpleAudioVolume`, via NAudio), anda em degraus de 5% (`Core/VolumeStep.cs`) e grava de volta. O player mostra "Volume N%" no lugar do artista por 1,5 s.

## 5. Limitações conhecidas

- **O encaixe na barra não é oficial.** O Windows não tem uma API para colocar coisas dentro da barra de tarefas. Tornar uma janela filha da `Shell_TrayWnd` e ler os ícones pela UI Automation funciona hoje (Windows 11, build 26200), mas uma atualização do Windows pode mudar a estrutura da barra e quebrar o encaixe ou a leitura das posições. Nesse caso, o app tenta posições estimadas e escreve no log.
- **Abas do Chrome:** o Chrome mostra ao Windows só **uma** aba por vez (a que começou a tocar por último). O player mostra essa aba; as outras abas tocando ficam invisíveis para ele até a aba mostrada sair. Depois do "Pausar tudo", as abas que levaram Stop somem do player até tocarem de novo, e o botão ▷ do player não consegue retomá-las.
- **Volume do Chrome é um só:** o Chrome tem uma única sessão de áudio para todas as abas e para o YouTube Music instalado, então a rodinha muda o volume de todos juntos. Fontes "outros" (Spotify etc.) não têm controle de volume nem "abrir".
- **Achar a aba depende do nome que o Chrome dá a ela** (título da página + sufixos como "Audio playing"). Se o título da mídia não aparecer no nome da aba e houver mais de uma aba tocando, nada é aberto.
- **Só o monitor principal.** O player fica na barra do monitor principal.
- **WhatsApp:** o WhatsApp Desktop publica um título genérico e nenhuma capa; o player mostra um ícone de nota musical.
- **Ícone dos apps "outros":** os botões funcionam só se o app aceitar os comandos do Windows, e as ondas usam o som geral do PC.

## Testes

```powershell
dotnet test
```

Os testes cobrem as regras (prioridade, classificação, "Pausar tudo", configuração), o layout, a posição na barra, a análise de som e o controlador, com fakes no lugar do Windows. O encaixe real na barra, o GSMTC e a captura de som são conferidos à mão (veja as [verificações práticas](superpowers/plans/2026-11-17-verificacoes.md)).
