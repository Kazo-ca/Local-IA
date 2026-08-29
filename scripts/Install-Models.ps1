<#
.SYNOPSIS
    Installe et compile les modèles spécialisés (14B et/ou 32B) dans Ollama.
.DESCRIPTION
    Télécharge les modèles de base depuis le registre Ollama et crée les modèles personnalisés avec les prompts système.
.EXAMPLE
    .\Install-Models.ps1 -Tier 14b
    .\Install-Models.ps1 -Tier 32b
    .\Install-Models.ps1 -Tier All
    .\Install-Models.ps1 -Profile csharp -Tier 32b
#>

[CmdletBinding()]
param(
    [ValidateSet("14b", "32b", "All")]
    [string]$Tier = "14b",

    [ValidateSet("all", "csharp", "deep_reasoning", "agent_tools")]
    [string]$Profile = "all"
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir
$configFile = Join-Path $rootDir "config\config.json"

if (-not (Test-Path $configFile)) {
    Write-Host "[ERREUR] Fichier de configuration non trouvé : $configFile" -ForegroundColor Red
    return
}

$config = Get-Content $configFile -Raw | ConvertFrom-Json

# 1. S'assurer que le service Ollama est actif
Write-Host "[1/3] Vérification de l'état du serveur Ollama..." -ForegroundColor Cyan
& (Join-Path $scriptDir "Start-OllamaService.ps1")

# 2. Déterminer les profils et les tiers à installer
$profilesToProcess = @()
if ($Profile -eq "all") {
    $profilesToProcess = $config.profiles.PSObject.Properties.Name
} else {
    $profilesToProcess = @($Profile)
}

$tiersToProcess = @()
if ($Tier -eq "All") {
    $tiersToProcess = @("fast_vram", "max_accuracy")
} elseif ($Tier -eq "14b") {
    $tiersToProcess = @("fast_vram")
} elseif ($Tier -eq "32b") {
    $tiersToProcess = @("max_accuracy")
}

Write-Host "======================================================" -ForegroundColor Cyan
Write-Host " LOCAL-IA : Téléchargement et Création des Modèles   " -ForegroundColor Cyan
Write-Host " Profils cibles : $($profilesToProcess -join ', ')" -ForegroundColor Yellow
Write-Host " Tiers cibles   : $($tiersToProcess -join ', ')" -ForegroundColor Yellow
Write-Host "======================================================" -ForegroundColor Cyan

foreach ($pKey in $profilesToProcess) {
    $pData = $config.profiles.$pKey
    Write-Host "`n>>> Traitement du profil : $($pData.name)" -ForegroundColor Magenta

    foreach ($tKey in $tiersToProcess) {
        $tierData = $pData.tiers.$tKey
        if ($null -eq $tierData) { continue }

        $baseModel = $tierData.base_model
        $customModel = $tierData.custom_model_name
        $modelfileRelative = $tierData.modelfile
        $modelfileAbsolute = Join-Path $rootDir $modelfileRelative

        Write-Host "`n[TÉLÉCHARGEMENT] Modèle de base : $baseModel ($($tierData.vram_offload))..." -ForegroundColor Cyan
        ollama pull $baseModel

        if (Test-Path $modelfileAbsolute) {
            Write-Host "[COMPILATION] Création du modèle personnalisé : $customModel..." -ForegroundColor Green
            ollama create $customModel -f $modelfileAbsolute
            Write-Host "[OK] Modèle '$customModel' créé et prêt !" -ForegroundColor Green
        } else {
            Write-Host "[ATTENTION] Modelfile introuvable : $modelfileAbsolute" -ForegroundColor Yellow
        }
    }
}

Write-Host "`n======================================================" -ForegroundColor Cyan
Write-Host " [TERMINÉ] Liste des modèles disponibles localement : " -ForegroundColor Cyan
Write-Host "======================================================" -ForegroundColor Cyan
ollama list
