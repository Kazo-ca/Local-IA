<#
.SYNOPSIS
    Télécharge et installe automatiquement la dernière version officielle de llama.cpp avec accélération NVIDIA CUDA (CUDA 12.4).
.DESCRIPTION
    Récupère les binaires précompilés pour Windows x64 (llama-server, llama-cli, DLLs cuBLAS) depuis le dépôt GitHub ggml-org/llama.cpp.
#>

[CmdletBinding()]
param(
    [string]$TargetDir = ""
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir

if ([string]::IsNullOrWhiteSpace($TargetDir)) {
    $TargetDir = Join-Path $rootDir "bin\llama-cpp"
}

Write-Host "======================================================" -ForegroundColor Cyan
Write-Host "    LOCAL-IA : Installation de llama.cpp (NVIDIA CUDA) " -ForegroundColor Cyan
Write-Host " Dossier cible : $TargetDir" -ForegroundColor Yellow
Write-Host "======================================================" -ForegroundColor Cyan

if (-not (Test-Path $TargetDir)) {
    New-Item -Path $TargetDir -ItemType Directory -Force | Out-Null
}

$tempZipDir = Join-Path $rootDir "scratch\downloads"
if (-not (Test-Path $tempZipDir)) {
    New-Item -Path $tempZipDir -ItemType Directory -Force | Out-Null
}

Write-Host "[1/4] Recherche de la dernière version CUDA 12.4 sur GitHub..." -ForegroundColor Cyan
$releases = Invoke-RestMethod -Uri "https://api.github.com/repos/ggml-org/llama.cpp/releases?per_page=10" -Headers @{"User-Agent"="PowerShell"}

$selectedRelease = $null
$mainAsset = $null
$cudaAsset = $null

foreach ($r in $releases) {
    $main = $r.assets | Where-Object { $_.name -match "llama-.*-bin-win-cuda-12\.4-x64\.zip" }
    $cudart = $r.assets | Where-Object { $_.name -match "cudart-llama-bin-win-cuda-12\.4-x64\.zip" }
    if ($main -and $cudart) {
        $selectedRelease = $r
        $mainAsset = $main[0]
        $cudaAsset = $cudart[0]
        break
    }
}

if (-not $selectedRelease) {
    Write-Host "[ERREUR] Impossible de trouver une release CUDA 12.4 valide." -ForegroundColor Red
    return
}

Write-Host "[OK] Version sélectionnée : $($selectedRelease.tag_name)" -ForegroundColor Green

# 2. Téléchargement des archives
$mainZip = Join-Path $tempZipDir $mainAsset.name
$cudaZip = Join-Path $tempZipDir $cudaAsset.name

Write-Host "[2/4] Téléchargement des binaires principaux ($($mainAsset.name))..." -ForegroundColor Cyan
Invoke-WebRequest -Uri $mainAsset.browser_download_url -OutFile $mainZip

Write-Host "[3/4] Téléchargement des DLLs NVIDIA CUDA ($($cudaAsset.name))..." -ForegroundColor Cyan
Invoke-WebRequest -Uri $cudaAsset.browser_download_url -OutFile $cudaZip

# 3. Extraction dans le dossier bin
Write-Host "[4/4] Extraction des fichiers vers $TargetDir..." -ForegroundColor Cyan
Expand-Archive -Path $mainZip -DestinationPath $TargetDir -Force
Expand-Archive -Path $cudaZip -DestinationPath $TargetDir -Force

# 4. Nettoyage
Remove-Item -Path $mainZip -Force -ErrorAction SilentlyContinue
Remove-Item -Path $cudaZip -Force -ErrorAction SilentlyContinue

$serverExe = Join-Path $TargetDir "llama-server.exe"
if (Test-Path $serverExe) {
    Write-Host "`n======================================================" -ForegroundColor Green
    Write-Host " [SUCCÈS] llama.cpp CUDA installé avec succès !" -ForegroundColor Green
    Write-Host " Exécutable serveur : $serverExe" -ForegroundColor White
    Write-Host "======================================================" -ForegroundColor Green
} else {
    Write-Host "[ATTENTION] llama-server.exe non trouvé après extraction." -ForegroundColor Yellow
}
