<#
.SYNOPSIS
    Démarre le serveur local Ollama avec le stockage configuré sur le SSD NVMe (E:\ollama_models).
.DESCRIPTION
    Configure les variables d'environnement optimales (OLLAMA_MODELS, OLLAMA_HOST, OLLAMA_KEEP_ALIVE)
    et lance le processus Ollama en arrière-plan s'il n'est pas déjà actif.
#>

[CmdletBinding()]
param(
    [string]$ModelsPath = "E:\ollama_models",
    [string]$HostAddress = "127.0.0.1:11434",
    [string]$KeepAlive = "60m"
)

Write-Host "======================================================" -ForegroundColor Cyan
Write-Host "      LOCAL-IA : Démarrage du Service Ollama          " -ForegroundColor Cyan
Write-Host "======================================================" -ForegroundColor Cyan

# 1. Vérification et création du dossier de modèles NVMe
if (-not (Test-Path $ModelsPath)) {
    Write-Host "[INFO] Création du répertoire de modèles sur : $ModelsPath" -ForegroundColor Yellow
    New-Item -Path $ModelsPath -ItemType Directory -Force | Out-Null
}

# 2. Configuration des variables d'environnement pour la session et l'utilisateur
$env:OLLAMA_MODELS = $ModelsPath
$env:OLLAMA_HOST = $HostAddress
$env:OLLAMA_KEEP_ALIVE = $KeepAlive
$env:OLLAMA_NUM_PARALLEL = "2"

[System.Environment]::SetEnvironmentVariable('OLLAMA_MODELS', $ModelsPath, 'User')
[System.Environment]::SetEnvironmentVariable('OLLAMA_HOST', $HostAddress, 'User')

Write-Host "[OK] Dossier modèles : $env:OLLAMA_MODELS" -ForegroundColor Green
Write-Host "[OK] Adresse écoute  : $env:OLLAMA_HOST" -ForegroundColor Green

# 3. Vérifier si Ollama est déjà en cours d'exécution
$existingProcess = Get-Process -Name "ollama*" -ErrorAction SilentlyContinue
if ($existingProcess) {
    Write-Host "[INFO] Le processus Ollama est déjà actif (PID: $($existingProcess[0].Id))." -ForegroundColor Green
} else {
    Write-Host "[INFO] Démarrage du serveur Ollama..." -ForegroundColor Yellow
    Start-Process -FilePath "ollama" -ArgumentList "serve" -WindowStyle Hidden
    Start-Sleep -Seconds 3
}

# 4. Test de connexion HTTP
$maxRetries = 10
$retryCount = 0
$connected = $false

while (-not $connected -and $retryCount -lt $maxRetries) {
    try {
        $response = Invoke-RestMethod -Uri "http://${HostAddress}/api/tags" -Method Get -TimeoutSec 2 -ErrorAction Stop
        $connected = $true
        Write-Host "[SUCCÈS] Serveur Ollama opérationnel et prêt à l'adresse http://${HostAddress} !" -ForegroundColor Green
    } catch {
        $retryCount++
        Start-Sleep -Seconds 1
    }
}

if (-not $connected) {
    Write-Host "[ERREUR] Impossible de joindre le serveur Ollama après plusieurs tentatives." -ForegroundColor Red
}
