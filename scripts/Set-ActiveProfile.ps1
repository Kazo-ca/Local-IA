<#
.SYNOPSIS
    Active un profil spécifique et met à jour l'alias de modèle actif pour VS Code / Copilot.
.DESCRIPTION
    Crée l'alias 'local-ia-active' dans Ollama et met à jour les paramètres VS Code dans .vscode/settings.json.
.EXAMPLE
    .\Set-ActiveProfile.ps1 -Profile csharp -Tier 32b
    .\Set-ActiveProfile.ps1 -Profile deep_reasoning -Tier 32b
    .\Set-ActiveProfile.ps1 -Profile agent_tools -Tier 14b
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("csharp", "deep_reasoning", "agent_tools")]
    [string]$Profile,

    [ValidateSet("14b", "32b")]
    [string]$Tier = "32b"
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir
$configFile = Join-Path $rootDir "config\config.json"
$vscodeSettingsPath = Join-Path $rootDir ".vscode\settings.json"

if (-not (Test-Path $configFile)) {
    Write-Host "[ERREUR] Fichier de configuration introuvable : $configFile" -ForegroundColor Red
    return
}

$config = Get-Content $configFile -Raw | ConvertFrom-Json
$tierKey = if ($Tier -eq "14b") { "fast_vram" } else { "max_accuracy" }
$targetModel = $config.profiles.$Profile.tiers.$tierKey.custom_model_name
$profileName = $config.profiles.$Profile.name

Write-Host "======================================================" -ForegroundColor Cyan
Write-Host " LOCAL-IA : Activation du Profil '$profileName'       " -ForegroundColor Cyan
Write-Host " Modèle cible : $targetModel (Tier: $Tier)            " -ForegroundColor Yellow
Write-Host "======================================================" -ForegroundColor Cyan

# 1. Mise à jour de l'alias Ollama
Write-Host "[1/2] Création de l'alias 'local-ia-active' vers '$targetModel'..." -ForegroundColor Cyan
try {
    ollama cp $targetModel local-ia-active
    Write-Host "[OK] Alias Ollama 'local-ia-active' mis à jour !" -ForegroundColor Green
} catch {
    Write-Host "[AVERTISSEMENT] Assurez-vous que le modèle $targetModel est bien installé." -ForegroundColor Yellow
}

# 2. Mise à jour des paramètres VS Code
Write-Host "[2/2] Configuration de VS Code (.vscode/settings.json)..." -ForegroundColor Cyan

$vscodeDir = Split-Path -Parent $vscodeSettingsPath
if (-not (Test-Path $vscodeDir)) {
    New-Item -Path $vscodeDir -ItemType Directory -Force | Out-Null
}

$settings = @{}
if (Test-Path $vscodeSettingsPath) {
    try {
        $settings = Get-Content $vscodeSettingsPath -Raw | ConvertFrom-Json -AsHashtable
    } catch {
        $settings = @{}
    }
}

# Mise à jour des clés de configuration pour l'extension Ollama et Copilot
$settings["ollama.endpoint"] = "http://127.0.0.1:11434"
$settings["localia.activeProfile"] = $Profile
$settings["localia.activeTier"] = $Tier
$settings["localia.activeModel"] = $targetModel
$settings["localia.endpoint"] = "http://127.0.0.1:11434/v1"

$jsonContent = $settings | ConvertTo-Json -Depth 10
Set-Content -Path $vscodeSettingsPath -Value $jsonContent -Encoding UTF8

Write-Host "[SUCCÈS] Profil '$profileName' ($Tier) activé avec succès pour VS Code !" -ForegroundColor Green
