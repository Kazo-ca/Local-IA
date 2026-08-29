<#
.SYNOPSIS
    Menu interactif principal pour gérer et démarrer les modèles LOCAL-IA pour VS Code Copilot.
.DESCRIPTION
    Fournit une interface console intuitive pour sélectionner un profil, démarrer les moteurs à la demande,
    surveiller les ressources matérielles et gérer les modèles.
#>

[CmdletBinding()]
param()

$Host.UI.RawUI.WindowTitle = "LOCAL-IA Launcher - VS Code Copilot"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = $scriptDir
$configFile = Join-Path $rootDir "config\config.json"

function Get-EngineStatusText {
    $ollamaProc = Get-Process -Name "ollama*" -ErrorAction SilentlyContinue
    $llamaProc = Get-Process -Name "llama-server" -ErrorAction SilentlyContinue

    $ollamaStatus = if ($ollamaProc) { "ACTIF (Port 11434)" } else { "ARRÊTÉ" }
    $llamaStatus = if ($llamaProc) { "ACTIF (Port 8080)" } else { "ARRÊTÉ" }

    return [PSCustomObject]@{
        Ollama = $ollamaStatus
        Llama = $llamaStatus
        IsOllamaRunning = [bool]$ollamaProc
        IsLlamaRunning = [bool]$llamaProc
    }
}

function Show-Header {
    Clear-Host
    $status = Get-EngineStatusText

    Write-Host "=========================================================================" -ForegroundColor Cyan
    Write-Host "             LOCAL-IA : GESTIONNAIRE DE MODÈLES POUR VS CODE            " -ForegroundColor Cyan
    Write-Host "    RTX 3060 12GB | 64GB RAM | Stockage NVMe (E:\ollama_models)             " -ForegroundColor DarkCyan
    Write-Host "=========================================================================" -ForegroundColor Cyan
    Write-Host "  Moteurs : Ollama: $($status.Ollama)  |  Llama.cpp: $($status.Llama)" -ForegroundColor DarkGray
    Write-Host ""
}

function Ensure-OllamaRunning {
    $startScript = Join-Path $rootDir "scripts\Start-OllamaService.ps1"
    & $startScript | Out-Null
}

function Get-InstalledOllamaModels {
    $proc = Get-Process -Name "ollama*" -ErrorAction SilentlyContinue
    if (-not $proc) {
        return @()
    }
    try {
        $res = Invoke-RestMethod -Uri "http://127.0.0.1:11434/api/tags" -Method Get -TimeoutSec 2 -ErrorAction Stop
        if ($res.models) {
            return $res.models.name
        }
    } catch {}
    return @()
}

function Show-ModelInstructions {
    param(
        [string]$ProfileKey,
        [string]$ProfileName,
        [string]$ModelName,
        [string]$Tier,
        [string]$SamplePrompt
    )

    Write-Host ""
    Write-Host "=========================================================================" -ForegroundColor Green
    Write-Host "  MODÈLE ACTIF : $ModelName" -ForegroundColor Green
    Write-Host "  PROFIL       : $ProfileName ($Tier)" -ForegroundColor Green
    Write-Host "  ENDPOINT     : http://localhost:11434/v1" -ForegroundColor Green
    Write-Host "=========================================================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "INSTRUCTIONS POUR UTILISER DANS VS CODE :" -ForegroundColor Yellow
    Write-Host "-------------------------------------------------------------------------" -ForegroundColor DarkGray
    Write-Host " 1. Dans VS Code Copilot Chat (Ctrl+Alt+I ou Cmd+Alt+I) :" -ForegroundColor White
    Write-Host "    - Le modèle actif est synchronisé sous le nom : " -NoNewline; Write-Host "$ModelName" -ForegroundColor Cyan
    Write-Host "    - Vous pouvez aussi utiliser l alias universel : " -NoNewline; Write-Host "local-ia-active" -ForegroundColor Cyan
    Write-Host ""
    Write-Host " 2. Pour les extensions IA (Continue, Roo Code, Cline, OpenAI Providers) :" -ForegroundColor White
    Write-Host "    - Provider : " -NoNewline; Write-Host "Ollama / OpenAI-Compatible" -ForegroundColor Cyan
    Write-Host "    - Base URL : " -NoNewline; Write-Host "http://localhost:11434/v1" -ForegroundColor Cyan
    Write-Host "    - API Key  : " -NoNewline; Write-Host "ollama" -ForegroundColor Cyan
    Write-Host "    - Modèle   : " -NoNewline; Write-Host "$ModelName" -ForegroundColor Cyan
    Write-Host ""
    Write-Host " EXEMPLE DE PROMPT À COPIER DANS COPILOT POUR CE PROFIL :" -ForegroundColor Yellow
    Write-Host "-------------------------------------------------------------------------" -ForegroundColor DarkGray
    Write-Host " $SamplePrompt" -ForegroundColor White
    Write-Host "-------------------------------------------------------------------------" -ForegroundColor DarkGray
    Write-Host ""
}

function Launch-Profile {
    param(
        [string]$ProfileKey,
        [string]$Tier
    )

    Show-Header
    Write-Host ">>> Démarrage du moteur Ollama à la demande..." -ForegroundColor Cyan
    Ensure-OllamaRunning

    if (-not (Test-Path $configFile)) {
        Write-Host "[ERREUR] Configuration introuvable." -ForegroundColor Red
        Pause
        return
    }

    $config = Get-Content $configFile -Raw | ConvertFrom-Json
    $tierKey = if ($Tier -eq "14b") { "fast_vram" } else { "max_accuracy" }
    $pData = $config.profiles.$ProfileKey
    $targetModel = $pData.tiers.$tierKey.custom_model_name
    $baseModel = $pData.tiers.$tierKey.base_model
    $modelfileRelative = $pData.tiers.$tierKey.modelfile
    $modelfileAbsolute = Join-Path $rootDir $modelfileRelative

    $installed = Get-InstalledOllamaModels
    if ($installed -notcontains $baseModel) {
        Write-Host "`n[TÉLÉCHARGEMENT] Téléchargement du modèle de base : $baseModel..." -ForegroundColor Cyan
        ollama pull $baseModel
    }

    if (Test-Path $modelfileAbsolute) {
        Write-Host "[COMPILATION] Création/Mise à jour du modèle personnalisé : $targetModel..." -ForegroundColor Green
        ollama create $targetModel -f $modelfileAbsolute
    }

    $setProfileScript = Join-Path $rootDir "scripts\Set-ActiveProfile.ps1"
    & $setProfileScript -Profile $ProfileKey -Tier $Tier

    Write-Host "`n[WARMUP] Chargement du modèle en mémoire..." -ForegroundColor Cyan
    try {
        $warmupBody = @{ model = $targetModel; prompt = ""; keep_alive = "60m" } | ConvertTo-Json
        Invoke-RestMethod -Uri "http://127.0.0.1:11434/api/generate" -Method Post -Body $warmupBody -TimeoutSec 10 -ErrorAction SilentlyContinue | Out-Null
        Write-Host "[OK] Modèle chaud et prêt en mémoire !" -ForegroundColor Green
    } catch {}

    $samplePrompt = ""
    switch ($ProfileKey) {
        "csharp" {
            $samplePrompt = "Génère un service C# .NET 9 OrderProcessingService avec architecture Clean, gestion d annulation CancellationToken, Entity Framework Core et tests unitaires xUnit."
        }
        "deep_reasoning" {
            $samplePrompt = "Démontre et détaille formellement l effet tunnel quantique à travers une barrière de potentiel rectangulaire 1D en utilisant l équation de Schrödinger indépendante du temps."
        }
        "agent_tools" {
            $samplePrompt = "Tu disposes des outils SearchFiles(path, pattern) et AnalyzeAst(filePath). Planifie et exécute la recherche de tous les contrôleurs C# non sécurisés."
        }
    }

    Show-ModelInstructions -ProfileKey $ProfileKey -ProfileName $pData.name -ModelName $targetModel -Tier $Tier -SamplePrompt $samplePrompt

    Write-Host "Appuyez sur une touche pour revenir au menu principal..." -ForegroundColor DarkGray
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
}

do {
    Show-Header
    $installedModels = Get-InstalledOllamaModels

    Write-Host " SÉLECTIONNEZ UN MODÈLE À DÉMARRER (Démarrage à la demande) :" -ForegroundColor Yellow
    Write-Host " -------------------------------------------------------------------------" -ForegroundColor DarkGray
    
    Write-Host " [1] C# et .NET Expert             - 32B (Précision Maximale | Hybride RAM/GPU)" -ForegroundColor White
    Write-Host " [2] C# et .NET Expert             - 14B (Fluide | 100% VRAM GPU)" -ForegroundColor White
    Write-Host ""
    Write-Host " [3] Réflexion Profonde (Math/Phys) - 32B (Précision Maximale | QwQ-32B + Tools)" -ForegroundColor White
    Write-Host " [4] Réflexion Profonde (Math/Phys) - 14B (Fluide | Qwen2.5 100% VRAM + Tools)" -ForegroundColor White
    Write-Host ""
    Write-Host " [5] Outils et Multi-Agents       - 32B (Précision Maximale | Function Calling)" -ForegroundColor White
    Write-Host " [6] Outils et Multi-Agents       - 14B (Fluide | 100% VRAM GPU)" -ForegroundColor White
    Write-Host ""
    Write-Host " -------------------------------------------------------------------------" -ForegroundColor DarkGray
    Write-Host " [L] Moteur Llama.cpp MoE (Experts CPU + GPU VRAM - Gain de Performance)" -ForegroundColor Green
    Write-Host " [S] Tableau de Bord et Statistiques (CPU, GPU, RAM, VRAM, Modèles)" -ForegroundColor Cyan
    Write-Host " [C] Vérifier / Synchroniser chatLanguageModels.json (VS Code)" -ForegroundColor Cyan
    Write-Host " [X] Arrêter un modèle / Arrêter les services (Libérer la mémoire)" -ForegroundColor Red
    Write-Host " -------------------------------------------------------------------------" -ForegroundColor DarkGray
    Write-Host " [7] Télécharger / Mettre à jour des modèles Ollama" -ForegroundColor DarkGray
    Write-Host " [8] Tester le modèle actuellement actif" -ForegroundColor DarkGray
    Write-Host " [Q] Quitter le Launcher" -ForegroundColor Red
    Write-Host " -------------------------------------------------------------------------" -ForegroundColor DarkGray
    
    $selection = Read-Host "Votre choix"

    switch ($selection) {
        "1" { Launch-Profile -ProfileKey "csharp" -Tier "32b" }
        "2" { Launch-Profile -ProfileKey "csharp" -Tier "14b" }
        "3" { Launch-Profile -ProfileKey "deep_reasoning" -Tier "32b" }
        "4" { Launch-Profile -ProfileKey "deep_reasoning" -Tier "14b" }
        "5" { Launch-Profile -ProfileKey "agent_tools" -Tier "32b" }
        "6" { Launch-Profile -ProfileKey "agent_tools" -Tier "14b" }
        "L" {
            Show-Header
            Write-Host "MODÈLES HAUTE PERFORMANCE LLAMA.CPP (MoE CPU/GPU SPLIT) :" -ForegroundColor Yellow
            Write-Host " [1] DeepSeek-Coder-V2 Lite MoE (16B / 2.4B actif - Spécialiste C# ultra-rapide)" -ForegroundColor White
            Write-Host " [2] Qwen3.8-27B (Programmation et Agentic Coding)" -ForegroundColor White
            Write-Host " [3] Qwen3.6-35B-A3B MoE (35B / 3B actif - Raisonnement Mathématique et Physique)" -ForegroundColor White
            Write-Host " [4] QwQ-32B (Raisonnement Profond Maths et Physique)" -ForegroundColor White
            Write-Host " [5] Mixtral-8x7B MoE (46.7B / 12.9B actif - Agents et Tool Use)" -ForegroundColor White
            Write-Host " [6] Qwen2.5-Coder-32B (Dense C# 32B)" -ForegroundColor White
            Write-Host " [7] Installer / Mettre à jour Llama.cpp CUDA 12.4" -ForegroundColor Cyan
            Write-Host " [R] Retour" -ForegroundColor Gray
            $lChoice = Read-Host "Votre choix"
            $llamaScript = Join-Path $rootDir "scripts\Start-LlamaServer.ps1"
            switch ($lChoice) {
                "1" { & $llamaScript -Model "deepseek-coder-v2-lite" -GpuLayers 24 -ContextSize 65536 }
                "2" { & $llamaScript -Model "qwen3.8-27b" -GpuLayers 24 -ContextSize 65536 }
                "3" { & $llamaScript -Model "qwen3.6-35b-a3b" -GpuLayers 24 -ContextSize 65536 }
                "4" { & $llamaScript -Model "qwq-32b" -GpuLayers 24 -ContextSize 65536 }
                "5" { & $llamaScript -Model "mixtral-8x7b" -GpuLayers 20 -ContextSize 65536 }
                "6" { & $llamaScript -Model "qwen2.5-coder-32b" -GpuLayers 24 -ContextSize 65536 }
                "7" {
                    $instLlama = Join-Path $rootDir "scripts\Install-LlamaCpp.ps1"
                    & $instLlama
                }
            }
            Write-Host "`nAppuyez sur une touche pour continuer..." -ForegroundColor Gray
            $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
        }
        "S" {
            $dashScript = Join-Path $rootDir "scripts\Show-Dashboard.ps1"
            & $dashScript
        }
        "C" {
            $syncScript = Join-Path $rootDir "scripts\Sync-ChatLanguageModels.ps1"
            & $syncScript
        }
        "X" {
            Show-Header
            Write-Host "GESTIONNAIRE D ARRÊT ET LIBÉRATION MÉMOIRE :" -ForegroundColor Yellow
            Write-Host " [1] Décharger les modèles en mémoire (Libérer VRAM et RAM)" -ForegroundColor White
            Write-Host " [2] Arrêter le service Ollama" -ForegroundColor White
            Write-Host " [3] Arrêter le service Llama.cpp" -ForegroundColor White
            Write-Host " [4] Tout arrêter (Ollama + Llama.cpp)" -ForegroundColor Red
            Write-Host " [5] Gérer le démarrage automatique Windows (Désactiver/Activer)" -ForegroundColor Cyan
            Write-Host " [R] Retour" -ForegroundColor Gray
            $xChoice = Read-Host "Choix"
            $stopScript = Join-Path $rootDir "scripts\Stop-Services.ps1"
            $autoScript = Join-Path $rootDir "scripts\Configure-Autostart.ps1"
            switch ($xChoice) {
                "1" { & $stopScript -Action "unload-models" }
                "2" { & $stopScript -Action "stop-ollama" }
                "3" { & $stopScript -Action "stop-llama" }
                "4" { & $stopScript -Action "stop-all" }
                "5" {
                    Write-Host "`nOptions Démarrage Windows :" -ForegroundColor Yellow
                    Write-Host " [D] Désactiver le lancement au démarrage de Windows" -ForegroundColor White
                    Write-Host " [E] Activer le lancement au démarrage de Windows" -ForegroundColor White
                    $asChoice = Read-Host "Choix"
                    if ($asChoice -match "^[dD]") { & $autoScript -Action disable }
                    elseif ($asChoice -match "^[eE]") { & $autoScript -Action enable }
                }
            }
            Write-Host "`nAppuyez sur une touche pour continuer..." -ForegroundColor Gray
            $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
        }
        "7" {
            Show-Header
            Ensure-OllamaRunning
            Write-Host "OPTIONS DE TELECHARGEMENT OLLAMA :" -ForegroundColor Yellow
            Write-Host " [1] Installer tous les modèles 14B (~27 Go total - 100% VRAM)" -ForegroundColor White
            Write-Host " [2] Installer tous les modèles 32B (~60 Go total - Exactitude Max)" -ForegroundColor White
            Write-Host " [3] Tout installer (14B et 32B)" -ForegroundColor White
            Write-Host " [4] Installer uniquement C# 32B" -ForegroundColor White
            Write-Host " [R] Retour" -ForegroundColor Gray
            $instChoice = Read-Host "Choix"
            $instScript = Join-Path $rootDir "scripts\Install-Models.ps1"
            switch ($instChoice) {
                "1" { & $instScript -Tier 14b }
                "2" { & $instScript -Tier 32b }
                "3" { & $instScript -Tier All }
                "4" { & $instScript -Profile csharp -Tier 32b }
            }
            Write-Host "`nAppuyez sur une touche pour continuer..." -ForegroundColor Gray
            $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
        }
        "8" {
            Show-Header
            $testScript = Join-Path $rootDir "scripts\Test-Models.ps1"
            & $testScript -Profile "active"
            Write-Host "`nAppuyez sur une touche pour continuer..." -ForegroundColor Gray
            $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
        }
    }
} while ($selection -notmatch "^[qQ]")

Write-Host "`nAu revoir !" -ForegroundColor Green
