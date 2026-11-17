# Miniplayer Barra: design

Data: 2026-11-17
Status: aprovado pela autora em 2026-11-17
Uso: pessoal, mas o código fica em um repositório **público** no GitHub da autora (`athaisfreitas`). Ela é a única autora: **nenhum** coautor nos commits, no README ou no PR.

## 1. Por que existe

A autora ouve música no app do YouTube Music, que é um app do Chrome instalado. Enquanto trabalha, ela quer **ver o que está tocando e pausar, avançar ou voltar** sem trazer o YouTube Music ou o navegador para a frente.

**Sucesso:** bater o olho no canto esquerdo da barra de tarefas, ver capa, música e artista, e pausar ou trocar a faixa com um clique, sem sair da janela em que está trabalhando.

## 2. Objetivos, não-objetivos e anti-objetivos

**Objetivos**
- Um player pequeno **encaixado no lado esquerdo da barra de tarefas** do Windows 11, no estilo da referência abaixo.
- Mostrar a capa pequena, o título (em negrito) e o artista.
- Botões **voltar, pausar/tocar e avançar**.
- Barrinhas de "ondas sonoras" que **se mexem com o som de verdade** e são **coloridas** (ver seção 5).
- Escolher sozinho qual mídia mostrar quando houver várias (seção 4).
- Clique direito: menu com **"Pausar tudo"** e "Sair".
- Abrir junto com o Windows.

**Não-objetivos** (decididos pela autora)
- Nada para jogos ou tela cheia: sem sobreposição e sem atalhos de teclado. O player existe só na barra.
- Barra de progresso, volume, letra, playlist ou busca.

**Anti-objetivos** (o app falha mesmo se "funcionar")
- Ficar pesado: CPU acordando sem parar quando nada toca, ou memória na casa das centenas de MB.
- Cobrir os ícones dos apps abertos (que ficam centralizados), o relógio ou a bandeja.
- Mostrar uma música errada ou velha como se fosse a atual.
- Roubar o foco da janela em que a autora está trabalhando ao clicar nos botões.
- Publicar dados pessoais no repositório público (seção 8).

## 3. De onde vêm os dados (verificado neste PC em 2026-11-17)

O Windows publica as mídias em reprodução pela API **Global System Media Transport Controls** (`Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager`). O teste listou estas sessões:

| Fonte | `SourceAppUserModelId` | Dados |
|---|---|---|
| App do YouTube Music (app do Chrome) | `Chrome._crx_cinhimbnkkghhklpknlkffjgod` | título, artista, capa, estado |
| Aba comum do Chrome (vídeo do YouTube) | `Chrome` | título, canal (como artista), capa, estado |
| WhatsApp Desktop | `5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App` | título genérico, sem capa |

A mesma API oferece tocar/pausar, avançar e voltar para cada sessão. Não precisa de extensão no navegador, de login nem de rede.

**Limitação conhecida:** com várias abas tocando ao mesmo tempo, o Chrome parece expor **uma única** sessão `Chrome` ao Windows. Ainda não está confirmado. Isso afeta o "Pausar tudo" (seção 6).

## 4. Qual mídia aparece (regra da autora)

Prioridade de fonte: **YouTube Music > Chrome (abas) > WhatsApp > outros**.

1. Se alguma sessão está **tocando**, mostrar a de maior prioridade entre as que tocam.
2. Se nenhuma toca, mostrar a de maior prioridade entre as existentes. Na prática, o YouTube Music pausado.
3. Se não existe sessão nenhuma, o player **some** e a barra fica limpa. *(decisão de projeto)*

Consequência da regra 1, confirmada com a autora: com o YT Music pausado e um vídeo tocando numa aba do Chrome, o player mostra o vídeo.

Identificação das fontes: YT Music pelo ID que contém `cinhimbnkkghhklpknlkffjgod`; abas do Chrome pelo ID exato `Chrome` (outros apps do Chrome, `Chrome._crx_*`, contam como "outros"); WhatsApp pelo ID que começa com `5319275A.WhatsAppDesktop`. *(decisão de projeto)* Os IDs ficam num arquivo de configuração (seção 7), para o caso de mudarem em outro PC.

A troca de mídia mostrada é **imediata**, pelos eventos da API (sessão mudou, propriedades mudaram, estado mudou). Não depende de consulta periódica.

## 5. Como fica

Referência enviada pela autora (barra escura):

```
 ┌─────────────────────────────────────────────────┐
 │  |◁    ‖    ▷|   ┌────┐  Money (2023 Remaster)  │
 │  ▂▅▇▆▃▂▅▄▂▁▁     │capa│  Pink Floyd             │       [ apps centralizados ]      ^ 🔊 14:32
 └─────────────────────────────────────────────────┘
   lado esquerdo da barra de tarefas
```

- **Posição:** encostado à esquerda da barra de tarefas do monitor principal, com uma margem pequena. Hoje a barra da autora está centralizada e os Widgets estão desligados, então esse espaço está livre.
- **Bloco das ondas:** as barrinhas ficam atrás dos três botões, como na referência. Os botões ficam por cima, sempre legíveis.
- **Capa:** quadrada, da altura útil da barra. Sem capa (ex.: WhatsApp), mostrar um ícone neutro de nota musical. *(decisão de projeto)*
- **Texto:** título em negrito na primeira linha e artista na segunda, mais apagado. Texto longo é cortado com "…" e o texto completo aparece ao passar o mouse. *(decisão de projeto)*
- **Largura:** cerca de 300 a 480 px conforme a escala da tela. O player **nunca** pode cobrir os ícones centralizados: se os apps abertos crescerem para a esquerda, o texto encolhe primeiro. Se ainda assim não couber, o texto some e ficam só os botões e a capa. *(decisão de projeto)*
- **Tema:** texto e botões seguem o tema da barra (claro ou escuro), como no Barra de Bateria.
- **Botão do meio:** mostra ‖ enquanto toca e ▷ quando está pausado.

**Ondas sonoras (pedido da autora: coloridas e reagindo ao som)**
- Cerca de 12 barrinhas, cada uma reagindo a uma faixa de frequência do som, com subida rápida e descida suave.
- O som analisado é **o do processo da fonte mostrada**: para YT Music e abas, o Chrome. Assim, a voz do Discord ou um alerta do Windows não fazem as barras pularem. Se isso falhar, usar o som geral do PC. *(decisão de projeto)*
- **Cor:** tirada da capa da música (uma cor dominante e viva), com um degradê discreto da base ao topo. Sem capa, usa a cor de destaque do Windows. *(decisão de projeto)*
- **Pausado ou sem som:** as barras ficam baixas e paradas, e a captura de áudio e a animação **param** (sem custo).
- Meta de custo enquanto toca: cerca de 1% de CPU, com animação a ~30 quadros por segundo.

**Interações**
- Clique em voltar, pausar ou avançar: age na sessão mostrada **sem** trazer nenhuma janela para a frente e sem tirar o foco da janela atual.
- Clique na capa ou no texto: traz o app da fonte (ex.: o YouTube Music) para a frente. *(decisão de projeto)*
- Clique direito: menu com **"Pausar tudo"** e **"Sair"**.

## 6. Comportamento

- **Pausar tudo** (pedido da autora: acontece de vários vídeos tocarem juntos depois de reabrir as janelas): pausa todas as sessões que estão tocando. Como o Chrome pode expor só uma aba por vez (seção 3), o app repete o ciclo "pausar, esperar um instante, ver se outra sessão passou a tocar" até nada tocar, com limite de tentativas. **Precisa de um teste prático no início da construção** (abrir 3 abas do YouTube tocando e conferir). Se não houver jeito confiável de pausar todas as abas, avisar a autora antes de seguir com uma alternativa.
- **Abrir com o Windows:** chave `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, valor `MiniplayerBarra`. Não precisa de administrador.
- **Uma instância só.**
- **Explorer reiniciou ou a barra mudou:** reencaixar sozinho, como o Barra de Bateria já faz.
- **O player nunca some por falta de lugar** (pedido da autora). Ele se ajusta ao espaço livre:
  - **Barra centralizada, Widgets desligados** (caso de hoje): encostado à esquerda da barra.
  - **Barra centralizada, Widgets ligados:** logo à direita do botão/área dos Widgets (clima), sem cobri-lo, e ainda à esquerda dos ícones centralizados.
  - **Barra alinhada à esquerda:** do lado direito, **logo à esquerda do Barra de Bateria**. Se o Barra de Bateria não estiver aberto, logo à esquerda da área de notificação (bandeja/relógio).
  - Em todos os casos, a regra de encolher da seção 5 vale: se faltar espaço, o texto encolhe e depois some, mas **botões e capa continuam visíveis**. O player reposiciona sozinho quando o alinhamento, os Widgets, o Barra de Bateria ou a largura dos ícones mudam.
  - Os botões de Pesquisa e Visão de Tarefas contam como parte dos ícones e nunca são cobertos.
- **Junto com o Barra de Bateria:** os dois apps encaixam na mesma barra, mas em lados opostos. O Barra de Bateria **não** é alterado. No fim da construção, testar os dois abertos juntos, inclusive reiniciando o Explorer. *(decisão de projeto)*
- **Sem rede**, sem telemetria, sem atualização automática.

## 7. Configuração

Um `config.json` opcional ao lado do programa, sem tela de configurações. *(decisão de projeto)* Campos:
- lista de prioridade das fontes (padrão: YT Music, Chrome, WhatsApp);
- ligar ou desligar as ondas.

Depois de editar, fecha e abre o app.

## 8. Repositório público e privacidade

- Repositório **público** `athaisfreitas/miniplayer-barra`. Segurança: público só deixa as pessoas **lerem o código**. O app não abre porta de rede nem recebe conexões, então ninguém acessa o PC por ele. O risco real é deixar dados pessoais escritos nos arquivos.
- Por isso, nada de dados pessoais no código, nos documentos, nos exemplos ou nos logs versionados: nomes de aparelhos, caminhos com o nome de usuário, e-mails. O `config.json` real não vai para o git, só um `config.exemplo.json`.
- Autoria: somente a autora, com o nome `athaisfreitas` e o **e-mail privado do GitHub** `322781500+athaisfreitas@users.noreply.github.com` (escolha da autora: o e-mail pessoal não aparece nos commits).
- Entrega: commits direto na `main`, sem PR (projeto pessoal). O repositório é criado e recebe o push só no fim, depois que tudo estiver revisado e sem dados pessoais. *(decisão de projeto)*
- Autoria, regra geral: somente a autora. Sem `Co-Authored-By` e sem linhas de atribuição.
- **Tarefa separada, com plano próprio, depois deste app:** tornar o **Barra de Bateria** público. Antes, revisar o repositório e o histórico em busca de dados pessoais. Mostrar a lista à autora e só mudar a visibilidade com o OK dela.

## 9. Restrições

- Windows 11 (10.0.26200), um usuário, sem administrador para rodar.
- Leve: ≈ 0% de CPU sem nada tocando e poucas dezenas de MB de memória.
- Mesma base do Barra de Bateria: **C# / .NET 8**, WinForms, `net8.0-windows10.0.19041.0` (que já dá acesso às APIs de mídia do Windows).

## 10. Abordagens consideradas

| | Abordagem | Prós | Contras |
|---|---|---|---|
| **A (escolhida pela autora)** | App separado em C#/.NET 8, **copiando e adaptando** o código de encaixe na barra do Barra de Bateria (`src/BarraBateria/Ui/TaskbarBar.cs`, `Native.cs`, `Theme.cs`, `StartupRegistration.cs`, `SingleInstance.cs`) | Base que já funciona nesta barra. Cada app quebra sozinho. Repositório público limpo | Código de encaixe duplicado em dois projetos |
| B | Juntar ao Barra de Bateria (um app, bateria à direita e player à esquerda) | Um processo, sem duplicação | Um problema no player derruba a bateria. Não vira um projeto próprio |
| C | App separado em WinUI/WPF | Visual e animações mais caprichados | Mais memória, nada reaproveitado, ganho pequeno numa barra |

## 11. Documentação (pedido: "completa")

Em português:
- `README.md`: o que é, imagem do player, o que cada botão faz, regra de prioridade, "Pausar tudo", como instalar, como configurar, onde fica cada coisa, como desinstalar, perguntas comuns ("é seguro?", "por que não aparece?").
- `docs/COMO-INSTALAR.md`: passo a passo para outra pessoa ou outro PC (instalar o .NET, baixar, compilar ou usar o script, abrir com o Windows).
- `docs/COMO-FUNCIONA.md`: visão técnica curta (API de mídia, captura de áudio, encaixe na barra) para quem quiser ler o código.
- Este design e o plano de implementação em `docs/superpowers/`.
- `LICENSE`: MIT, no nome da autora. *(decisão de projeto)*
- `publicar.ps1`: roda os testes, compila, instala em `%LOCALAPPDATA%\Programs\MiniplayerBarra` e abre o app, igual ao Barra de Bateria.

## 12. Fica a cargo de quem construir

- A técnica exata de captura de áudio por processo (API de *process loopback* do Windows) e da análise de frequências, além da biblioteca usada (ex.: NAudio) ou de uma implementação própria.
- O algoritmo para tirar a cor dominante da capa.
- Medidas finas: tamanhos, margens e fonte, desde que fique parecido com a referência e respeite a escala da tela.
- Como detectar onde começam os ícones centralizados, onde termina a área dos Widgets e onde está a janela do Barra de Bateria (ela é filha de `Shell_TrayWnd`; ver `TaskbarBar.cs` no projeto irmão).
- O desenho dos ícones dos botões.
