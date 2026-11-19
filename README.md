# Miniplayer Barra

Um player de música pequeno que fica **dentro da barra de tarefas do Windows 11**. Ele mostra o que está tocando (capa, música e artista) e tem os botões de voltar, pausar e avançar. Assim você troca de música sem sair da janela em que está trabalhando.

![O Miniplayer Barra na barra de tarefas](docs/imagens/player.png)

Foi feito para o **YouTube Music instalado como app do Chrome**, mas também funciona com vídeos do YouTube em abas do Chrome, com o WhatsApp Desktop e com outros apps que avisam o Windows do que estão tocando.

- Não precisa de extensão no navegador, de login nem de internet.
- É leve: quase 0% de CPU quando nada toca e cerca de 40 MB de memória.
- Abre sozinho junto com o Windows.

## O que tem no player

| Parte | O que faz |
|---|---|
| **⏮ Voltar** | Volta para a música anterior. |
| **⏯ Pausar / tocar** | Mostra ‖ enquanto toca e ▷ quando está pausado. |
| **⏭ Avançar** | Passa para a próxima música. |
| **Barrinhas coloridas** | Ficam atrás dos botões e se mexem com o som de verdade. A cor vem da capa da música. Com a música pausada, elas ficam paradas. |
| **Capa, música e artista** | Clique para abrir o app ou **a aba exata** do Chrome que está tocando, mesmo que ela esteja em segundo plano no meio de várias abas. Passe o mouse para ver o nome completo, se ele estiver cortado com "…". |
| **Rodinha do mouse** | Gire sobre o player para subir ou descer o volume, de 5 em 5%, **só do app que está tocando** (como no Mixer de Volume do Windows). O player mostra "Volume 40%" por um instante. O volume geral do Windows não muda. |
| **Clique com o botão direito** | Abre um menu com **"Pausar tudo"** e **"Sair"**. |

Clicar nos botões **não** tira o foco da janela em que você está. Se você estiver digitando um texto, pode pausar a música e continuar digitando.

Se não houver nada tocando nem pausado, o player some e a barra fica limpa. Ele volta sozinho quando alguma mídia aparecer.

## Qual mídia aparece

Às vezes há várias coisas abertas ao mesmo tempo, como uma música pausada no YouTube Music e um vídeo numa aba. O player escolhe uma só, nesta ordem:

1. **O que está tocando ganha do que está pausado.**
2. Entre as que estão no mesmo estado, vale a prioridade: **YouTube Music > abas do Chrome > WhatsApp > outros**.

Exemplo: com o YouTube Music pausado, se você der play num vídeo numa aba do Chrome, o player mostra o vídeo. Quando você pausa o vídeo, ele volta a mostrar o YouTube Music pausado.

A ordem pode ser mudada no `config.json` (veja [Configurar](#configurar)).

## "Pausar tudo"

Às vezes, ao reabrir o Chrome, vários vídeos começam a tocar juntos. Clique com o botão direito no player e escolha **"Pausar tudo"**: tudo que estiver tocando para, inclusive várias abas do Chrome ao mesmo tempo.

Um detalhe: o Chrome só mostra ao Windows **uma aba por vez**. Para chegar nas outras abas, o "Pausar tudo" precisa "parar" a aba (e não só pausar). Uma aba parada sai do player até você dar play nela de novo, pela própria aba. O YouTube Music só é pausado, então ele continua no player.

## Onde o player aparece

O player procura um espaço livre na barra de tarefas do monitor principal, sem cobrir os ícones dos apps, o relógio nem a bandeja:

| Como está a sua barra | Onde o player fica |
|---|---|
| Ícones no centro, Widgets desligados | Encostado na ponta esquerda da barra. |
| Ícones no centro, Widgets ligados (clima) | Logo à direita do clima. |
| Ícones à esquerda | Do lado direito, logo à esquerda do Barra de Bateria, se ele estiver aberto (projeto irmão, ainda não publicado), ou da área do relógio. |

O player **nunca some por falta de espaço**. Se a barra ficar cheia, primeiro o texto encolhe, depois o texto some, e os botões e a capa continuam aparecendo. Quando você muda o alinhamento da barra, liga os Widgets ou abre mais apps, ele se reposiciona sozinho. Se o Explorer do Windows reiniciar, ele se encaixa de novo.

## Instalar

Resumo (o passo a passo completo está em [docs/COMO-INSTALAR.md](docs/COMO-INSTALAR.md)):

1. Instale o **.NET 8 SDK** (gratuito, da Microsoft).
2. Baixe este repositório (botão verde **Code → Download ZIP**, ou `git clone`).
3. Na pasta do projeto, rode no PowerShell:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\publicar.ps1
   ```

O script roda os testes, instala o app em `%LOCALAPPDATA%\Programs\MiniplayerBarra`, cria o `config.json` e abre o player. Na primeira vez que abre, o app se cadastra para abrir junto com o Windows. Não precisa ser administrador.

Para atualizar depois de baixar uma versão nova, rode o mesmo comando. O seu `config.json` é mantido.

## Configurar

A configuração fica num arquivo `config.json`, ao lado do programa (`%LOCALAPPDATA%\Programs\MiniplayerBarra\config.json`). Ele é opcional: sem ele, o app usa os valores abaixo.

```json
{
  "prioridade": ["youtubeMusic", "chrome", "whatsapp"],
  "idYoutubeMusic": "cinhimbnkkghhklpknlkffjgod",
  "idWhatsApp": "5319275A.WhatsAppDesktop",
  "ondas": true
}
```

| Campo | Para que serve |
|---|---|
| `prioridade` | A ordem das fontes quando nenhuma está tocando, ou quando mais de uma toca. Valores aceitos: `youtubeMusic`, `chrome` (abas do Chrome) e `whatsapp`. Uma fonte que ficar fora da lista vem depois das listadas. Os outros apps ("outros") vêm sempre por último. |
| `idYoutubeMusic` | Um pedaço do código que o Windows usa para reconhecer o app do YouTube Music. O padrão serve para o app instalado pelo Chrome. Só mude se o seu YouTube Music aparecer como "outros". |
| `idWhatsApp` | O começo do código do WhatsApp Desktop no Windows. Só mude se o seu WhatsApp aparecer como "outros". |
| `ondas` | `true` mostra as barrinhas que se mexem com o som. `false` deixa as barrinhas desligadas (o app fica ainda mais leve). |

Depois de editar, feche o app (botão direito → **Sair**) e abra de novo (`MiniplayerBarra.exe`, na mesma pasta). Se o arquivo tiver um erro de digitação, o app usa os valores padrão.

## Onde fica cada coisa

| O quê | Onde |
|---|---|
| Código | esta pasta (`src/` = app, `tests/` = testes) |
| App instalado | `%LOCALAPPDATA%\Programs\MiniplayerBarra\MiniplayerBarra.exe` |
| Configuração | `%LOCALAPPDATA%\Programs\MiniplayerBarra\config.json` |
| Exemplo de configuração | `config.exemplo.json`, nesta pasta |
| Log (registro de erros e avisos) | `%LOCALAPPDATA%\MiniplayerBarra\log.txt` |
| Abrir com o Windows | registro: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, valor `MiniplayerBarra` |

Dica: `%LOCALAPPDATA%` é uma pasta do seu usuário. Para abrir, cole `%LOCALAPPDATA%\Programs\MiniplayerBarra` na barra de endereço do Explorador de Arquivos.

## Desinstalar

1. Feche o app: clique com o botão direito no player → **Sair**.
2. Apague a pasta `%LOCALAPPDATA%\Programs\MiniplayerBarra`.
3. Apague o valor `MiniplayerBarra` da inicialização. O jeito mais fácil é rodar no PowerShell:

   ```powershell
   Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'MiniplayerBarra'
   ```

   Também dá para desligar em **Configurações → Aplicativos → Inicialização**, mas isso só desativa; não apaga o valor.
4. (Opcional) Apague a pasta do log: `%LOCALAPPDATA%\MiniplayerBarra`.

Feche o app **antes** de apagar o valor do registro. Ele só grava o valor ao iniciar: se o app estiver aberto, nada muda na hora, mas o valor volta no próximo início do app.

## Perguntas comuns

**É seguro?**
Sim. O app não usa a internet, não abre nenhuma porta de rede e não envia nada para lugar nenhum. Ele só lê o que o próprio Windows já mostra sobre as mídias tocando (é a mesma informação que aparece nos controles de mídia do Windows) e o volume do som que sai do app que está tocando, para mexer as barrinhas. O som não é gravado nem salvo. Não precisa de administrador. O código está todo aqui, aberto, para quem quiser conferir.

**Por que o player não aparece?**
- Não há nenhuma mídia aberta. O player só aparece quando algum app avisa o Windows que tem algo tocando ou pausado. Dê play em uma música e ele aparece.
- O app não está aberto. Abra `%LOCALAPPDATA%\Programs\MiniplayerBarra\MiniplayerBarra.exe`.
- A barra de tarefas está oculta automaticamente, ou em outro monitor. O player fica só na barra do monitor principal.
- Logo depois de reiniciar o Explorer, ele pode levar alguns segundos para se encaixar de novo.
- Se nada disso resolver, veja o log em `%LOCALAPPDATA%\MiniplayerBarra\log.txt`.

**Funciona com o Spotify (ou outro app)?**
Funciona com qualquer app que avisa o Windows do que está tocando, como o Spotify, o Edge e o Firefox. Eles aparecem como **"outros"**: entram por último na prioridade e as barrinhas usam o som geral do PC, em vez de só o som do app. Os botões funcionam se o app aceitar os comandos do Windows.

**Por que o volume do YouTube Music muda junto com as abas do Chrome?**
O Windows enxerga o Chrome como um app só, com um único controle de volume para todas as abas e para o YouTube Music instalado. Não dá para separar pelo Windows. A rodinha do player mexe nesse controle.

**A barra de tarefas fica mais pesada?**
Não. Com tudo pausado, o app fica parado (perto de 0% de CPU). As barrinhas e a captura do som só rodam enquanto a mídia mostrada está tocando.

**Funciona junto com o Barra de Bateria?**
Sim. Os dois cabem na mesma barra, cada um de um lado, sem se cobrir.

## Documentos

- [Como instalar, passo a passo](docs/COMO-INSTALAR.md)
- [Como funciona por dentro](docs/COMO-FUNCIONA.md)
- [Design: o que o app faz e por quê](docs/superpowers/specs/2026-11-17-miniplayer-barra-design.md)
- [Plano de implementação](docs/superpowers/plans/2026-11-17-miniplayer-barra.md)
- [Verificações práticas feitas antes de construir](docs/superpowers/plans/2026-11-17-verificacoes.md)

## Licença

[MIT](LICENSE). Pode usar, copiar e modificar à vontade.
