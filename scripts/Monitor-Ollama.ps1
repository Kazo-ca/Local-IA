<#
.SYNOPSIS
    Moniteur d'activité en temps réel pour Ollama et la carte graphique GPU.
.DESCRIPTION
    Affiche les modèles chargés en mémoire VRAM/RAM, l'utilisation GPU NVIDIA et les logs en direct.
#>

[CmdletBinding()]
param(
    [ValidateSet("dashboard", "logs", "gpu")]
    [string]$Mode = "dashboard"
)

$logPath = "$env:LOCALAPPDATA\Ollama\server.log"

if ($Mode -eq "logs") {
    if (Test-Path $logPath) {
        Write-Host ">>> Affichage des logs Ollama en temps réel (Ctrl+C pour quitter) :`n" -ForegroundColor Cyan
        Get-Content -Path $logPath -Tail 50 -Wait
    } else {
        Write-Host "[INFO] Fichier de log introuvable à l'emplacement : $logPath" -ForegroundColor Yellow
    }
    return
}

if ($Mode -eq "gpu") {
    Write-Host ">>> Surveillance NVIDIA GPU (Ctrl+C pour quitter) :`n" -ForegroundColor Cyan
    nvidia-smi -l 1
    return
}

# Mode Dashboard interactif
Clear-Host
Write-Host "╔═══════════════════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║             📊 LOCAL-IA : MONITEUR D'ACTIVITÉ EN TEMPS RÉEL               ║" -ForegroundColor Cyan
Write-Host "╚═══════════════════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# 1. Modèles actuellement chargés en mémoire
Write-Host "🧠 MODÈLES ACTUELLEMENT CHARGÉS EN MÉMOIRE (ollama ps) :" -ForegroundColor Yellow
Write-Host "─────────────────────────────────────────────────────────────────────────────" -ForegroundColor DarkGray
ollama ps
Write-Host ""

# 2. Utilisation GPU / VRAM
Write-Host "🎮 UTILISATION GPU & VRAM (NVIDIA RTX 3060) :" -ForegroundColor Yellow
Write-Host "─────────────────────────────────────────────────────────────────────────────" -ForegroundColor DarkGray
nvidia-smi --query-gpu=name,utilization.gpu,memory.used,memory.total,temperature.gpu --format=table
Write-Host ""

# 3. Dernières requêtes du serveur
Write-Host "📜 DERNIÈRES LIGNES DE LOGS OLLAMA :" -ForegroundColor Yellow
Write-Host "─────────────────────────────────────────────────────────────────────────────" -ForegroundColor DarkGray
if (Test-Path $logPath) {
    Get-Content -Path $logPath -Tail 10 | ForEach-Object {
        if ($_ -match "error") {
            Write-Host $_ -ForegroundColor Red
        } elseif ($_ -match "HTTP" -or $_ -match "generate" -or $_ -match "chat") {
            Write-Host $_ -ForegroundColor Green
        } else {
            Write-Host $_ -ForegroundColor Gray
        }
    }
} else {
    Write-Host "Aucun fichier de log détecté." -ForegroundColor DarkGray
}

Write-Host "`n─────────────────────────────────────────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host "Commandes directes utiles :" -ForegroundColor Cyan
Write-Host " • Voir les logs en direct  : Get-Content `"$logPath`" -Wait -Tail 30" -ForegroundColor White
Write-Host " • Suivre le GPU en continu : nvidia-smi -l 1" -ForegroundColor White
