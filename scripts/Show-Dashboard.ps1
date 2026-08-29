<#
.SYNOPSIS
    Tableau de bord complet en temps réel affichant les statistiques matérielles (CPU, GPU, RAM, VRAM),
    l'état des serveurs IA (Ollama, Llama.cpp) et les modèles chargés en mémoire.
#>

[CmdletBinding()]
param()

function Get-HardwareStats {
    # 1. RAM
    $os = Get-CimInstance Win32_OperatingSystem
    $totalRamGB = [math]::Round($os.TotalVisibleMemorySize / 1MB, 2)
    $freeRamGB = [math]::Round($os.FreePhysicalMemory / 1MB, 2)
    $usedRamGB = [math]::Round($totalRamGB - $freeRamGB, 2)
    $ramPct = [math]::Round(($usedRamGB / $totalRamGB) * 100, 1)

    # 2. CPU
    $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
    $cpuName = $cpu.Name
    $cpuLoad = $cpu.LoadPercentage
    if ($null -eq $cpuLoad) { $cpuLoad = 0 }

    # 3. GPU & VRAM (NVIDIA)
    $gpuName = "NVIDIA GPU"
    $vramUsedMB = 0
    $vramTotalMB = 12288
    $gpuLoad = 0
    $gpuTemp = 0
    try {
        $smi = nvidia-smi --query-gpu=name,memory.used,memory.total,utilization.gpu,temperature.gpu --format=csv,noheader,nounits 2>$null
        if ($smi) {
            $parts = $smi -split ','
            $gpuName = $parts[0].Trim()
            $vramUsedMB = [int]$parts[1].Trim()
            $vramTotalMB = [int]$parts[2].Trim()
            $gpuLoad = [int]$parts[3].Trim()
            $gpuTemp = [int]$parts[4].Trim()
        }
    } catch {}
    $vramPct = [math]::Round(($vramUsedMB / $vramTotalMB) * 100, 1)

    # 4. Disques
    $driveE = Get-PSDrive E -ErrorAction SilentlyContinue
    $driveC = Get-PSDrive C -ErrorAction SilentlyContinue

    $eFree = if ($driveE) { [math]::Round($driveE.Free / 1GB, 1) } else { 0 }
    $cFree = if ($driveC) { [math]::Round($driveC.Free / 1GB, 1) } else { 0 }

    return [PSCustomObject]@{
        TotalRamGB = $totalRamGB
        UsedRamGB  = $usedRamGB
        RamPct     = $ramPct
        CpuName    = $cpuName
        CpuLoad    = $cpuLoad
        GpuName    = $gpuName
        VramUsedMB = $vramUsedMB
        VramTotalMB = $vramTotalMB
        VramPct    = $vramPct
        GpuLoad    = $gpuLoad
        GpuTemp    = $gpuTemp
        DriveE_FreeGB = $eFree
        DriveC_FreeGB = $cFree
    }
}

function Render-ProgressBar {
    param([double]$Percent, [int]$Width = 24)
    $filled = [int]([math]::Round(($Percent / 100) * $Width))
    if ($filled -gt $Width) { $filled = $Width }
    if ($filled -lt 0) { $filled = 0 }
    $empty = $Width - $filled
    $bar = ('█' * $filled) + ('░' * $empty)
    return $bar
}

function Show-DashboardView {
    Clear-Host
    $stats = Get-HardwareStats

    Write-Host '╔═══════════════════════════════════════════════════════════════════════════╗' -ForegroundColor Cyan
    Write-Host '║             📊 LOCAL-IA : TABLEAU DE BORD & STATS EN DIRECT               ║' -ForegroundColor Cyan
    Write-Host '╚═══════════════════════════════════════════════════════════════════════════╝' -ForegroundColor Cyan
    Write-Host ''

    # --- SECTION MATÉRIEL ---
    Write-Host '💻 STATISTIQUES MATÉRIELLES :' -ForegroundColor Yellow
    Write-Host '─────────────────────────────────────────────────────────────────────────────' -ForegroundColor DarkGray

    # GPU
    $gpuColor = if ($stats.VramPct -gt 85) { 'Red' } elseif ($stats.VramPct -gt 60) { 'Yellow' } else { 'Green' }
    $vramBar = Render-ProgressBar -Percent $stats.VramPct
    Write-Host '🎮 GPU  : ' -NoNewline; Write-Host "$($stats.GpuName) " -NoNewline -ForegroundColor White
    Write-Host "[Charge: $($stats.GpuLoad)% | Temp: $($stats.GpuTemp)°C]" -ForegroundColor DarkGray
    Write-Host "   VRAM : [$vramBar] $($stats.VramUsedMB) MB / $($stats.VramTotalMB) MB ($($stats.VramPct)%)" -ForegroundColor $gpuColor

    # RAM
    $ramColor = if ($stats.RamPct -gt 85) { 'Red' } elseif ($stats.RamPct -gt 60) { 'Yellow' } else { 'Green' }
    $ramBar = Render-ProgressBar -Percent $stats.RamPct
    Write-Host "🧠 RAM  : [$ramBar] $($stats.UsedRamGB) GB / $($stats.TotalRamGB) GB ($($stats.RamPct)%)" -ForegroundColor $ramColor

    # CPU
    $cpuBar = Render-ProgressBar -Percent $stats.CpuLoad
    Write-Host "⚡ CPU  : [$cpuBar] $($stats.CpuLoad)% ($($stats.CpuName))" -ForegroundColor Cyan

    # Stockage
    Write-Host "💾 DISK : NVMe (E:) : $($stats.DriveE_FreeGB) GB libres | System (C:) : $($stats.DriveC_FreeGB) GB libres" -ForegroundColor Green
    Write-Host ''

    # --- SECTION SERVICES IA ---
    Write-Host '🚀 ÉTAT DES SERVEURS IA :' -ForegroundColor Yellow
    Write-Host '─────────────────────────────────────────────────────────────────────────────' -ForegroundColor DarkGray

    # Ollama Service
    $ollamaProc = Get-Process -Name "ollama*" -ErrorAction SilentlyContinue
    if ($ollamaProc) {
        Write-Host ' • Ollama Engine    : ' -NoNewline
        Write-Host '🟢 ACTIF ' -ForegroundColor Green -NoNewline
        Write-Host "(PID: $($ollamaProc[0].Id) | Port: 11434 | NVMe: E:\ollama_models)" -ForegroundColor DarkGray
    } else {
        Write-Host ' • Ollama Engine    : ' -NoNewline
        Write-Host '⚪ ARRÊTÉ' -ForegroundColor DarkGray
    }

    # Llama.cpp Service
    $llamaProc = Get-Process -Name "llama-server" -ErrorAction SilentlyContinue
    if ($llamaProc) {
        Write-Host ' • Llama.cpp Engine : ' -NoNewline
        Write-Host '🟢 ACTIF ' -ForegroundColor Green -NoNewline
        Write-Host "(PID: $($llamaProc[0].Id) | Port: 8080 | MoE CUDA)" -ForegroundColor DarkGray
    } else {
        Write-Host ' • Llama.cpp Engine : ' -NoNewline
        Write-Host '⚪ ARRÊTÉ' -ForegroundColor DarkGray
    }
    Write-Host ''

    # --- SECTION MODÈLES CHARGÉS (Détails précis) ---
    Write-Host '🔥 MODÈLES ACTUELLEMENT CHARGÉS EN MÉMOIRE (VRAM/RAM) :' -ForegroundColor Yellow
    Write-Host '─────────────────────────────────────────────────────────────────────────────' -ForegroundColor DarkGray

    $loadedCount = 0

    # 1. Modèles Ollama
    if ($ollamaProc) {
        try {
            $ollamaPs = Invoke-RestMethod -Uri "http://127.0.0.1:11434/api/ps" -Method Get -TimeoutSec 2 -ErrorAction SilentlyContinue
            if ($ollamaPs.models -and $ollamaPs.models.Count -gt 0) {
                foreach ($om in $ollamaPs.models) {
                    $loadedCount++
                    $sizeGB = [math]::Round($om.size / 1GB, 2)
                    $vramGB = [math]::Round($om.size_vram / 1GB, 2)
                    $expMinutes = if ($om.expires_at) {
                        $diff = (Get-Date $om.expires_at) - (Get-Date)
                        [math]::Max(0, [int]$diff.TotalMinutes)
                    } else { 0 }

                    Write-Host " [Ollama] " -ForegroundColor Cyan -NoNewline
                    Write-Host "$($om.name)" -ForegroundColor White -NoNewline
                    Write-Host " (Taille: ${sizeGB} Go | VRAM: ${vramGB} Go | Expire dans: ${expMinutes} min)" -ForegroundColor DarkGray
                }
            }
        } catch {
            # Fallback commande CLI
            try {
                $cliPs = ollama ps 2>$null
                if ($cliPs -and $cliPs.Count -gt 1) {
                    $loadedCount++
                    Write-Host " [Ollama CLI] " -ForegroundColor Cyan
                    $cliPs | Select-Object -Skip 1 | ForEach-Object { Write-Host "   $_" -ForegroundColor White }
                }
            } catch {}
        }
    }

    # 2. Modèle Llama.cpp
    if ($llamaProc) {
        try {
            $llamaModels = Invoke-RestMethod -Uri "http://127.0.0.1:8080/v1/models" -Method Get -TimeoutSec 2 -ErrorAction SilentlyContinue
            if ($llamaModels.data -and $llamaModels.data.Count -gt 0) {
                foreach ($lm in $llamaModels.data) {
                    $loadedCount++
                    $leafName = [System.IO.Path]::GetFileName($lm.id)
                    $paramsB = if ($lm.meta.n_params) { [math]::Round($lm.meta.n_params / 1000000000, 1) } else { "?" }
                    $ftype = if ($lm.meta.ftype) { $lm.meta.ftype } else { "GGUF" }
                    $ctxK = if ($lm.meta.n_ctx) { "$([int]($lm.meta.n_ctx / 1024))k" } else { "32k" }

                    Write-Host " [Llama.cpp] " -ForegroundColor Green -NoNewline
                    Write-Host "$leafName" -ForegroundColor White -NoNewline
                    Write-Host " (${paramsB}B params | $ftype | Contexte: $ctxK | Port: 8080)" -ForegroundColor DarkGray
                }
            } else {
                $loadedCount++
                Write-Host " [Llama.cpp] " -ForegroundColor Green -NoNewline
                Write-Host "Serveur actif sur port 8080 (PID: $($llamaProc[0].Id))" -ForegroundColor White
            }
        } catch {
            $loadedCount++
            Write-Host " [Llama.cpp] " -ForegroundColor Green -NoNewline
            Write-Host "Serveur actif sur port 8080 (PID: $($llamaProc[0].Id))" -ForegroundColor White
        }
    }

    if ($loadedCount -eq 0) {
        Write-Host '   Aucun modèle n''est actuellement maintenu en mémoire vive.' -ForegroundColor DarkGray
    }

    Write-Host ''
    Write-Host '─────────────────────────────────────────────────────────────────────────────' -ForegroundColor DarkGray
    Write-Host 'ACTIONS RAPIDES :' -ForegroundColor Yellow
    Write-Host ' [D] 🛑 Décharger les modèles de la mémoire' -ForegroundColor White
    Write-Host ' [O] 🛑 Arrêter le service Ollama' -ForegroundColor White
    Write-Host ' [L] 🛑 Arrêter le service Llama.cpp' -ForegroundColor White
    Write-Host ' [A] 🛑 Tout arrêter (Libérer 100% VRAM et RAM)' -ForegroundColor Red
    Write-Host ' [R] 🔄 Rafraîchir les statistiques' -ForegroundColor Cyan
    Write-Host ' [Q] ↩️ Retour au Menu Principal' -ForegroundColor Gray
    Write-Host '─────────────────────────────────────────────────────────────────────────────' -ForegroundColor DarkGray
}

# Boucle interactive du Dashboard
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stopScript = Join-Path $scriptDir "Stop-Services.ps1"

do {
    Show-DashboardView
    $choice = Read-Host "Votre action"

    switch ($choice) {
        "D" {
            & $stopScript -Action "unload-models"
            Start-Sleep -Seconds 2
        }
        "O" {
            & $stopScript -Action "stop-ollama"
            Start-Sleep -Seconds 2
        }
        "L" {
            & $stopScript -Action "stop-llama"
            Start-Sleep -Seconds 2
        }
        "A" {
            & $stopScript -Action "stop-all"
            Start-Sleep -Seconds 2
        }
    }
} while ($choice -notmatch "^[qQ]")
