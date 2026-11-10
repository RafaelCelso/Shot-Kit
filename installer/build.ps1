<#
  Gera o instalador do ShotKit.
  1. Publica o app self-contained (já leva o .NET; quem instala não precisa de nada).
  2. Compila installer\ShotKit.iss com o Inno Setup.
  Saída: artifacts\installer\ShotKit-Setup-<versão>.exe

  Uso:  powershell -ExecutionPolicy Bypass -File installer\build.ps1
#>
$ErrorActionPreference = 'Stop'
$root    = Split-Path $PSScriptRoot -Parent
$proj    = Join-Path $root 'src\ShotKit\ShotKit.csproj'
$publish = Join-Path $root 'artifacts\publish'

# Versão vem do <Version> do .csproj.
$version = ([xml](Get-Content $proj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'Não encontrei <Version> em ShotKit.csproj.' }

# Inno Setup: instalação por usuário ou para todos.
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 não encontrado. Instale com: winget install JRSoftware.InnoSetup' }

Write-Host "==> Publicando ShotKit $version (self-contained, win-x64)..."
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish falhou.' }

Write-Host "==> Compilando instalador..."
& $iscc "/DAppVersion=$version" "/DSourceDir=$publish" (Join-Path $PSScriptRoot 'ShotKit.iss')
if ($LASTEXITCODE -ne 0) { throw 'ISCC falhou.' }

$out = Join-Path $root "artifacts\installer\ShotKit-Setup-$version.exe"
Write-Host "==> Pronto: $out ($([math]::Round((Get-Item $out).Length / 1MB, 1)) MB)"
