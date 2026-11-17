# Como instalar o Miniplayer Barra

Este guia começa do zero: um PC com Windows 11 que nunca teve nada de programação instalado. Leva uns 10 minutos, quase tudo esperando downloads.

O app é instalado só para o seu usuário. **Não precisa ser administrador**, a não ser que o instalador do .NET peça.

## O que você precisa

- Windows 11.
- O **.NET 8 SDK**, da Microsoft (gratuito). É ele que transforma o código em programa.
- Este repositório, baixado no seu PC.

## 1. Instalar o .NET 8 SDK

1. Abra a página oficial: <https://dotnet.microsoft.com/download/dotnet/8.0>.
2. Na coluna **SDK**, baixe o instalador do Windows que combina com o seu PC: **x64** (a maioria) ou **Arm64**. O script de instalação escolhe sozinho a versão certa, mas o .NET precisa ser da mesma arquitetura do PC.
3. Abra o arquivo baixado e siga o instalador (Avançar, Instalar, Fechar).

Para conferir se deu certo, abra o **PowerShell** (procure "PowerShell" no menu Iniciar) e digite:

```powershell
dotnet --list-sdks
```

Deve aparecer uma linha começando com `8.`. Se aparecer "dotnet não é reconhecido", feche e abra o PowerShell de novo. Se ainda assim não funcionar, reinicie o PC.

Outra opção, se você usa o `winget`:

```powershell
winget install Microsoft.DotNet.SDK.8
```

## 2. Baixar o Miniplayer Barra

Escolha um dos jeitos:

**Jeito fácil (ZIP)**
1. Na página do repositório no GitHub, clique no botão verde **Code** e depois em **Download ZIP**.
2. Clique com o botão direito no ZIP baixado → **Extrair tudo** e escolha uma pasta, por exemplo `Documentos\miniplayer-barra`.

**Com Git**
```powershell
git clone https://github.com/athaisfreitas/miniplayer-barra.git
```

## 3. Compilar e instalar

1. Abra a pasta do projeto no Explorador de Arquivos (a que tem o arquivo `publicar.ps1`).
2. Clique na barra de endereço, digite `powershell` e aperte Enter. Um PowerShell abre já nessa pasta.
3. Rode:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\publicar.ps1
   ```

O script faz 4 coisas e mostra cada uma na tela:

1. Fecha o Miniplayer Barra, se ele já estiver aberto.
2. Roda os testes automáticos. Se algum falhar, ele para e **não instala nada**.
3. Compila e copia o app para `%LOCALAPPDATA%\Programs\MiniplayerBarra`. Na primeira vez, cria o `config.json` a partir do `config.exemplo.json`. Se já existir um `config.json`, ele é mantido.
4. Abre o app.

Na primeira vez, o `dotnet` baixa alguns pacotes da internet (como a biblioteca de áudio NAudio), então pode demorar um pouco mais. Depois de instalado, o app em si não usa a internet.

O `-ExecutionPolicy Bypass` só vale para esse comando: ele deixa o Windows rodar o script baixado sem mudar nenhuma configuração do seu PC.

## 4. Pronto

Dê play em uma música. O player aparece na barra de tarefas (veja no [README](../README.md#onde-o-player-aparece) onde ele fica em cada configuração da barra).

**Abrir com o Windows:** na primeira vez que o app abre, ele se cadastra sozinho para abrir junto com o Windows (no registro do seu usuário, `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, valor `MiniplayerBarra`). Para desligar isso sem desinstalar, use **Configurações → Aplicativos → Inicialização**.

## Atualizar

Baixe a versão nova (ZIP de novo, ou `git pull` na pasta) e rode o mesmo comando do passo 3. O script fecha o app antigo, instala o novo e abre de novo. O seu `config.json` não é apagado.

## Se algo der errado

| O que aparece | O que fazer |
|---|---|
| `dotnet` não é reconhecido | Instale o .NET 8 SDK (passo 1) e abra um PowerShell novo. |
| `Testes falharam: nada foi instalado.` | Algo no código não passou nos testes. Baixe de novo a versão mais recente. |
| Erro dizendo que o arquivo está em uso | Feche o app (botão direito no player → **Sair**) e rode o script de novo. |
| O app abriu, mas o player não aparece | Veja "Por que o player não aparece?" no [README](../README.md#perguntas-comuns). |

O log do app fica em `%LOCALAPPDATA%\MiniplayerBarra\log.txt`.

## Desinstalar

Veja a seção [Desinstalar](../README.md#desinstalar) do README.
