# Baixa o instalador oficial do Equalizer APO 1.4.2 (SourceForge) para vendor\ e confere o MD5
# publicado pelo projeto. O build.ps1 chama este script quando o arquivo nao existe.
$ErrorActionPreference = 'Stop'
$vendor = Join-Path $PSScriptRoot '..\vendor'
$file = Join-Path $vendor 'EqualizerAPO-x64-1.4.2.exe'
$md5 = '410aab9749ae4673b950bc29a4eb226f'
New-Item -ItemType Directory -Force $vendor | Out-Null
if (-not (Test-Path $file)) {
    Write-Host 'Baixando Equalizer APO 1.4.2 (12 MB)...'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -UserAgent 'Wget/1.21' -OutFile $file `
        -Uri 'https://sourceforge.net/projects/equalizerapo/files/1.4.2/EqualizerAPO-x64-1.4.2.exe/download'
}
$got = (Get-FileHash $file -Algorithm MD5).Hash.ToLower()
if ($got -ne $md5) { Remove-Item $file; throw "MD5 diferente do publicado ($got). Arquivo apagado." }
Write-Host 'Equalizer APO conferido (MD5 ok).'
