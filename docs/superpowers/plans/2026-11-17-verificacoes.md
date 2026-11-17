# Verificações práticas (Task 2)

Feitas em 2026-11-17 no PC de desenvolvimento: Windows 11 (build 26200), tela 1920×1080 em 100% (96 DPI), barra de tarefas embaixo, com 48 px de altura. O protótipo foi um console .NET 8 descartável (`.scratch/verificacoes/`, fora do Git) com NAudio 2.4.0, `Windows.Media.Control` e `System.Windows.Automation`.

## 1. Pausar tudo

**Montagem:** 3 vídeos de música públicos do YouTube, cada um numa janela nova do Chrome (`chrome.exe --new-window <url>`), todos tocando, e o app do YouTube Music tocando. A conferência foi feita pelo nome das abas na UI Automation (o Chrome acrescenta `- Audio playing` ao nome da aba que está tocando) e pelo pico da sessão de áudio do Chrome (`AudioSessionControl.AudioMeterInformation.MasterPeakValue`, NAudio).

**Sessões GSMTC com tudo tocando:** 3 sessões no total.

| AUMID | Quantas |
|---|---|
| `Chrome._crx_cinhimbnkkghhklpknlkffjgod` (YT Music) | 1 |
| `Chrome` (abas) | **1**, mesmo com 3 abas tocando (e mais uma aba pausada que já existia) |
| `5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App` | 1 |

Confirmado: o Chrome expõe **uma única** sessão `Chrome` ao Windows. Ela mostra a aba que começou a tocar por último.

**Ciclo só com `TryPauseAsync`: NÃO funciona.**
Ciclo 1: pausa o YT Music e a sessão `Chrome`. Depois de 400 ms, todas as sessões aparecem como `Paused` e o ciclo termina. Só que 2 das 3 abas **continuam tocando**. O nome delas continua com `Audio playing` e o pico do Chrome continua em ~0,45. Mesmo depois de 12 s, a sessão `Chrome` segue presa na aba pausada e não passa para outra aba que está tocando.

**Ciclo com `TryPauseAsync` + `TryStopAsync`: funciona.** Testei 2 vezes, com o mesmo resultado.

```
até 10 vezes:
    tocando = sessões com PlaybackStatus == Playing
    se tocando está vazio: parar
    para cada sessão em tocando:
        await s.TryPauseAsync()
        se s.SourceAppUserModelId == "Chrome":
            await s.TryStopAsync()   // o Chrome solta a aba e expõe a próxima que está tocando
    esperar 400 ms
```

Resultado observado: ciclo 1 (YT Music + aba A) → a sessão `Chrome` passa para a aba B, `Playing`. Ciclo 2 (aba B) → passa para a aba C. Ciclo 3 (aba C) → a sessão `Chrome` volta para uma aba que já estava pausada. Ciclo 4: nada tocando. Foram **3 ciclos** e cerca de 1,3 s. Depois disso, nenhuma aba tinha `Audio playing` e o pico do Chrome ficou em 0,000 por 3 s.

Observações:
- Na sessão `Chrome` tocando, `IsPauseEnabled` pode vir `false` e `IsStopEnabled` vem `true`. Mesmo assim, `TryPauseAsync` funcionou. `TryStopAsync` sozinho também já tira a aba da sessão.
- Uma aba que recebeu Stop some do GSMTC até voltar a tocar. Então, depois do "Pausar tudo", o player não consegue retomar essa aba pelo botão ▷. Isso é aceitável para o "Pausar tudo".
- Não usar Stop no YT Music: ele já tem sessão própria e basta pausar.
- O teste foi feito com uma aba por janela, que vira visível ao abrir. Não testei várias abas tocando na mesma janela. Como o comportamento vem da sessão de mídia do Chrome, deve ser o mesmo, mas isso fica para conferir no teste final.
- Alternativas olhadas e descartadas: achar o botão de pausa do YouTube pela UI Automation (não aparece de forma confiável), o botão "Control your music, videos, and more" do Chrome (`MediaToolbarButtonView`; o `Expand` da UIA devolveu `E_FAIL`) e o botão "Mute tab" (`AlertIndicatorButton`), que só tira o som.

## 2. Som por processo (Chrome)

- **NAudio 2.4.0 não tem API pública de captura por processo.** `ProcessLoopbackMode` e `AudioClientActivationType` existem, mas são `internal`, e não existe `CreateForProcessCaptureAsync`.
- **Funcionou o P/Invoke + NAudio:**
  - `ActivateAudioInterfaceAsync("VAD\\Process_Loopback", typeof(IAudioClient).GUID, PROPVARIANT{vt=VT_BLOB, blob=AUDIOCLIENT_ACTIVATION_PARAMS{ActivationType=1 (PROCESS_LOOPBACK), TargetProcessId=raiz, ProcessLoopbackMode=0 (INCLUDE_TARGET_PROCESS_TREE)}}, handler, out op)` (de `Mmdevapi.dll`).
  - O handler implementa o `IActivateAudioInterfaceCompletionHandler` do NAudio e também `IAgileObject`.
  - O `IAudioClient` devolvido vai para `new NAudio.CoreAudioApi.AudioClient(iac)`, que tem construtor público.
  - `Initialize(Shared, Loopback | EventCallback | AutoConvertPcm, 2_000_000 /*200 ms*/, 0, new WaveFormat(44100, 16, 2), Guid.Empty)`, depois `SetEventHandle`, `AudioCaptureClient` (`GetNextPacketSize` / `GetBuffer` / `ReleaseBuffer`) e `Start`.
  - No process loopback, `GetMixFormat` não serve. O formato tem que ser passado.
- **Raiz:** o `chrome.exe` cujo processo pai não é `chrome.exe` (via `CreateToolhelp32Snapshot`). Havia 1 raiz entre ~47 processos `chrome.exe`. O som sai de um processo filho (o serviço de áudio do Chrome), e não da raiz: a sessão de áudio do Chrome aparece com o PID de um filho. Por isso `includeProcessTree` é obrigatório.
- **Picos a cada 100 ms:** com o YT Music tocando, de 0,08 a 0,24. Pausado, 0,000 o tempo todo. Com o loopback continua chegando pacote enquanto está pausado (zeros), então quem para a captura é o app, pelo estado da sessão.
- **Isolamento:** com o YT Music tocando e o alvo trocado para o `explorer.exe`, o pico ficou em 0,000. A captura pega só a árvore do processo pedido.
- Conclusão para a Task 7: usar P/Invoke de `ActivateAudioInterfaceAsync` + `AudioClient` do NAudio. O `WasapiLoopbackCapture` fica como plano B.

## 3. Posições na barra (UI Automation)

Caminho: `Shell_TrayWnd` → `Windows.UI.Composition.DesktopWindowContentBridge` → `Windows.UI.Input.InputSite.WindowClass` → `TaskbarFrame` (class `Taskbar.TaskbarFrameAutomationPeer`) → `TaskbarFrameRepeater` (Group) → botões. Use o `TreeWalker.RawViewWalker`.

| Elemento | AutomationId | ClassName | Largura (100%) |
|---|---|---|---|
| Iniciar | `StartButton` | `ToggleButton` | 45 px (centralizada), 55 px (à esquerda) |
| Ícone de app | `Appid: <AUMID>` (ex.: `Appid: Chrome`, `Appid: MSEdge`) | `Taskbar.TaskListButtonAutomationPeer` | 44 px |
| Widgets | `WidgetsButton` | `ToggleButton` | 152 px (com o clima) |
| Bandeja e relógio (fora do repeater) | `SystemTrayIcon` | `SystemTray.*` | — |

- **Último ícone de app:** o filho de `TaskbarFrameRepeater` com class `Taskbar.TaskListButtonAutomationPeer` que tem o maior `BoundingRectangle.Right`. Neste PC, Pesquisa e Visão de Tarefas estão desligados e não apareceram. Quando estiverem ligados, devem aparecer no mesmo repeater e contam como ícones (spec, seção 6).
- **Cuidado:** com Widgets ligados, o `WidgetsButton` fica **dentro** do `TaskbarFrameRepeater`. Por isso o retângulo do repeater não serve como "área dos ícones". Use o `StartButton` como início dos ícones.
- A janela Win32 `TrayNotifyWnd` (filha de `Shell_TrayWnd`) dá a borda esquerda da bandeja.

Posições medidas (x de 0 a 1920, com o Barra de Bateria aberto):

| Layout | Widgets | Iniciar | Último ícone (borda direita) | Bandeja |
|---|---|---|---|---|
| Centralizada, Widgets desligados | — | 718 | 1203 | 1662 |
| Centralizada, Widgets ligados | 6–158 (à esquerda) | 696 | 1225 | 1662 |
| À esquerda, Widgets desligados | — | 0 | 495 | 1662 |
| À esquerda, Widgets ligados | **1498–1650 (à direita, perto da bandeja)** | 0 | 539 | 1662 |

- No caso "à esquerda + Widgets ligados", o botão Widgets vai para a direita, fica **por baixo** do Barra de Bateria (1416–1654) e encosta na bandeja. Isso não estava previsto na spec. Para o player no layout à esquerda, a borda direita deve ser o menor x entre o Barra de Bateria, o `WidgetsButton` (quando estiver à direita dos ícones) e a `TrayNotifyWnd`.
- Como mudar as configurações: `TaskbarAl` (HKCU\…\Explorer\Advanced) aceita escrita normal. A escrita em `TaskbarDa` é **bloqueada** (UCPD: "operação não autorizada"). Os Widgets foram ligados e desligados pelo app Configurações, via UIA (toggle `SystemSettings_DesktopTaskbar_Da_ToggleSwitch`). No fim, tudo voltou ao original: centralizada (`TaskbarAl` ausente) e Widgets desligados (`TaskbarDa = 0`).

**Janela do Barra de Bateria:** filha direta de `Shell_TrayWnd` (`EnumChildWindows` + `GetWindowThreadProcessId` igual ao PID do processo `BarraBateria`). É a única filha de outro processo. A class é a padrão do WinForms (`WindowsForms10.Window.8.app.0.<hash>_r3_ad1`), e o hash muda conforme a build. Então **identifique pelo processo, não pela class**. Retângulo: 238×24 px, centrado na altura da barra. Ela fica logo à esquerda da bandeja e não mudou de lugar quando o alinhamento mudou.
