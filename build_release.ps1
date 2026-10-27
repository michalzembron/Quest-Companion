# ==============================================================================
# Quest Companion - Automated GitHub Release Packaging Script
# ==============================================================================

$ErrorActionPreference = "Stop"

$rootDir = $PSScriptRoot
$csprojPath = Join-Path $rootDir "Quest Companion\Quest Companion.csproj"
$releasesDir = Join-Path $rootDir "releases"
$publishDir = Join-Path $rootDir "publish"

$standaloneDir = Join-Path $publishDir "standalone"
$dependentDir = Join-Path $publishDir "framework-dependent"

$version = "1.0.0"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  Building Quest Companion v$version GitHub Release Assets " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Stop any running instance
Stop-Process -Name "Quest Companion" -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

# 2. Prepare output directories
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue }
if (-not (Test-Path $releasesDir)) {
    New-Item -ItemType Directory -Path $releasesDir -Force | Out-Null
} else {
    Get-ChildItem -Path $releasesDir | ForEach-Object {
        try { Remove-Item $_.FullName -Force -Recurse -ErrorAction Stop } catch { }
    }
}
New-Item -ItemType Directory -Path $standaloneDir -Force | Out-Null
New-Item -ItemType Directory -Path $dependentDir -Force | Out-Null

# 3. Publish Self-Contained Standalone (Recommended for 95% of users)
Write-Host "`n[1/4] Publishing Self-Contained Standalone Single-File (.NET 10 bundled)..." -ForegroundColor Yellow
dotnet publish $csprojPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $standaloneDir

# 4. Publish Framework-Dependent (Lightweight)
Write-Host "`n[2/4] Publishing Framework-Dependent Build (Requires .NET 10 Desktop Runtime)..." -ForegroundColor Yellow
dotnet publish $csprojPath `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $dependentDir

# 5. Package ZIP Archives (Single executable inside each ZIP)
Write-Host "`n[3/4] Packaging ZIP archives for GitHub Release (Clean single .exe per ZIP)..." -ForegroundColor Yellow
$standaloneZip = Join-Path $releasesDir "QuestCompanion-v$version-win-x64-standalone.zip"
$dependentZip = Join-Path $releasesDir "QuestCompanion-v$version-win-x64-framework-dependent.zip"

try {
    if (Test-Path $standaloneZip) { Remove-Item $standaloneZip -Force }
    Compress-Archive -Path "$standaloneDir\Quest Companion.exe" -DestinationPath $standaloneZip -Force
} catch {
    Write-Warning "Nie mozna nadpisac $standaloneZip - upewnij sie, ze nie masz otwartego tego archiwum ZIP w Eksploratorze Windows."
    throw $_
}

try {
    if (Test-Path $dependentZip) { Remove-Item $dependentZip -Force }
    Compress-Archive -Path "$dependentDir\Quest Companion.exe" -DestinationPath $dependentZip -Force
} catch {
    Write-Warning "Nie mozna nadpisac $dependentZip - upewnij sie, ze nie masz otwartego tego archiwum ZIP w Eksploratorze Windows."
    throw $_
}

# 6. Generate SHA256 Checksums
Write-Host "`n[4/4] Generating SHA256 Checksums..." -ForegroundColor Yellow
$checksumFile = Join-Path $releasesDir "SHA256_CHECKSUMS.txt"
$filesToHash = Get-ChildItem -Path $releasesDir -File | Where-Object { $_.Name -ne "SHA256_CHECKSUMS.txt" }
$hashOutput = foreach ($f in $filesToHash) {
    $hash = (Get-FileHash -Path $f.FullName -Algorithm SHA256).Hash
    "$hash  $($f.Name)"
}
$hashOutput | Out-File -FilePath $checksumFile -Encoding utf8

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host "  SUCCESS! Release artifacts ready in: $releasesDir" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Get-ChildItem -Path $releasesDir | Select-Object Name, @{Name="Size (MB)"; Expression={[math]::Round($_.Length / 1MB, 2)}} | Format-Table -AutoSize
