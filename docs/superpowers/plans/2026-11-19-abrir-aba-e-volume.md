# Abrir a aba certa + volume do app: plano de implementação

> Os passos usam caixas de seleção (`- [ ]`) para acompanhar o progresso. Marque cada uma ao terminar.

**Goal:**
1. Clicar na capa ou no texto do player abre **a aba exata** do Chrome que está tocando, mesmo que ela esteja em segundo plano numa janela cheia de abas.
2. Girar a rodinha do mouse em cima do player sobe/desce o volume **só do app que está tocando** (como o controle por app do Mixer de Volume do Windows), sem mexer no volume geral.

**Architecture:**
- **Abrir a aba:** já existe `Ui/SourceWindow.cs`, mas ele só acha a janela do Chrome cujo **título** contém o título da música. O título da janela é o da aba ativa, então quando a aba que toca está em segundo plano, não acha nada. A correção: se o caminho rápido falhar, procurar a aba pela **UI Automation** (a mesma API que `Ui/TaskbarProbe.cs` já usa), selecionar a aba (`SelectionItemPattern.Select()`) e trazer a janela dela para a frente. Isso roda fora da thread da interface (leva ~0,2–0,5 s).
- **Volume:** a API de sessões de áudio do Windows (Core Audio, via NAudio, que já está no projeto) dá um volume por processo: `AudioSessionControl.SimpleAudioVolume.Volume`. É o mesmo controle do Mixer de Volume. A rodinha do mouse no player gera "degraus" de 5%; o `AppController` lê o volume atual, calcula o novo e aplica. O player mostra `Volume 40%` no lugar do artista por 1,5 s.

**Tech Stack:** C# 12, .NET 8, WinForms, `System.Windows.Automation` (já referenciado via `UseWPF`), NAudio 2.4.0, xUnit.

Todos os caminhos são relativos à raiz do repositório. Código, nomes e commits em inglês; comentários e logs seguem o estilo atual do projeto (português).

---

## Fatos já verificados (2026-11-19, no PC da usuária)

Não precisa verificar de novo; está aqui para você não errar.

- Chrome 157, Windows em pt-BR, interface do Chrome em inglês.
- UI Automation: as janelas do Chrome são `ClassName == "Chrome_WidgetWin_1"` filhas da raiz. **Atenção:** o VS Code e outros apps Electron usam a mesma classe → filtre pelo processo `chrome`.
- As abas da faixa de abas são `ControlType.TabItem` com **`ClassName == "TabView"`**. Sem esse filtro, aparecem também "abas" de dentro das páginas (ex.: filtros do WhatsApp Web).
- Toda `TabView` suporta `SelectionItemPattern` (para selecionar) e `ScrollItemPattern`.
- O nome da aba é o título da página + sufixos do Chrome, ex.: `Obsidian in 24 Minutes - YouTube`, `... - Part of group Work`, `... - Inactive tab - 134 MB freed up`, e `... - Audio playing` quando está tocando (visto em 2026-11-17, `docs/superpowers/plans/2026-11-17-verificacoes.md`).
- Buscar todas as `TabView` em 8 janelas / 37 abas levou ~230–490 ms.
- O Chrome expõe **uma única** sessão de áudio no Windows para todas as abas **e** para o YouTube Music instalado como app (é tudo o mesmo `chrome.exe`). Então o volume do Chrome vale para todas as abas e o YT Music juntos. Isso é limitação do Windows/Chrome, não bug; documentar.
- Baseline: `dotnet test` → 121 testes aprovados.

---

## Task 1: Lógica pura para escolher a aba

**Files:**
- Create: `src/MiniplayerBarra/Ui/ChromeTabs.cs`
- Create: `tests/MiniplayerBarra.Tests/ChromeTabsTests.cs`

- [ ] **Step 1: Escrever os testes (devem falhar)**

```csharp
using MiniplayerBarra.Ui;
using Xunit;

namespace MiniplayerBarra.Tests;

public class ChromeTabsTests
{
    [Fact]
    public void Picks_tab_containing_title()
    {
        var tabs = new[] { "Inbox - Gmail", "Obsidian in 24 Minutes - YouTube", "Home / X" };
        Assert.Equal(1, ChromeTabs.Pick(tabs, "Obsidian in 24 Minutes"));
    }

    [Fact]
    public void Prefers_playing_tab_when_title_repeats()
    {
        var tabs = new[] { "Song A - YouTube", "Song A - YouTube - Audio playing" };
        Assert.Equal(1, ChromeTabs.Pick(tabs, "Song A"));
    }

    [Fact]
    public void Falls_back_to_single_playing_tab_when_title_not_found()
    {
        var tabs = new[] { "Inbox - Gmail", "Some radio - Audio playing" };
        Assert.Equal(1, ChromeTabs.Pick(tabs, "Different title"));
    }

    [Fact]
    public void Returns_null_when_ambiguous_or_missing()
    {
        Assert.Null(ChromeTabs.Pick(new[] { "A - Audio playing", "B - Audio playing" }, "zzz"));
        Assert.Null(ChromeTabs.Pick(new[] { "A", "B" }, "zzz"));
        Assert.Null(ChromeTabs.Pick(Array.Empty<string>(), "A"));
    }

    [Fact]
    public void Matching_ignores_case()
    {
        Assert.Equal(0, ChromeTabs.Pick(new[] { "SONG A - YouTube" }, "song a"));
    }

    [Fact]
    public void Empty_title_only_uses_playing_fallback()
    {
        Assert.Equal(1, ChromeTabs.Pick(new[] { "A", "B - Audio playing" }, ""));
    }

    [Theory]
    [InlineData(MiniplayerBarra.Core.SourceKind.YouTubeMusic, "Any", "YouTube Music")]
    [InlineData(MiniplayerBarra.Core.SourceKind.ChromeTab, "My Song", "My Song")]
    public void Needle_depends_on_kind(MiniplayerBarra.Core.SourceKind kind, string title, string expected) =>
        Assert.Equal(expected, ChromeTabs.NeedleFor(kind, title));
}
```

- [ ] **Step 2: Rodar e ver falhar:** `dotnet test --filter ChromeTabsTests` → erro de compilação (classe não existe).

- [ ] **Step 3: Implementar só a parte pura em `Ui/ChromeTabs.cs`**

```csharp
using MiniplayerBarra.Core;

namespace MiniplayerBarra.Ui;

/// <summary>Acha e seleciona, pela UI Automation, a aba do Chrome que está tocando.</summary>
internal static class ChromeTabs
{
    /// <summary>Sufixos que o Chrome põe no nome da aba que toca (inglês e português).</summary>
    private static readonly string[] AudioMarkers = { "Audio playing", "Reproduzindo áudio", "Áudio em reprodução" };

    /// <summary>YT Music: aba com "YouTube Music" no nome. Aba comum: o título da mídia.</summary>
    public static string NeedleFor(SourceKind kind, string mediaTitle) =>
        kind == SourceKind.YouTubeMusic ? "YouTube Music" : mediaTitle;

    /// <summary>
    /// Índice da aba a abrir: 1) abas cujo nome contém <paramref name="needle"/>, preferindo a que toca;
    /// 2) se nenhuma, a única aba que toca; 3) senão null.
    /// </summary>
    public static int? Pick(IReadOnlyList<string> tabNames, string needle)
    {
        int? firstByTitle = null;
        int? playingByTitle = null;
        int? singlePlaying = null;
        int playingCount = 0;
        for (int i = 0; i < tabNames.Count; i++)
        {
            var name = tabNames[i];
            bool playing = IsPlaying(name);
            if (playing) { playingCount++; singlePlaying = i; }
            if (string.IsNullOrWhiteSpace(needle) || !name.Contains(needle, StringComparison.OrdinalIgnoreCase)) continue;
            firstByTitle ??= i;
            if (playing) playingByTitle ??= i;
        }
        if (firstByTitle is not null) return playingByTitle ?? firstByTitle;
        return playingCount == 1 ? singlePlaying : null;
    }

    private static bool IsPlaying(string name)
    {
        foreach (var m in AudioMarkers)
            if (name.Contains(m, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
```

- [ ] **Step 4:** `dotnet test --filter ChromeTabsTests` → todos passam.
- [ ] **Step 5: Commit** `feat: pick the Chrome tab that matches the playing media`

---

## Task 2: Selecionar a aba pela UI Automation e ligar no "Abrir fonte"

**Files:**
- Modify: `src/MiniplayerBarra/Ui/ChromeTabs.cs` (adicionar `TrySelect`)
- Modify: `src/MiniplayerBarra/Ui/SourceWindow.cs` (`Activate`)
- Modify: `src/MiniplayerBarra/AppController.cs` (`OnClicked`: rodar `_openSource` fora da thread da interface)
- Modify: `tests/MiniplayerBarra.Tests/AppControllerTests.cs` (o teste `OpenSource_click_opens_shown_media` passa a esperar)

- [ ] **Step 1: `ChromeTabs.TrySelect`** (adicione `using System.Diagnostics;` e `using System.Windows.Automation;`; veja `Ui/TaskbarProbe.cs` para o estilo de uso da UIA)

```csharp
/// <summary>
/// Seleciona a aba escolhida por <see cref="Pick"/> entre todas as janelas do Chrome.
/// Devolve o HWND da janela da aba, ou IntPtr.Zero. Lento (centenas de ms): não chame na thread da interface.
/// </summary>
public static IntPtr TrySelect(string needle)
{
    var windowCond = new PropertyCondition(AutomationElement.ClassNameProperty, "Chrome_WidgetWin_1");
    var tabCond = new AndCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem),
        new PropertyCondition(AutomationElement.ClassNameProperty, "TabView"));

    var names = new List<string>();
    var found = new List<(AutomationElement Window, AutomationElement Tab)>();
    foreach (AutomationElement win in AutomationElement.RootElement.FindAll(TreeScope.Children, windowCond))
    {
        if (!IsChrome(win.Current.ProcessId)) continue; // VS Code/Electron usam a mesma classe
        foreach (AutomationElement tab in win.FindAll(TreeScope.Descendants, tabCond))
        {
            names.Add(tab.Current.Name ?? "");
            found.Add((win, tab));
        }
    }

    var index = Pick(names, needle);
    if (index is null)
    {
        Log.Info($"Abrir fonte: nenhuma aba do Chrome casa com \"{needle}\" ({names.Count} abas)");
        return IntPtr.Zero;
    }
    var (window, chosen) = found[index.Value];
    if (chosen.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var p))
        ((SelectionItemPattern)p).Select();
    return new IntPtr(window.Current.NativeWindowHandle);
}

private static bool IsChrome(int pid)
{
    try { using var p = Process.GetProcessById(pid); return p.ProcessName.Equals("chrome", StringComparison.OrdinalIgnoreCase); }
    catch (Exception) { return false; }
}
```

Envolva o corpo em `try/catch (ElementNotAvailableException)` devolvendo `IntPtr.Zero` (uma aba pode fechar no meio da busca).

- [ ] **Step 2: `SourceWindow.Activate`** — troque o começo para usar a UIA quando o caminho rápido falhar:

```csharp
var hwnd = Find(media); // rápido: a aba já é a ativa, ou é o app do YT Music, ou WhatsApp
if (hwnd == IntPtr.Zero && media.Kind is SourceKind.ChromeTab or SourceKind.YouTubeMusic)
    hwnd = ChromeTabs.TrySelect(ChromeTabs.NeedleFor(media.Kind, media.Title ?? ""));
if (hwnd == IntPtr.Zero) { /* log atual + return false */ }
```

O resto (`IsIconic` → `ShowWindow(SW_RESTORE)`, `AllowSetForegroundWindow`, `SetForegroundWindow`) continua igual. Atualize o `<summary>` do `Matches`/classe dizendo que, se o título da janela não bater, procura a aba pela UIA.

- [ ] **Step 3: `AppController.OnClicked`** — no ramo `PlayerHit.OpenSource`, rode em segundo plano para o player não travar meio segundo:

```csharp
else if (hit == PlayerHit.OpenSource)
{
    _ = Task.Run(() =>
    {
        try { _openSource(media); }
        catch (Exception ex) { Log.Error("Abrir fonte", ex); }
    });
}
```

(`SetForegroundWindow` vale por processo — quem recebeu o último clique —, então funciona de uma thread do pool.)

- [ ] **Step 4: Ajustar o teste** `OpenSource_click_opens_shown_media` em `AppControllerTests.cs`: depois de `r.View.Click(PlayerHit.OpenSource);`, espere com `Assert.True(SpinWait.SpinUntil(() => r.Opened.Count == 1, 2000));` antes de conferir. Troque `Opened` no `Rig` para algo seguro entre threads se o teste acusar problema (ex.: `lock` no `Add`).

- [ ] **Step 5:** `dotnet build` sem avisos novos e `dotnet test` → tudo passa.
- [ ] **Step 6: Commit** `feat: open the exact Chrome tab that is playing via UI Automation`

---

## Task 3: Volume por app (Core Audio)

**Files:**
- Create: `src/MiniplayerBarra/Core/VolumeStep.cs`
- Create: `src/MiniplayerBarra/Audio/AppVolume.cs`
- Modify: `src/MiniplayerBarra/Audio/SourceProcess.cs` (expor a comparação de nome)
- Create: `tests/MiniplayerBarra.Tests/VolumeStepTests.cs`

- [ ] **Step 1: Testes do degrau (devem falhar)**

```csharp
using MiniplayerBarra.Core;
using Xunit;

namespace MiniplayerBarra.Tests;

public class VolumeStepTests
{
    [Theory]
    [InlineData(0.50f, 1, 0.55f)]
    [InlineData(0.50f, -1, 0.45f)]
    [InlineData(0.50f, 2, 0.60f)]
    [InlineData(1.00f, 1, 1.00f)]
    [InlineData(0.00f, -1, 0.00f)]
    [InlineData(0.37f, 1, 0.40f)]   // volume fora da grade: encaixa em 5% e anda
    [InlineData(0.42f, -1, 0.35f)]
    public void Apply_moves_in_5_percent_steps(float current, int notches, float expected) =>
        Assert.Equal(expected, VolumeStep.Apply(current, notches), 3);

    [Theory]
    [InlineData(0.55f, 55)]
    [InlineData(0f, 0)]
    [InlineData(1f, 100)]
    public void Percent_rounds(float v, int expected) => Assert.Equal(expected, VolumeStep.Percent(v));
}
```

- [ ] **Step 2: Implementar `Core/VolumeStep.cs`**

```csharp
namespace MiniplayerBarra.Core;

/// <summary>Volume em degraus de 5% (20 degraus de 0 a 1).</summary>
public static class VolumeStep
{
    private const int Steps = 20;

    /// <param name="notches">Cliques da rodinha: positivo sobe, negativo desce.</param>
    public static float Apply(float current, int notches)
    {
        int step = (int)MathF.Round(Math.Clamp(current, 0f, 1f) * Steps) + notches;
        return Math.Clamp(step, 0, Steps) / (float)Steps;
    }

    public static int Percent(float volume) => (int)MathF.Round(volume * 100f);
}
```

Confira os casos 0.37→0.40 e 0.42→0.35 (Round(7.4)=7→8; Round(8.4)=8→7). Rode `dotnet test --filter VolumeStepTests`.

- [ ] **Step 3: `SourceProcess`** — torne pública a comparação de nome para reutilizar (sem mudar o comportamento):

```csharp
/// <summary>"chrome" casa com chrome.exe; "WhatsApp" casa com WhatsApp.exe e WhatsApp.Root.exe.</summary>
public static bool SameName(string exeName, string processName)
```
(só troque `private` por `public` e mantenha o comentário.)

- [ ] **Step 4: `Audio/AppVolume.cs`**

```csharp
using System.Diagnostics;
using MiniplayerBarra.Core;
using NAudio.CoreAudioApi;

namespace MiniplayerBarra.Audio;

/// <summary>Volume do app da fonte no Mixer de Volume do Windows (não mexe no volume geral).</summary>
public interface IAppVolume
{
    /// <summary>Volume atual (0..1) do app desse tipo de fonte, ou null se não há sessão de áudio dele.</summary>
    float? Get(SourceKind kind);

    /// <summary>Aplica em todas as sessões de áudio do app. False se não achou nenhuma.</summary>
    bool Set(SourceKind kind, float volume);
}

/// <summary>
/// Usa as sessões de áudio do dispositivo de saída padrão. O Chrome tem uma sessão só para todas
/// as abas e o app do YT Music, então o volume vale para todos eles juntos.
/// </summary>
internal sealed class AppVolume : IAppVolume
{
    public float? Get(SourceKind kind)
    {
        float? max = null;
        ForEachSession(kind, v => max = Math.Max(max ?? 0f, v.Volume));
        return max;
    }

    public bool Set(SourceKind kind, float volume)
    {
        int n = 0;
        ForEachSession(kind, v => { v.Volume = volume; n++; });
        return n > 0;
    }

    private static void ForEachSession(SourceKind kind, Action<SimpleAudioVolume> action)
    {
        var name = SourceProcess.ProcessNameFor(kind);
        if (name is null) return;
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var manager = device.AudioSessionManager;
        manager.RefreshSessions();
        var sessions = manager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            if (s.IsSystemSoundsSession) continue;
            string exe;
            try { using var p = Process.GetProcessById((int)s.GetProcessID); exe = p.ProcessName; }
            catch (Exception) { continue; }
            if (SourceProcess.SameName(exe, name)) action(s.SimpleAudioVolume);
        }
    }
}
```

Se algum membro do NAudio 2.4.0 tiver nome diferente (ex.: `GetProcessID`, `IsSystemSoundsSession`, `SimpleAudioVolume.Volume`), ajuste pelo IntelliSense/erro de build; a ideia é: dispositivo padrão → sessões → filtrar pelo processo → `SimpleAudioVolume.Volume`.

- [ ] **Step 5:** `dotnet build` e `dotnet test` → passam.
- [ ] **Step 6: Commit** `feat: per-app volume through Windows audio sessions`

---

## Task 4: Rodinha do mouse → AppController → mostrar o volume

**Files:**
- Modify: `src/MiniplayerBarra/Ui/IPlayerView.cs`
- Modify: `src/MiniplayerBarra/AppController.cs`
- Modify: `src/MiniplayerBarra/Program.cs` (linha do `new AppController(...)`)
- Modify: `tests/MiniplayerBarra.Tests/Fakes/FakePlayerView.cs`
- Modify: `tests/MiniplayerBarra.Tests/AppControllerTests.cs`

- [ ] **Step 1: Contrato da view** em `IPlayerView`:

```csharp
/// <summary>Rodinha do mouse sobre o player, em cliques inteiros: positivo sobe, negativo desce.</summary>
event Action<int>? VolumeWheel;

/// <summary>Mostra "Volume N%" por um instante (no lugar do artista).</summary>
void ShowVolume(int percent);
```

- [ ] **Step 2: Fakes** em `FakePlayerView`: `public List<int> Volumes { get; } = new();`, `public void ShowVolume(int percent) => Volumes.Add(percent);`, o evento `VolumeWheel` e `public void Wheel(int notches) => VolumeWheel?.Invoke(notches);`. No mesmo arquivo, crie:

```csharp
internal sealed class FakeAppVolume : IAppVolume
{
    public float? Current { get; set; } = 0.5f;
    public List<(SourceKind Kind, float Volume)> Sets { get; } = new();
    public float? Get(SourceKind kind) => Current;
    public bool Set(SourceKind kind, float volume) { Sets.Add((kind, volume)); Current = volume; return true; }
}
```

- [ ] **Step 3: Testes no `AppControllerTests`** (devem falhar). Acrescente `public FakeAppVolume Volume { get; } = new();` no `Rig` e passe `volume: Volume` no construtor. Use o padrão dos testes existentes para pôr uma mídia na tela (ex.: o que o `OpenSource_click_opens_shown_media` faz antes do clique).

```csharp
[Fact]
public async Task Wheel_up_raises_volume_of_shown_source_and_shows_it()
{
    // ... mostrar uma mídia ChromeTab, como nos outros testes ...
    r.View.Wheel(1);
    Assert.Equal((SourceKind.ChromeTab, 0.55f), r.Volume.Sets.Single());
    Assert.Equal(55, r.View.Volumes.Single());
}

[Fact]
public async Task Wheel_does_nothing_without_audio_session()
{
    // ... mostrar uma mídia ...
    r.Volume.Current = null;
    r.View.Wheel(-1);
    Assert.Empty(r.Volume.Sets);
    Assert.Empty(r.View.Volumes);
}

[Fact]
public void Wheel_without_media_does_nothing()
{
    using var r = new Rig();
    r.View.Wheel(1);
    Assert.Empty(r.Volume.Sets);
}
```

- [ ] **Step 4: `AppController`**
  - Novo parâmetro opcional no fim do construtor: `IAppVolume? volume = null` → campo `private readonly IAppVolume? _volume;`.
  - `StartAsync`: `_view.VolumeWheel += OnVolumeWheel;` · `Dispose`: `-=`.
  - Handler (roda na thread da interface; enumerar sessões leva poucos ms):

```csharp
private void OnVolumeWheel(int notches)
{
    var media = _shown;
    if (_disposed || media is null || _volume is null || notches == 0) return;
    try
    {
        var current = _volume.Get(media.Kind);
        if (current is null)
        {
            Log.Info($"Volume: nenhuma sessão de áudio de {media.Kind}");
            return;
        }
        var next = VolumeStep.Apply(current.Value, notches);
        if (_volume.Set(media.Kind, next)) _view.ShowVolume(VolumeStep.Percent(next));
    }
    catch (Exception ex)
    {
        Log.Error("Volume", ex);
    }
}
```

  - `Program.cs`: `new AppController(_sessions, _view, _settings, () => Theme.Current().Accent, _meter, volume: new AppVolume());`

- [ ] **Step 5:** `dotnet test` → passa (o `TaskbarPlayer` ainda não compila sem o Task 5? Ele implementa `IPlayerView`, então faça os Steps 1–2 do Task 5 junto se o build reclamar).
- [ ] **Step 6: Commit** `feat: mouse wheel over the player changes the source app volume`

---

## Task 5: Rodinha e aviso de volume no `TaskbarPlayer`

**Files:**
- Modify: `src/MiniplayerBarra/Ui/TaskbarPlayer.cs`

- [ ] **Step 1: Evento e acumulador.** Na classe `TaskbarPlayer`: `public event Action<int>? VolumeWheel;` e `internal void OnWheel(int notches) => VolumeWheel?.Invoke(notches);`. Na `EmbeddedForm`, um campo `private int _wheelAcc;` e:

```csharp
protected override void OnMouseWheel(MouseEventArgs e)
{
    base.OnMouseWheel(e);
    // Touchpad manda deltas pequenos: acumula até um clique inteiro (120).
    _wheelAcc += e.Delta;
    int notches = _wheelAcc / 120;
    if (notches == 0) return;
    _wheelAcc -= notches * 120;
    _owner.OnWheel(notches);
}
```
Zere `_wheelAcc` no `OnMouseLeave`.

- [ ] **Step 2: Mostrar o volume.** Campos `private string? _volumeText;` e um `System.Windows.Forms.Timer _volumeTimer` de 1500 ms (crie no construtor como o `_check`, descarte no `Dispose`). No `Tick`: parar o timer, `_volumeText = null`, recalcular layout e redesenhar.

```csharp
public void ShowVolume(int percent)
{
    if (_disposed || _state is null) return;
    _volumeText = $"Volume {percent}%";
    _volumeTimer.Stop();
    _volumeTimer.Start();
    // recalcular e redesenhar pelo mesmo caminho que o Show usa (Relayout + desenho)
}
```

  - Em `Relayout()`: `string artist = _volumeText ?? state.Media.Artist ?? "";`
  - No método que chama `PlayerRenderer.Draw` (perto da linha 244): passe `_volumeText is null ? state : state with { Media = state.Media with { Artist = _volumeText } }`. (`PlayerState` e `MediaSnapshot` são `record`, então `with` funciona; o cache do renderer já usa o artista na chave.)

- [ ] **Step 3: Dica no tooltip.** Em `TooltipAt`, acrescente uma segunda linha ao texto: `"\nClique: abrir · Rodinha: volume"`.

- [ ] **Step 4: Verificar se a rodinha chega na janela.** O player é janela filha da barra de tarefas (outro processo). Com "Rolar janelas inativas ao passar o mouse" (padrão do Windows 11) deve chegar. Rode o app (`dotnet run --project src/MiniplayerBarra`), toque algo e gire a rodinha sobre o player; confira no log (`Core/Log.cs` diz onde fica) ou na tela.
  - **Se NÃO chegar:** instale um hook de mouse de baixo nível (`SetWindowsHookEx(WH_MOUSE_LL=14, ...)` com `GetModuleHandle(null)`) só enquanto o mouse está sobre o player (instale no `OnMouseMove` se ainda não houver, remova no `OnMouseLeave` e no `Dispose`). No callback, para `WM_MOUSEWHEEL` (0x020A), leia `mouseData >> 16` (short) como delta, chame a mesma lógica do Step 1 via `BeginInvoke` e devolva `1` (engolir) — senão `CallNextHookEx`. Guarde o delegate num campo para o GC não coletar.

- [ ] **Step 5:** `dotnet build`, `dotnet test`.
- [ ] **Step 6: Commit** `feat: show volume feedback and handle wheel on the taskbar player`

---

## Task 6: Teste manual e documentação

- [ ] **Step 1: Build de release + rodar** (o app é desktop, não tem preview web): pare a instância que estiver rodando, `dotnet build -c Release` e rode o executável gerado (ou use `publicar.ps1` se for o fluxo de instalação; leia o script antes).
- [ ] **Step 2: Roteiro manual** (anote o resultado de cada item no fim deste arquivo):
  1. 3+ janelas do Chrome com várias abas; um vídeo do YouTube tocando numa aba **em segundo plano** de uma janela que **não** está na frente → clicar na capa → a janela vem para a frente **com a aba certa**.
  2. Mesmo teste com a janela **minimizada**. (Se a UIA não achar abas em janela minimizada, anote; aceitável restaurar a janela antes, mas só se for simples.)
  3. YouTube Music (app instalado) tocando → clicar → abre o app. YouTube Music numa aba comum → abre a aba.
  4. WhatsApp → continua abrindo como antes.
  5. Clicar no texto (título/artista) → mesmo comportamento da capa.
  6. Rodinha para cima/baixo sobre o player → "Volume N%" aparece, o som do Chrome muda, o volume geral do Windows **não** muda (confira no Mixer de Volume: só a barra do Chrome mexe).
  7. Touchpad com dois dedos → volume muda sem pular de 0 a 100.
  8. Rodinha com mídia de fonte "Outro" (ex.: Spotify) → nada acontece e nada quebra (fica no log).
- [ ] **Step 3: Docs**
  - `README.md`, seção "O que tem no player": clicar na capa/texto abre a aba exata; rodinha do mouse = volume só do app.
  - `README.md`, "Perguntas comuns": "Por que o volume do YouTube Music muda junto com as abas do Chrome?" → o Chrome tem um controle de volume só no Windows.
  - `docs/COMO-FUNCIONA.md`: no mapa do código, `Ui/ChromeTabs.cs` e `Audio/AppVolume.cs`; nova seção curta "Abrir a aba e volume do app"; em "Limitações conhecidas", as duas limitações (volume do Chrome é um só; achar a aba depende do nome que o Chrome dá à aba).
- [ ] **Step 4: Commit** `docs: document open-exact-tab and per-app volume`

---

## Fora do escopo (ideias para depois)

- **Volume só de uma aba** (sem afetar outras abas do Chrome nem o YT Music): exige uma extensão do Chrome conversando com o app. Esforço 4/5.
- Clique do meio = mudo.
- Fontes "Outro" (Spotify etc.): mapear o AUMID para o processo e liberar volume/abrir para elas.
