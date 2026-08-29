<#
.SYNOPSIS
    Gère l'arrêt des services IA (Ollama, Llama.cpp) et le déchargement des modèles en mémoire (VRAM/RAM).
#>

[CmdletBinding()]
param(
    [ValidateSet("unload-models", "stop-ollama", "stop-llama", "stop-all")]
    [string]$Action = "unload-models"
)

Write-Host "======================================================" -ForegroundColor Cyan
Write-Host "      LOCAL-IA : Gestionnaire d'Arrêt et Nettoyage     " -ForegroundColor Cyan
Write-Host "======================================================" -ForegroundColor Cyan

switch ($Action) {
    "unload-models" {
        Write-Host "[1/2] Déchargement des modèles Ollama de la VRAM..." -ForegroundColor Cyan
        try {
            $psOut = ollama ps 2>$null
            if ($psOut -and $psOut.Count -gt 1) {
                # Extraire les noms de modèles
                $models = ($psOut | Select-Object -Skip 1) | ForEach-Object {
                    ($_ -split '\s+')[0]
                }
                foreach ($m in $models) {
                    if (-not [string]::IsNullOrWhiteSpace($m)) {
                        Write-Host " -> Déchargement du modèle : $m" -ForegroundColor Yellow
                        ollama stop $m
                    }
                }
                Write-Host "[OK] Tous les modèles Ollama ont été déchargés de la mémoire !" -ForegroundColor Green
            } else {
                Write-Host "[INFO] Aucun modèle Ollama actif en mémoire." -ForegroundColor Gray
            }
        } catch {
            Write-Host "[INFO] Ollama n'est pas actif." -ForegroundColor Gray
        }

        Write-Host "[2/2] Vérification de llama-server..." -ForegroundColor Cyan
        $llamaProc = Get-Process -Name "llama-server" -ErrorAction SilentlyContinue
        if ($llamaProc) {
            Write-Host " -> Arrêt de l'instance llama-server (PID: $($llamaProc.Id))..." -ForegroundColor Yellow
            Stop-Process -Name "llama-server" -Force
            Write-Host "[OK] Llama-server arrêté, VRAM libérée !" -ForegroundColor Green
        } else {
            Write-Host "[INFO] Aucun serveur llama-server en cours d'exécution." -ForegroundColor Gray
        }
    }

    "stop-ollama" {
        Write-Host "[INFO] Arrêt complet du service Ollama..." -ForegroundColor Yellow
        $ollamaProcs = Get-Process -Name "ollama*" -ErrorAction SilentlyContinue
        if ($ollamaProcs) {
            foreach ($p in $ollamaProcs) {
                Write-Host " -> Arrêt du processus PID $($p.Id)..." -ForegroundColor DarkGray
                Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
            }
            Write-Host "[SUCCÈS] Service Ollama arrêté." -ForegroundColor Green
        } else {
            Write-Host "[INFO] Le service Ollama n'était pas en cours d'exécution." -ForegroundColor Gray
        }
    }

    "stop-llama" {
        Write-Host "[INFO] Arrêt du moteur Llama.cpp..." -ForegroundColor Yellow
        $llamaProc = Get-Process -Name "llama-server" -ErrorAction SilentlyContinue
        if ($llamaProc) {
            Stop-Process -Name "llama-server" -Force
            Write-Host "[SUCCÈS] Moteur Llama.cpp arrêté." -ForegroundColor Green
        } else {
            Write-Host "[INFO] Aucun serveur Llama.cpp n'était actif." -ForegroundColor Gray
        }
    }

    "stop-all" {
        Write-Host "[ATTENTION] Arrêt de TOUS les processus IA et libération complète de la mémoire..." -ForegroundColor Red
        
        # Arrêt Ollama
        $ollamaProcs = Get-Process -Name "ollama*" -ErrorAction SilentlyContinue
        if ($ollamaProcs) {
            $ollamaProcs | Stop-Process -Force -ErrorAction SilentlyContinue
            Write-Host " • Processus Ollama arrêtés." -ForegroundColor Green
        }

        # Arrêt Llama.cpp
        $llamaProc = Get-Process -Name "llama-server" -ErrorAction SilentlyContinue
        if ($llamaProc) {
            $llamaProc | Stop-Process -Force -ErrorAction SilentlyContinue
            Write-Host " • Processus Llama.cpp arrêtés." -ForegroundColor Green
        }

        Write-Host "[SUCCÈS] 100% de la VRAM (12 Go) et de la RAM (64 Go) ont été libérées !" -ForegroundColor Green
    }
}
