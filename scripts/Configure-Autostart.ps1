<#
.SYNOPSIS
    Gère le démarrage automatique d'Ollama au lancement de Windows.
#>

[CmdletBinding()]
param(
    [ValidateSet("disable", "enable", "status")]
    [string]$Action = "disable"
)

$startupPath = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\Ollama.lnk"

switch ($Action) {
    "disable" {
        if (Test-Path $startupPath) {
            Remove-Item -Path $startupPath -Force
            Write-Host "[OK] Le démarrage automatique d'Ollama au boot de Windows a été DÉSACTIVÉ." -ForegroundColor Green
        } else {
            Write-Host "[INFO] Le démarrage automatique d'Ollama était déjà désactivé." -ForegroundColor Gray
        }
    }
    "enable" {
        $ollamaExe = "$env:LOCALAPPDATA\Programs\Ollama\ollama app.exe"
        if (-not (Test-Path $ollamaExe)) {
            $ollamaExe = "$env:LOCALAPPDATA\Programs\Ollama\ollama.exe"
        }
        if (Test-Path $ollamaExe) {
            $wshShell = New-Object -ComObject WScript.Shell
            $shortcut = $wshShell.CreateShortcut($startupPath)
            $shortcut.TargetPath = $ollamaExe
            $shortcut.Save()
            Write-Host "[OK] Le démarrage automatique d'Ollama au boot de Windows a été ACTIVÉ." -ForegroundColor Green
        }
    }
    "status" {
        if (Test-Path $startupPath) {
            Write-Host "[STATUT] Démarrage automatique Windows : ACTIVÉ" -ForegroundColor Yellow
        } else {
            Write-Host "[STATUT] Démarrage automatique Windows : DÉSACTIVÉ (Démarrage uniquement à la demande)" -ForegroundColor Green
        }
    }
}
