# Compila, testa e instala o Miniplayer Barra neste PC.
# Uso (na pasta do projeto):  powershell -ExecutionPolicy Bypass -File .\publicar.ps1
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$destino = Join-Path $env:LOCALAPPDATA 'Programs\MiniplayerBarra'

Write-Host '1/4 Fechando o Miniplayer Barra (se estiver aberto)...'
Get-Process MiniplayerBarra -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Write-Host '2/4 Rodando os testes...'
dotnet test
if ($LASTEXITCODE -ne 0) { throw 'Testes falharam: nada foi instalado.' }

Write-Host "3/4 Publicando em $destino ..."
$rid = switch ($env:PROCESSOR_ARCHITECTURE) {
    'AMD64' { 'win-x64' }
    'ARM64' { 'win-arm64' }
    default { throw "Arquitetura nao suportada: $($env:PROCESSOR_ARCHITECTURE)" }
}
dotnet publish src/MiniplayerBarra -c Release -r $rid --self-contained false -p:PublishSingleFile=true -o $destino
if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar.' }

$config = Join-Path $destino 'config.json'
if (-not (Test-Path $config)) {
    Copy-Item 'config.exemplo.json' $config
    Write-Host '   config.json criado a partir do exemplo.'
} else {
    Write-Host '   config.json ja existia: mantido.'
}

Write-Host '4/4 Abrindo o app (ele se registra para abrir com o Windows)...'
Start-Process (Join-Path $destino 'MiniplayerBarra.exe')
Write-Host 'Pronto.'
