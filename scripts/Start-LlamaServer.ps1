<#
.SYNOPSIS
    Démarre le serveur haute performance llama.cpp avec offloading MoE (GPU VRAM + CPU RAM).
.DESCRIPTION
    Exécute llama-server.exe avec accélération NVIDIA CUDA, Flash Attention et optimisation multithread.
.EXAMPLE
    .\Start-LlamaServer.ps1 -Model deepseek-coder-v2-lite -GpuLayers 24
    .\Start-LlamaServer.ps1 -Model qwq-32b -GpuLayers 24
#>

[CmdletBinding()]
param(
    [ValidateSet("deepseek-coder-v2-lite", "qwq-32b", "qwen2.5-coder-32b", "mixtral-8x7b", "qwen3.8-27b", "qwen3.6-35b-a3b")]
    [string]$Model = "qwq-32b",

    [int]$GpuLayers = 24,
    [int]$Port = 8080,
    [int]$ContextSize = 65536,
    [int]$Threads = 16
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir
$binDir = Join-Path $rootDir "bin\llama-cpp"
$serverExe = Join-Path $binDir "llama-server.exe"

# 1. Vérification / Installation de llama.cpp CUDA
if (-not (Test-Path $serverExe)) {
    Write-Host "[INFO] llama.cpp n est pas encore installé. Lancement de l installation CUDA..." -ForegroundColor Yellow
    & (Join-Path $scriptDir "Install-LlamaCpp.ps1")
}

if (-not (Test-Path $serverExe)) {
    Write-Host "[ERREUR] Impossible de trouver llama-server.exe dans $binDir" -ForegroundColor Red
    return
}

# 2. Vérification / Téléchargement du modèle GGUF
$downloadScript = Join-Path $scriptDir "Download-GGUF.ps1"
$modelGgufPath = & $downloadScript -Model $Model

if ([string]::IsNullOrWhiteSpace($modelGgufPath) -or (-not (Test-Path $modelGgufPath))) {
    Write-Host "[ERREUR] Fichier GGUF introuvable ou téléchargement incomplet : $modelGgufPath" -ForegroundColor Red
    return
}

# 3. Arrêter toute instance précédente de llama-server
$existing = Get-Process -Name "llama-server" -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "[INFO] Arrêt de l ancienne instance de llama-server (PID: $($existing.Id))..." -ForegroundColor Yellow
    Stop-Process -Name "llama-server" -Force
    Start-Sleep -Seconds 1
}

Write-Host "======================================================" -ForegroundColor Cyan
Write-Host " LOCAL-IA : Démarrage du Serveur Llama.cpp (MoE CUDA) " -ForegroundColor Cyan
Write-Host " Modèle GGUF     : $(Split-Path $modelGgufPath -Leaf)" -ForegroundColor Yellow
Write-Host " Couches GPU     : $GpuLayers (VRAM RTX 3060 12GB)" -ForegroundColor Yellow
Write-Host " CPU Threads     : $Threads (AMD Ryzen 7 3700X)" -ForegroundColor Yellow
Write-Host " Contexte        : $ContextSize tokens" -ForegroundColor Yellow
Write-Host " Endpoint OpenAI : http://127.0.0.1:${Port}/v1" -ForegroundColor Green
Write-Host "======================================================" -ForegroundColor Cyan

# 4. Lancement du serveur avec Flash Attention et offloading
$argsList = @(
    "-m", "`"$modelGgufPath`"",
    "-ngl", "$GpuLayers",
    "-t", "$Threads",
    "-c", "$ContextSize",
    "--flash-attn", "on",
    "--host", "127.0.0.1",
    "--port", "$Port"
)

$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = $serverExe
$startInfo.Arguments = $argsList -join " "
$startInfo.WorkingDirectory = $binDir
$startInfo.UseShellExecute = $true
$startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Minimized

$process = [System.Diagnostics.Process]::Start($startInfo)
Write-Host "[INFO] Serveur Llama.cpp lancé (PID: $($process.Id)) ! Chargement des poids en mémoire..." -ForegroundColor Green

# 5. Attente du chargement complet du modèle (statut 200 OK)
$retry = 0
$ready = $false
Write-Host -NoNewline "[CHARGEMENT] " -ForegroundColor Cyan

while (-not $ready -and $retry -lt 30) {
    Start-Sleep -Seconds 2
    $retry++
    Write-Host -NoNewline "." -ForegroundColor Yellow
    try {
        $res = Invoke-RestMethod -Uri "http://127.0.0.1:${Port}/v1/models" -Method Get -TimeoutSec 2 -ErrorAction Stop
        if ($res.models -or $res.data) {
            $ready = $true
        }
    } catch {
        # Si 503 "Loading model", continue d'attendre
    }
}
Write-Host ""

if ($ready) {
    Write-Host "=========================================================================" -ForegroundColor Green
    Write-Host "   SERVEUR LLAMA.CPP PRÊT SUR : http://localhost:${Port}/v1              " -ForegroundColor Green
    Write-Host "   MODÈLE CHARGÉ EN VRAM + RAM (PID: $($process.Id))                     " -ForegroundColor Green
    Write-Host "=========================================================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "CONFIGURATION VS CODE (chatLanguageModels.json) :" -ForegroundColor Yellow
    Write-Host "-------------------------------------------------------------------------" -ForegroundColor DarkGray
    Write-Host "Ajoutez ou sélectionnez ce modèle dans VS Code Copilot :" -ForegroundColor Cyan
    Write-Host "{ 'name': 'LlamaCpp-MoE', 'vendor': 'customendpoint', 'models': [ { 'id': '$Model', 'url': 'http://127.0.0.1:${Port}/v1' } ] }" -ForegroundColor Cyan
    Write-Host "-------------------------------------------------------------------------" -ForegroundColor DarkGray
} else {
    Write-Host "[ATTENTION] Le modèle est encore en cours de lecture depuis le SSD NVMe." -ForegroundColor Yellow
}
