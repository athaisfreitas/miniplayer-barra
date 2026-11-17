# Miniplayer Barra: plano de implementação

> Os passos usam caixas de seleção (`- [ ]`) para acompanhar o progresso.

**Goal:** Um player pequeno, encaixado no lado esquerdo da barra de tarefas do Windows 11, que mostra capa, título e artista da mídia em reprodução (prioridade para o YouTube Music), tem botões voltar, pausar e avançar, mostra barrinhas coloridas que reagem ao som e oferece "Pausar tudo" no clique direito.

**Architecture:** Um executável WinForms. Ele tem quatro partes independentes:
- **Media:** lê e comanda as sessões de mídia do Windows (GSMTC).
- **Audio:** captura o som do processo da fonte e transforma em 12 bandas.
- **Lógica pura:** escolhe a sessão, calcula a cor da capa e decide onde o player cabe na barra.
- **Ui:** desenha numa janela filha da `Shell_TrayWnd`, com código adaptado do Barra de Bateria.

O `AppController` liga as quatro partes e roda tudo na thread da interface.

**Tech Stack:** C# 12, .NET 8 (SDK 8.0.428 instalado), `net8.0-windows10.0.19041.0` (WinForms + WinRT `Windows.Media.Control`), NAudio (captura WASAPI + FFT), UI Automation (`UIAutomationClient`) para ler a barra, e xUnit.

**Spec:** `docs/superpowers/specs/2026-11-17-miniplayer-barra-design.md`. Leia junto com este plano.

Todos os caminhos são relativos à pasta do projeto. O projeto irmão de onde se copia código é o Barra de Bateria, na pasta `barra-bateria` ao lado desta (chamado aqui de **BB**).

## Global Constraints

- Windows 11, sem administrador. Leve: ≈ 0% de CPU sem nada tocando e poucas dezenas de MB de memória. A animação e a captura de áudio só rodam enquanto a mídia mostrada está **tocando**.
- Sem rede, sem telemetria, sem atualização automática.
- Prioridade de fonte padrão: **YouTube Music > Chrome (abas) > WhatsApp > outros**. A sessão tocando ganha da pausada. Sem sessão nenhuma, o player some.
- IDs: YT Music = AUMID que contém `cinhimbnkkghhklpknlkffjgod`. Aba do Chrome = AUMID exatamente `Chrome`. WhatsApp = AUMID que começa com `5319275A.WhatsAppDesktop`. Qualquer outro, incluindo outros `Chrome._crx_*`, é "outros".
- O player **nunca some por falta de lugar**. Se faltar espaço, encolhe o texto, depois esconde o texto, e botões e capa continuam visíveis.
- Clicar nos botões **não** pode tirar o foco da janela atual: use `WS_EX_NOACTIVATE` + `MA_NOACTIVATE`, como no BB.
- Menu do clique direito: exatamente **"Pausar tudo"** e **"Sair"**. Textos visíveis em português.
- Abrir com o Windows: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, valor `MiniplayerBarra`. Instância única: mutex `Local\MiniplayerBarra`.
- Instalação em `%LOCALAPPDATA%\Programs\MiniplayerBarra\`. `config.json` fica ao lado do .exe, com chaves em português. Log em `%LOCALAPPDATA%\MiniplayerBarra\log.txt`.
- Eventos do GSMTC e do NAudio chegam em outras threads. **Tudo** que toca a Ui passa pela thread da interface (`SynchronizationContext` capturado no início).
- **Repositório público.** Nada de dados pessoais no código, nos documentos, nos exemplos ou nos testes: nomes de aparelhos, caminhos com nome de usuário, e-mails. Os IDs de app da tabela acima são públicos e podem aparecer.
- Commits: autor `athaisfreitas <322781500+athaisfreitas@users.noreply.github.com>`, já configurado no repositório. **Nunca** adicionar coautores (`Co-Authored-By`) nem linhas de atribuição, nem em commits nem em documentos. Commits direto na `main`.

## Review Focus

1. **A sessão some entre o desenho e o clique** (ex.: a aba foi fechada). O comando é ignorado sem erro e o player se atualiza. Teste: `Command_on_vanished_session_is_ignored` (Task 9).
2. **Título ou artista vazio, enorme ou com emoji/acentos.** O layout corta com "…" e não estoura a largura. Sem artista, o título fica centralizado na vertical. Testes: `Long_title_is_truncated_to_available_width` e `Empty_artist_uses_single_line` (Task 6).
3. **Capa ausente ou com bytes inválidos.** Usa o ícone de nota e a cor de destaque do Windows, sem exceção. Teste: `Invalid_cover_bytes_fall_back_to_accent` (Task 5).
4. **Vários eventos do GSMTC em rajada** (troca de faixa dispara propriedades e estado juntos). O player não pisca nem mostra uma mistura de faixa antiga com faixa nova. Ele mostra sempre o estado mais recente. Teste: `Burst_of_changes_shows_latest_snapshot` (Task 9).
5. **Fone conectado/desconectado ou dispositivo de áudio trocado durante a captura.** A captura reinicia sozinha e as barras voltam. Verificação manual na Task 10, porque depende de hardware.

---

### Task 1: Projeto, base copiada do BB e configuração

**Files:**
- Create: `MiniplayerBarra.sln`, `src/MiniplayerBarra/MiniplayerBarra.csproj`, `src/MiniplayerBarra/app.manifest` (copiar do BB), `tests/MiniplayerBarra.Tests/MiniplayerBarra.Tests.csproj`, `.gitignore` (copiar do BB e acrescentar `config.json` na raiz e em `src/`), `config.exemplo.json`
- Create (copiar do BB e trocar namespace `BarraBateria` → `MiniplayerBarra`): `src/MiniplayerBarra/Core/Log.cs`, `src/MiniplayerBarra/SingleInstance.cs`, `src/MiniplayerBarra/StartupRegistration.cs` (`ValueName = "MiniplayerBarra"`), `src/MiniplayerBarra/Ui/Native.cs`, `src/MiniplayerBarra/Ui/Theme.cs`
- Create: `src/MiniplayerBarra/Core/Settings.cs`, `src/MiniplayerBarra/Core/SourceKind.cs`
- Test: `tests/MiniplayerBarra.Tests/SettingsTests.cs`, `tests/MiniplayerBarra.Tests/SingleInstanceTests.cs`, `tests/MiniplayerBarra.Tests/ThemeTests.cs` (copiar do BB e adaptar)

**Interfaces:**
- Produces:
  - `enum SourceKind { YouTubeMusic, ChromeTab, WhatsApp, Other }`
  - `record Settings(IReadOnlyList<SourceKind> Priority, string YouTubeMusicId, string WhatsAppIdPrefix, bool Waves)` com `static Settings Default` e `static Settings Load(string path)`.
  - `Theme` do BB, acrescido de `Color Accent`, que vem de `HKCU\Software\Microsoft\Windows\DWM\AccentColor` (DWORD em ABGR). Se faltar, usar `#0078D4`.
  - `Log`, `SingleInstance.TryAcquire(string)` e `StartupRegistration.Ensure(string exePath)` iguais ao BB.

O csproj é igual ao do BB (`WinExe`, `net8.0-windows10.0.19041.0`, `UseWindowsForms`, `PerMonitorV2`, `InternalsVisibleTo MiniplayerBarra.Tests`), com `AssemblyName`/`RootNamespace` `MiniplayerBarra` e a referência `NAudio` (última versão estável). O projeto de testes é igual ao do BB.

`config.exemplo.json`:
```json
{
  "prioridade": ["youtubeMusic", "chrome", "whatsapp"],
  "idYoutubeMusic": "cinhimbnkkghhklpknlkffjgod",
  "idWhatsApp": "5319275A.WhatsAppDesktop",
  "ondas": true
}
```

- [ ] **Step 1: Write the failing tests** in `SettingsTests.cs`:
  - `Missing_file_returns_default`: `Priority` = `[YouTubeMusic, ChromeTab, WhatsApp]`, `Waves` = true, `YouTubeMusicId` = `"cinhimbnkkghhklpknlkffjgod"`, `WhatsAppIdPrefix` = `"5319275A.WhatsAppDesktop"`.
  - `Reads_portuguese_keys`: arquivo com `"prioridade": ["whatsapp","youtubeMusic"], "ondas": false` → `Priority` = `[WhatsApp, YouTubeMusic]`, `Waves` = false.
  - `Unknown_priority_names_are_ignored_and_duplicates_dropped`: `["chrome","xyz","chrome"]` → `[ChromeTab]`.
  - `Invalid_json_returns_default`.
  - `ThemeTests.Accent_from_abgr_dword`: `Theme.AccentFromAbgr(unchecked((int)0xFFD47800))` → `Color.FromArgb(0x00, 0x78, 0xD4)`.
- [ ] **Step 2: Run** `dotnet test`. Expected: falha de compilação/FAIL (os tipos não existem).
- [ ] **Step 3: Implement** os arquivos acima. Os nomes de `prioridade` são `youtubeMusic`, `chrome` e `whatsapp`, sem diferenciar maiúsculas. `Other` nunca entra na lista: é sempre o último. Acrescente `static Color AccentFromAbgr(int abgr)` em `Theme`.
- [ ] **Step 4: Run** `dotnet test`. Expected: PASS.
- [ ] **Step 5: Commit** `git add -A && git commit -m "feat: projeto, base do Barra de Bateria e configuração"`

### Task 2: Verificações práticas (protótipo descartável)

Responde três dúvidas da spec antes de construir em cima delas. O código fica em `.scratch/verificacoes/` (pasta no `.gitignore`) e **não** é commitado. Só a anotação dos resultados é commitada.

**Files:**
- Create: `.scratch/verificacoes/` (console .NET descartável)
- Create: `docs/superpowers/plans/2026-11-17-verificacoes.md` (resultados, sem dados pessoais)
- Modify: `.gitignore` (acrescentar `.scratch/`)

- [ ] **Step 1: Pausar tudo.** Abra 3 abas comuns do YouTube no Chrome, todas tocando, mais o YT Music tocando. Liste as sessões GSMTC. Anote quantas sessões `Chrome` aparecem. Depois rode o ciclo "pausar todas as sessões tocando, esperar 400 ms, listar de novo" até 10 vezes e anote se, no fim, **todas** as abas estão pausadas (ouvindo e olhando o Chrome).
  - Se ficar alguma aba tocando: **pare e avise a autora** antes da Task 4, como a spec manda (seção 6).
- [ ] **Step 2: Som por processo.** Capture o áudio só da árvore de processos do Chrome. A raiz é o `chrome.exe` cujo processo pai não é `chrome.exe`. Use `includeProcessTree = true`. Primeiro pela API do NAudio, se a versão tiver captura por processo. Se não tiver, por P/Invoke de `ActivateAudioInterfaceAsync` com `AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK`. Imprima o pico a cada 100 ms com o YT Music tocando e depois pausado.
  - Anote qual caminho funcionou. Se nenhum funcionar, a Task 7 usa só a captura do som geral (`WasapiLoopbackCapture`), que é o plano B da spec.
- [ ] **Step 3: Posições na barra.** Com UI Automation, a partir de `Shell_TrayWnd`, liste `AutomationId`, `Name` e `BoundingRectangle` dos filhos. Anote os IDs do botão Iniciar (esperado `StartButton`), do último ícone de app e do botão Widgets (esperado `WidgetsButton`). Faça isso com a barra centralizada, com os Widgets ligados (Configurações → Personalização → Barra de tarefas) e com a barra alinhada à esquerda. **Volte as configurações** para o estado original no fim: centralizada e Widgets desligados.
  - Liste também as janelas filhas da `Shell_TrayWnd` do processo `BarraBateria` (`EnumChildWindows` + `GetWindowThreadProcessId`), com o BB aberto.
- [ ] **Step 4: Commit** só da anotação: `git add .gitignore docs/superpowers/plans/2026-11-17-verificacoes.md && git commit -m "docs: resultados das verificações práticas"`

### Task 3: Escolher a sessão mostrada

**Files:**
- Create: `src/MiniplayerBarra/Core/MediaSnapshot.cs`, `src/MiniplayerBarra/Core/SourceClassifier.cs`, `src/MiniplayerBarra/Core/SessionPicker.cs`
- Test: `tests/MiniplayerBarra.Tests/SourceClassifierTests.cs`, `tests/MiniplayerBarra.Tests/SessionPickerTests.cs`

**Interfaces:**
- Consumes: `Settings`, `SourceKind` (Task 1).
- Produces:
  - `record MediaSnapshot(string SessionId, SourceKind Kind, string Title, string Artist, bool IsPlaying, byte[]? Cover)`. `SessionId` = AUMID; se houver repetição, acrescenta `#n`.
  - `static SourceKind SourceClassifier.Classify(string aumid, Settings s)`
  - `static MediaSnapshot? SessionPicker.Pick(IReadOnlyList<MediaSnapshot> sessions, IReadOnlyList<SourceKind> priority)`

- [ ] **Step 1: Write the failing tests:**
  - `Classify`: `"Chrome._crx_cinhimbnkkghhklpknlkffjgod"` → `YouTubeMusic`; `"Chrome"` → `ChromeTab`; `"Chrome._crx_outroapp"` → `Other`; `"5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App"` → `WhatsApp`; `"Spotify.exe"` → `Other`.
  - `Playing_beats_paused_regardless_of_priority`: YT Music pausado + aba tocando → aba.
  - `Among_playing_highest_priority_wins`: aba tocando + YT Music tocando + WhatsApp tocando → YT Music.
  - `Nothing_playing_shows_highest_priority`: YT Music pausado + aba pausada → YT Music.
  - `Other_is_always_last`: `Other` tocando + WhatsApp tocando → WhatsApp.
  - `Empty_list_returns_null`.
  - `Kind_missing_from_priority_ranks_before_Other`: priority `[WhatsApp]`, aba tocando + Other tocando → aba.
- [ ] **Step 2: Run** `dotnet test --filter "SourceClassifier|SessionPicker"`. Expected: FAIL.
- [ ] **Step 3: Implement.** Ordem: tocando antes de pausado. Depois, a posição na `priority`. Tipos fora da lista vêm depois dos listados, e `Other` fica sempre por último. Desempate pela ordem original da lista.
- [ ] **Step 4: Run** de novo. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -am "feat: escolha da mídia mostrada por prioridade"` (com `git add` dos arquivos novos).

### Task 4: Sessões de mídia do Windows e "Pausar tudo"

**Files:**
- Create: `src/MiniplayerBarra/Media/IMediaSessions.cs`, `src/MiniplayerBarra/Media/GsmtcMediaSessions.cs`, `src/MiniplayerBarra/Core/PauseAll.cs`
- Test: `tests/MiniplayerBarra.Tests/PauseAllTests.cs`, `tests/MiniplayerBarra.Tests/Fakes/FakeMediaSessions.cs`

**Interfaces:**
- Consumes: `MediaSnapshot`, `SourceClassifier`, `Settings`.
- Produces:
```csharp
public interface IMediaSessions : IDisposable
{
    event Action? Changed;                       // qualquer thread
    Task<IReadOnlyList<MediaSnapshot>> GetAsync();
    Task<bool> TogglePlayPauseAsync(string sessionId);   // false se a sessão sumiu
    Task<bool> NextAsync(string sessionId);
    Task<bool> PreviousAsync(string sessionId);
    Task<bool> PauseAsync(string sessionId);
}
static Task<int> PauseAll.RunAsync(IMediaSessions sessions, Func<TimeSpan, Task> delay, int maxRounds = 10)
```

`GsmtcMediaSessions.CreateAsync(Settings)` usa `GlobalSystemMediaTransportControlsSessionManager.RequestAsync()`. Ela assina `SessionsChanged` e, em cada sessão, `MediaPropertiesChanged` e `PlaybackInfoChanged`, refazendo as assinaturas quando a lista muda. Todos disparam `Changed`. A capa é lida do `Thumbnail` para `byte[]`, com limite de 2 MB. Ela fica em cache por `SessionId` + título, para não ler de novo a cada mudança de estado.

- [ ] **Step 1: Write the failing tests** com `FakeMediaSessions`. O fake guarda uma lista mutável e pode programar "pausar a sessão X faz a sessão Y aparecer tocando", simulando o Chrome:
  - `Pauses_every_playing_session`: 3 tocando → todas pausadas, retorna 3.
  - `Keeps_going_when_new_playing_session_appears`: pausar `Chrome` faz um novo `Chrome` tocar 2 vezes seguidas → no fim, nada tocando, retorna 3, `delay` chamado com 400 ms.
  - `Stops_after_max_rounds`: uma sessão que volta a tocar sempre → para depois de 10 rodadas, sem laço infinito.
  - `Nothing_playing_returns_zero_without_delay`.
- [ ] **Step 2: Run** `dotnet test --filter PauseAll`. Expected: FAIL.
- [ ] **Step 3: Implement** `PauseAll.RunAsync`: a cada rodada, `GetAsync`; se não houver nada tocando, termina; se houver, `PauseAsync` em cada uma e `delay(400 ms)`. Se a Task 2 mostrou outro comportamento necessário, siga a anotação. Depois, implemente `GsmtcMediaSessions`.
- [ ] **Step 4: Run** `dotnet test`. Expected: PASS. Verificação manual: um teste de console rápido (pode reaproveitar `.scratch`) lista as sessões por `GsmtcMediaSessions` e mostra título, artista, tipo e `Cover` não nulo para o YT Music.
- [ ] **Step 5: Commit** `git commit -m "feat: sessões de mídia do Windows e pausar tudo"` (com `git add` dos arquivos novos).

### Task 5: Cor da capa

**Files:**
- Create: `src/MiniplayerBarra/Ui/CoverArt.cs`
- Test: `tests/MiniplayerBarra.Tests/CoverArtTests.cs`

**Interfaces:**
- Produces: `static (Bitmap? Image, Color WaveColor) CoverArt.Decode(byte[]? bytes, Color fallbackAccent)`

Algoritmo da cor: reduza a imagem para 32×32. Ignore pixels com saturação < 0.35 ou brilho < 0.30 (HSV). Agrupe os restantes em 36 faixas de matiz, com peso = saturação × brilho. Escolha a faixa mais pesada e devolva a média RGB dela. Clareie até brilho ≥ 0.75, para ficar legível na barra escura. Se nenhum pixel passar no filtro (capa cinza ou preta), use `fallbackAccent`.

- [ ] **Step 1: Write the failing tests:**
  - `Red_cover_gives_reddish_color`: PNG 64×64 vermelho puro → `R > 200`, `G < 100`, `B < 100`.
  - `Mostly_gray_with_blue_patch_picks_blue`: 90% cinza + 10% azul vivo → `B` é o maior canal.
  - `Grayscale_cover_falls_back_to_accent`.
  - `Null_bytes_give_null_image_and_accent`.
  - `Invalid_cover_bytes_fall_back_to_accent`: `new byte[] {1,2,3}` → `Image` null, cor = accent, sem exceção.
- [ ] **Step 2: Run** `dotnet test --filter CoverArt`. Expected: FAIL.
- [ ] **Step 3: Implement** conforme o algoritmo acima.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat: cor das ondas a partir da capa"` (com `git add`).

### Task 6: Layout do player e lugar na barra

**Files:**
- Create: `src/MiniplayerBarra/Ui/PlayerLayout.cs`, `src/MiniplayerBarra/Ui/TaskbarSlot.cs`
- Test: `tests/MiniplayerBarra.Tests/PlayerLayoutTests.cs`, `tests/MiniplayerBarra.Tests/TaskbarSlotTests.cs`

**Interfaces:**
- Produces:
```csharp
enum PlayerHit { None, Previous, PlayPause, Next, OpenSource }
record PlayerLayout(Size Size, Rectangle Waves, Rectangle Previous, Rectangle PlayPause, Rectangle Next,
                    Rectangle Cover, Rectangle Title, Rectangle Artist, bool ShowText)
{
    static PlayerLayout Compute(Graphics g, string title, string artist, float scale, int maxWidth);
    static int MinWidth(float scale);         // ondas+botões + capa, sem texto
    PlayerHit HitTest(Point p);               // capa e texto → OpenSource
}
record TaskbarInfo(Rectangle Taskbar, bool Centered, Rectangle? Widgets, int AppsLeft, int AppsRight, int RightLimit);
static Rectangle TaskbarSlot.Place(TaskbarInfo info, int desiredWidth, int minWidth, int height, int margin);
```

Medidas a 100% (multiplicar por `scale`): altura 40. Bloco das ondas/botões com 96 de largura e três botões de 32×32 centralizados na vertical. Capa 36×36, 8 depois do bloco. Texto 8 depois da capa, até 260 de largura. Título em Segoe UI Semibold 10 pt, artista em Segoe UI 9 pt. `RightLimit` = esquerda do BB, se ele estiver aberto; senão, esquerda da área de notificação.

- [ ] **Step 1: Write the failing tests:**
  - `Long_title_is_truncated_to_available_width`: título de 200 caracteres, `maxWidth` 300 → `Size.Width <= 300`, `ShowText` = true.
  - `Below_min_width_hides_text_but_keeps_buttons_and_cover`: `maxWidth = MinWidth(1) - 10` → `ShowText` false, `Size.Width == MinWidth(1)`.
  - `Empty_artist_uses_single_line`: artista "" → `Artist` vazio e `Title` centralizado na vertical.
  - `HitTest_maps_regions`: centro de `Previous` → `Previous`; centro de `Cover` → `OpenSource`; ponto da área das ondas fora dos botões → `None`.
  - `Centered_no_widgets_sits_at_left_edge`: `Place` → `Left == Taskbar.Left + margin`, `Right <= AppsLeft - margin`.
  - `Centered_with_widgets_sits_right_of_widgets`: `Left == Widgets.Right + margin`.
  - `Left_aligned_sits_left_of_RightLimit`: `Right == RightLimit - margin`, `Left >= AppsRight + margin`.
  - `Too_narrow_space_still_returns_min_width`: espaço livre de 50 px → largura = `minWidth`, nunca 0.
  - `Vertically_centered_in_taskbar`.
- [ ] **Step 2: Run** `dotnet test --filter "PlayerLayout|TaskbarSlot"`. Expected: FAIL.
- [ ] **Step 3: Implement.** O texto é cortado com `TextRenderer.MeasureText` + `TextFormatFlags.EndEllipsis`.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat: layout do player e posição na barra"` (com `git add`).

### Task 7: Ondas sonoras

**Files:**
- Create: `src/MiniplayerBarra/Audio/IWaveSource.cs`, `src/MiniplayerBarra/Audio/SpectrumAnalyzer.cs`, `src/MiniplayerBarra/Audio/AudioMeter.cs`, `src/MiniplayerBarra/Audio/SourceProcess.cs`
- Test: `tests/MiniplayerBarra.Tests/SpectrumAnalyzerTests.cs`, `tests/MiniplayerBarra.Tests/SourceProcessTests.cs`

**Interfaces:**
- Consumes: `SourceKind`.
- Produces:
```csharp
sealed class SpectrumAnalyzer   // 12 bandas, thread-safe
{
    SpectrumAnalyzer(int sampleRate, int bands = 12);
    void Push(ReadOnlySpan<float> mono);     // thread de captura
    float[] Frame();                          // thread da Ui, 0..1, subida imediata, descida máx. 0.06/quadro
}
sealed class AudioMeter : IWaveSource, IDisposable   // IWaveSource definido na Task 9; crie a interface já aqui
{
    AudioMeter();                             // cria o SpectrumAnalyzer com o sample rate da captura (recria se mudar)
    void Start(SourceKind kind);              // reinicia se o tipo mudou
    void Stop();
    float[] Frame();                          // repassa ao analyzer; parado → 12 zeros
}
static string? SourceProcess.ProcessNameFor(SourceKind kind);   // YouTubeMusic/ChromeTab → "chrome", WhatsApp → "WhatsApp", Other → null
static int? SourceProcess.FindRootPid(string processName, IEnumerable<(int Pid, int ParentPid, string Name)> processes);
```

Análise: FFT de 2048 amostras (NAudio `FastFourierTransform`) com janela Hann. Bandas logarítmicas entre 60 Hz e 12 kHz. Nível em dB, normalizado entre −60 dB (0) e −10 dB (1).

Captura: use o caminho que a Task 2 anotou. Com processo conhecido, faça a captura por árvore de processos a partir de `FindRootPid`. Sem processo, ou se isso falhar, use `WasapiLoopbackCapture` (som geral) e registre no log. Se a captura parar com erro (ex.: dispositivo trocado), reinicie em 1 s. A cada nova tentativa, o tempo de espera dobra até no máximo 30 s.

- [ ] **Step 1: Write the failing tests:**
  - `Silence_gives_all_zero`.
  - `Sine_1kHz_peaks_in_band_containing_1kHz`: a banda que contém 1 kHz é a maior, e as bandas abaixo de 200 Hz ficam < 0.2.
  - `Decay_is_gradual`: depois de um tom forte, `Push` de silêncio → o próximo `Frame()` cai no máximo 0.06 por banda.
  - `FindRootPid_picks_chrome_whose_parent_is_not_chrome`: lista com o pai `explorer`(10) → `chrome`(20) → `chrome`(30) → retorna 20.
  - `ProcessNameFor` cobre os quatro tipos.
- [ ] **Step 2: Run** `dotnet test --filter "Spectrum|SourceProcess"`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS. Verificação manual com o YT Music tocando: `AudioMeter` + `Frame()` impresso em console → valores se mexem com a música; com a música pausada → caem a 0.
- [ ] **Step 5: Commit** `git commit -m "feat: ondas sonoras a partir do som da fonte"` (com `git add`).

### Task 8: Desenho e encaixe na barra

**Files:**
- Create: `src/MiniplayerBarra/Ui/IPlayerView.cs`, `src/MiniplayerBarra/Ui/PlayerRenderer.cs`, `src/MiniplayerBarra/Ui/TaskbarProbe.cs`, `src/MiniplayerBarra/Ui/TaskbarPlayer.cs`
- Test: `tests/MiniplayerBarra.Tests/PlayerRendererTests.cs`

**Interfaces:**
- Consumes: `PlayerLayout`, `TaskbarSlot`, `TaskbarInfo`, `Theme`, `CoverArt`, `MediaSnapshot`.
- Produces:
```csharp
record PlayerState(MediaSnapshot Media, Bitmap? Cover, Color WaveColor);
interface IPlayerView : IDisposable
{
    void Show(PlayerState? state);      // null = esconder
    void SetBands(float[] bands);       // só redesenha
    event Action<PlayerHit>? Clicked;   // Previous / PlayPause / Next / OpenSource
    event Action? PauseAllRequested;
    event Action? ExitRequested;
}
static class PlayerRenderer { static void Draw(Graphics g, PlayerLayout l, PlayerState s, float[] bands, Theme t, float scale); }
static class TaskbarProbe { static TaskbarInfo? Read(IntPtr trayHwnd); }
sealed class TaskbarPlayer : IPlayerView { static TaskbarPlayer? TryCreate(); }
```

`PlayerRenderer`: desenha as 12 barras no bloco das ondas, com cor `WaveColor` e um degradê da base, com alfa 0.9, até o topo, com alfa 0.35. Com a mídia pausada, as barras ficam em altura mínima e com alfa 0.25. Os botões ficam por cima, na cor do tema: ⏮ ⏸/▶ ⏭, desenhados com `Segoe Fluent Icons` (`E892`, `E769`/`E768`, `E893`) e, se essa fonte não existir, com `Segoe MDL2 Assets`. Os cantos da capa são arredondados em 4 px. Sem capa, usa uma nota (`E8D6`) num quadrado cinza translúcido.

`TaskbarProbe.Read`:
- `Centered` = valor de registro `TaskbarAl` ≠ 0 (ausente = centralizado).
- Widgets: `TaskbarDa` = 1 e retângulo do elemento UIA anotado na Task 2.
- `AppsLeft`/`AppsRight`: do botão Iniciar e do último ícone de app, por UIA.
- `RightLimit`: a esquerda da janela filha da `Shell_TrayWnd` que pertence ao processo `BarraBateria`, ou a esquerda de `TrayNotifyWnd`.
- Se a UIA falhar: `AppsLeft` = centro da barra − 250 × escala, e Widgets, se ligado, = 160 × escala a partir da esquerda. Registrar no log.

`TaskbarPlayer`: adapte `BB/src/BarraBateria/Ui/TaskbarBar.cs`, mantendo o encaixe, o `Listener` de `TaskbarCreated`, o `UpdateLayeredWindow`, o `MA_NOACTIVATE` e o tooltip manual. As diferenças:
- Posição por `TaskbarProbe` + `TaskbarSlot`, em vez de `TaskbarLayout`.
- Checagem de mudança a cada 3 s comparando todo o `TaskbarInfo` (inclui o BB abrir ou fechar e os ícones de apps crescerem).
- Clique esquerdo → `Clicked(layout.HitTest(p))` quando não for `None`.
- Tooltip com "Título — Artista" completo sobre a capa ou o texto.
- Menu com "Pausar tudo" e "Sair".
- `Show(null)` esconde a janela com `SW_HIDE` e para o timer de 3 s até o próximo `Show`.
- Não existe plano B na bandeja: se `TryCreate` falhar, o `Program` registra no log e tenta de novo a cada 10 s.

- [ ] **Step 1: Write the failing tests** em `PlayerRendererTests`:
  - `Draws_without_throwing_for_all_states`: tocando e pausado, com e sem capa, com e sem texto.
  - `Playing_bars_are_tinted_with_wave_color`: bandas todas 1.0 e `WaveColor` vermelho → um pixel no bloco das ondas, fora dos botões, tem `R` dominante e alfa > 0.
  - `Paused_bars_are_faint`: o mesmo pixel tem alfa < 0.3 × 255.
- [ ] **Step 2: Run** `dotnet test --filter PlayerRenderer`. Expected: FAIL.
- [ ] **Step 3: Implement** `PlayerRenderer`, `TaskbarProbe` e `TaskbarPlayer`.
- [ ] **Step 4: Run** `dotnet test`. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "feat: player desenhado e encaixado na barra de tarefas"` (com `git add`).

### Task 9: Controlador e programa

**Files:**
- Create: `src/MiniplayerBarra/AppController.cs`, `src/MiniplayerBarra/Program.cs`
- Test: `tests/MiniplayerBarra.Tests/AppControllerTests.cs`, `tests/MiniplayerBarra.Tests/Fakes/FakePlayerView.cs`

**Interfaces:**
- Consumes: `IMediaSessions`, `SessionPicker`, `PauseAll`, `CoverArt`, `IPlayerView`, `AudioMeter`/`SpectrumAnalyzer`, `Settings`, `Theme`.
- Produces: `sealed class AppController : IDisposable { AppController(IMediaSessions s, IPlayerView v, Settings cfg, Func<Color> accent, IWaveSource? waves); Task StartAsync(); }`, usando `interface IWaveSource { void Start(SourceKind k); void Stop(); float[] Frame(); }` (criada na Task 7, em `src/MiniplayerBarra/Audio/IWaveSource.cs`).

Comportamento:
- Em cada `Changed`, faça `GetAsync` → `Pick` → `view.Show`.
- Um contador de geração descarta respostas antigas: só a mais recente é aplicada.
- O `Decode` da capa só roda quando título ou capa mudam.
- Enquanto a mídia mostrada está tocando e `Waves` é true: `waves.Start(kind)` e um timer de 33 ms chama `view.SetBands(waves.Frame())`. Em qualquer outro caso: `Stop()` e o timer para.
- Os cliques chamam os métodos de `IMediaSessions` com o `SessionId` mostrado e, em seguida, um refresh.
- "Pausar tudo" chama `PauseAll.RunAsync(s, Task.Delay)`.
- "Abrir fonte" leva para a frente uma janela de nível superior visível:
  - YT Music: título contém `"YouTube Music"`.
  - Aba do Chrome: processo `chrome` cujo título contém o título da mídia.
  - WhatsApp: processo `WhatsApp`.
  - Outros: nada.
  - Use `AllowSetForegroundWindow`/`SetForegroundWindow` e restaure se estiver minimizada.

`Program.Main`: copie a estrutura do BB, com `SingleInstance("MiniplayerBarra")`, `Log.FilePath`, `Settings.Load`, `StartupRegistration.Ensure` (só quando o exe se chama `MiniplayerBarra.exe`), `TaskbarPlayer.TryCreate` com novas tentativas e `GsmtcMediaSessions.CreateAsync`. Sem modo `--diagnostico`.

- [ ] **Step 1: Write the failing tests** com `FakeMediaSessions` (Task 4) e `FakePlayerView`:
  - `Shows_picked_session_on_start`: YT Music pausado + aba pausada → `Show` com YT Music.
  - `Hides_when_no_sessions`: lista vazia → `Show(null)`.
  - `PlayPause_click_targets_shown_session`.
  - `Command_on_vanished_session_is_ignored`: a sessão some, depois chega o clique → `TogglePlayPauseAsync` retorna false, sem exceção, e o próximo `Show` reflete a lista nova.
  - `Burst_of_changes_shows_latest_snapshot`: 5 `Changed` seguidos, em que o fake responde o 1º por último → o último `Show` é o da 5ª lista.
  - `Waves_start_only_while_playing`: tocando → `Start(YouTubeMusic)`; pausado → `Stop()`; `Waves=false` → `Start` nunca é chamado.
  - `PauseAll_menu_pauses_everything`.
- [ ] **Step 2: Run** `dotnet test --filter AppController`. Expected: FAIL.
- [ ] **Step 3: Implement** `AppController` e `Program`.
- [ ] **Step 4: Run** `dotnet test`. Expected: tudo PASS. Rode `dotnet run --project src/MiniplayerBarra` e confira que o player aparece no canto esquerdo com a música atual do YT Music.
- [ ] **Step 5: Commit** `git commit -m "feat: app completo"` (com `git add`).

### Task 10: Publicação local, documentação e conferência final

**Files:**
- Create: `publicar.ps1` (adaptar do BB: processo `MiniplayerBarra`, destino `Programs\MiniplayerBarra`, `src/MiniplayerBarra`), `README.md`, `docs/COMO-INSTALAR.md`, `docs/COMO-FUNCIONA.md`, `LICENSE` (MIT, "Copyright (c) 2026 athaisfreitas"), `docs/imagens/player.png`

- [ ] **Step 1:** Rode `powershell -ExecutionPolicy Bypass -File .\publicar.ps1`. Expected: testes passam, o app é instalado e abre, e o valor `MiniplayerBarra` existe na chave `Run`.
- [ ] **Step 2: Conferência manual**. Cada item precisa dar certo:
  1. O YT Music tocando aparece com capa, título, artista e barras coloridas se mexendo.
  2. Pausar, avançar e voltar funcionam sem tirar o foco da janela atual (testar digitando no Bloco de Notas).
  3. Com o YT Music pausado, tocar um vídeo numa aba do Chrome faz o player mostrar o vídeo. Pausar o vídeo faz o player voltar ao YT Music pausado.
  4. Três abas tocando + "Pausar tudo" → tudo para.
  5. Clicar na capa traz o YT Music para a frente.
  6. Ligar e desligar os Widgets, e alinhar a barra à esquerda e voltar: o player se move como na spec (seção 6) e nunca some. **Volte** as configurações originais no fim.
  7. Com o BB aberto: os dois aparecem e nada se sobrepõe. Reinicie o Explorer (`Stop-Process -Name explorer`): os dois reencaixam.
  8. Desconectar e reconectar o fone com a música tocando: as barras voltam (Review Focus 5).
  9. Com tudo pausado por 1 minuto, o Gerenciador de Tarefas mostra ≈ 0% de CPU e menos de 80 MB de memória. Tocando, mostra cerca de 1% de CPU.
- [ ] **Step 3:** Tire a captura `docs/imagens/player.png` só do player. Nela não pode aparecer nada pessoal além da música.
- [ ] **Step 4:** Escreva a documentação em português, conforme a spec seção 11:
  - README com: o que é, imagem, botões, regra de prioridade, "Pausar tudo", onde o player aparece em cada configuração da barra, instalar, configurar (`config.json` com os campos explicados), onde fica cada coisa, desinstalar (fechar o app, apagar a pasta e o valor `Run`), e perguntas comuns ("É seguro?", "Por que não aparece?", "Funciona com Spotify?", que mostra como "outros").
  - `COMO-INSTALAR.md`: passo a passo do zero (instalar o .NET 8 SDK, baixar o ZIP ou fazer `git clone`, rodar `publicar.ps1`).
  - `COMO-FUNCIONA.md`: GSMTC, captura por processo, encaixe na barra, UIA, e limitações conhecidas (encaixe não oficial, abas do Chrome).
- [ ] **Step 5: Varredura de privacidade.** Rode `git grep -n -i -E "<nome pessoal>|<usuário do Windows>|gmail|C:\\\\Users"` (trocando os marcadores pelo nome pessoal e pelo usuário do Windows de quem desenvolve) e confira com `git log --format='%an %ae%n%b'` que não aparece nenhum coautor nem e-mail pessoal. Expected: nenhum resultado, exceto o nome de usuário `athaisfreitas` e o e-mail noreply.
- [ ] **Step 6: Commit** `git add -A && git commit -m "docs: README, guias, licença e script de publicação"`

### Task 11: Publicar no GitHub

- [ ] **Step 1:** Mostre à autora o resultado da varredura da Task 10 e **peça confirmação explícita** para criar o repositório público `athaisfreitas/miniplayer-barra`.
- [ ] **Step 2:** Crie o repositório público vazio (sem README, licença ou .gitignore). O `gh` não está instalado: ou a autora cria em github.com/new, ou se instala o GitHub CLI (`winget install GitHub.cli`) e ela faz `gh auth login` uma vez.
- [ ] **Step 3:** `git remote add origin https://github.com/athaisfreitas/miniplayer-barra.git && git branch -M main && git push -u origin main`. Expected: push aceito. Abra a página do repositório e confira que o README aparece com a imagem e que a lista de colaboradores tem só a autora.
