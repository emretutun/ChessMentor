<#
.SYNOPSIS
    Stockfish satranç motorunu resmi GitHub sürümlerinden indirip engine/ klasörüne kurar.

.DESCRIPTION
    Stockfish ücretsiz ve açık kaynaklıdır (GPLv3). Bu betik işlemcinizin AVX2 desteğine bakar,
    uygun Windows derlemesini https://github.com/official-stockfish/Stockfish/releases adresinden indirir
    ve depo kökündeki engine\stockfish.exe olarak kaydeder. ChessMentor motoru orada otomatik bulur.

.PARAMETER Build
    auto (varsayılan), avx2, sse41-popcnt veya generic. Uygulama açılmıyorsa sse41-popcnt ya da generic deneyin.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\get-stockfish.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\get-stockfish.ps1 -Build sse41-popcnt
#>
param(
    [ValidateSet('auto', 'avx2', 'sse41-popcnt', 'generic')]
    [string]$Build = 'auto'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest'i çok hızlandırır
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$root = Split-Path -Parent $PSScriptRoot
$engineDir = Join-Path $root 'engine'

if ($Build -eq 'auto') {
    try {
        Add-Type -Namespace Native -Name Cpu -MemberDefinition '[DllImport("kernel32.dll")] public static extern bool IsProcessorFeaturePresent(uint feature);' -ErrorAction SilentlyContinue
        # 40 = PF_AVX2_INSTRUCTIONS_AVAILABLE (Windows 10 2004 ve sonrası)
        $Build = if ([Native.Cpu]::IsProcessorFeaturePresent(40)) { 'avx2' } else { 'sse41-popcnt' }
    } catch {
        $Build = 'sse41-popcnt'
    }
}

$asset = if ($Build -eq 'generic') { 'stockfish-windows-x86-64.zip' } else { "stockfish-windows-x86-64-$Build.zip" }
$url = "https://github.com/official-stockfish/Stockfish/releases/latest/download/$asset"

$temp = Join-Path ([IO.Path]::GetTempPath()) ("stockfish-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null

try {
    Write-Host "Derleme : $Build"
    Write-Host "İndiriliyor: $url"
    $zip = Join-Path $temp $asset
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing

    Write-Host "Arşiv açılıyor..."
    Expand-Archive -Path $zip -DestinationPath (Join-Path $temp 'x') -Force

    $exe = Get-ChildItem -Path (Join-Path $temp 'x') -Filter 'stockfish*.exe' -Recurse | Select-Object -First 1
    if (-not $exe) { throw "Arşivde stockfish*.exe bulunamadı." }

    New-Item -ItemType Directory -Force -Path $engineDir | Out-Null
    $target = Join-Path $engineDir 'stockfish.exe'
    Copy-Item $exe.FullName $target -Force

    # Lisans dosyasını da yanına koy (GPLv3 gereği)
    $copying = Get-ChildItem -Path (Join-Path $temp 'x') -Filter 'Copying.txt' -Recurse | Select-Object -First 1
    if ($copying) { Copy-Item $copying.FullName (Join-Path $engineDir 'Stockfish-COPYING.txt') -Force }

    # Hızlı doğrulama: motor UCI ile cevap veriyor mu?
    $check = "uci`nquit`n" | & $target 2>$null | Select-String '^id name' | Select-Object -First 1
    if ($check) {
        Write-Host ""
        Write-Host "Kuruldu: $target" -ForegroundColor Green
        Write-Host ($check.Line -replace '^id name ', 'Motor  : ') -ForegroundColor Green
    } else {
        Write-Warning "Motor kuruldu ama çalıştırılamadı. İşlemciniz bu derlemeyi desteklemiyor olabilir; -Build sse41-popcnt veya -Build generic deneyin."
    }
}
finally {
    Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue
}
