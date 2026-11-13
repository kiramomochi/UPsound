# Compila o UPsound e o instalador com o compilador C# que ja vem no Windows (.NET Framework 4.8).
#   dist\UPsound.exe         o app (vai para Arquivos de Programas)
#   dist\UPsound-Setup.exe   o instalador, com o app e o Equalizer APO dentro
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $fw 'csc.exe'
$wpf = Join-Path $fw 'WPF'
$assets = Join-Path $root 'assets'
$dist = Join-Path $root 'dist'
$apo = Join-Path $root 'vendor\EqualizerAPO-x64-1.4.2.exe'

if (-not (Test-Path (Join-Path $assets 'tray-dark-on.ico'))) { & (Join-Path $root 'tools\make-icon.ps1') }
New-Item -ItemType Directory -Force $dist | Out-Null

$refs = @(
    (Join-Path $wpf 'PresentationCore.dll'),
    (Join-Path $wpf 'PresentationFramework.dll'),
    (Join-Path $wpf 'WindowsBase.dll'),
    (Join-Path $fw 'System.Xaml.dll'),
    (Join-Path $fw 'System.dll')
) | ForEach-Object { "/r:$_" }
$common = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu', '/utf8output',
    "/win32icon:$(Join-Path $assets 'UPsound.ico')", "/win32manifest:$(Join-Path $root 'src\app.manifest')")

function Compile($out, $sources, $resources) {
    $res = $resources | ForEach-Object { "/resource:$($_[0]),$($_[1])" }
    & $csc @common "/out:$out" $refs $res $sources
    if ($LASTEXITCODE -ne 0) { throw "Falha na compilacao de $out" }
}

# 1. App
$appSources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
$trayIcons = Get-ChildItem $assets -Filter 'tray-*.ico' | ForEach-Object { , @($_.FullName, $_.Name) }
Compile (Join-Path $dist 'UPsound.exe') $appSources $trayIcons
Write-Host "Pronto: dist\UPsound.exe"

# 2. Instalador (reaproveita tema, controles, motor e rotinas de instalacao do app)
if (-not (Test-Path $apo)) { & (Join-Path $root 'tools\get-apo.ps1') }
$shared = 'Theme.cs', 'Controls.cs', 'Engine.cs', 'Install.cs', 'AssemblyInfo.cs' | ForEach-Object { Join-Path $root "src\$_" }
$setupSources = @($shared) + @(Get-ChildItem (Join-Path $root 'setup') -Filter *.cs | ForEach-Object { $_.FullName })
Compile (Join-Path $dist 'UPsound-Setup.exe') $setupSources @(
    , @((Join-Path $dist 'UPsound.exe'), 'UPsound.exe')
    , @($apo, 'EqualizerAPO.exe')
    , @((Join-Path $assets 'UPsound.png'), 'UPsound.png')
)
Write-Host "Pronto: dist\UPsound-Setup.exe"
