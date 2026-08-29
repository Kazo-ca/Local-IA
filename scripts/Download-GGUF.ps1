<#
.SYNOPSIS
    Télécharge des modèles GGUF haute performance (MoE & Dense) optimisés pour llama.cpp et le stockage NVMe (E:\llama_models).
#>

[CmdletBinding()]
param(
    [ValidateSet("deepseek-coder-v2-lite", "qwq-32b", "qwen2.5-coder-32b", "mixtral-8x7b", "qwen3.8-27b", "qwen3.6-35b-a3b")]
    [string]$Model = "deepseek-coder-v2-lite",

    [string]$DestinationDir = "E:\llama_models"
)

if (-not (Test-Path $DestinationDir)) {
    New-Item -Path $DestinationDir -ItemType Directory -Force | Out-Null
}

$modelCatalog = @{
    "deepseek-coder-v2-lite" = @{
        "name" = "DeepSeek-Coder-V2-Lite-Instruct-Q4_K_M.gguf"
        "size_gb" = "~10.3 Go"
        "type" = "MoE (16B total / 2.4B actif - Idéal C# & GPU/CPU Split)"
        "url" = "https://huggingface.co/bartowski/DeepSeek-Coder-V2-Lite-Instruct-GGUF/resolve/main/DeepSeek-Coder-V2-Lite-Instruct-Q4_K_M.gguf"
    }
    "qwq-32b" = @{
        "name" = "qwq-32b-q4_k_m.gguf"
        "size_gb" = "~19.8 Go"
        "type" = "Raisonnement Profond (Maths & Physique Quantique)"
        "url" = "https://huggingface.co/Qwen/QwQ-32B-GGUF/resolve/main/qwq-32b-q4_k_m.gguf"
    }
    "qwen2.5-coder-32b" = @{
        "name" = "qwen2.5-coder-32b-instruct-q4_k_m.gguf"
        "size_gb" = "~19.8 Go"
        "type" = "Dense C# & .NET 32B (Précision Maximale)"
        "url" = "https://huggingface.co/Qwen/Qwen2.5-Coder-32B-Instruct-GGUF/resolve/main/qwen2.5-coder-32b-instruct-q4_k_m.gguf"
    }
    "mixtral-8x7b" = @{
        "name" = "mixtral-8x7b-instruct-v0.1.Q4_K_M.gguf"
        "size_gb" = "~26.4 Go"
        "type" = "MoE Multi-Agents (46.7B total / 12.9B actif - Split CPU/GPU)"
        "url" = "https://huggingface.co/TheBloke/Mixtral-8x7B-Instruct-v0.1-GGUF/resolve/main/mixtral-8x7b-instruct-v0.1.Q4_K_M.gguf"
    }
    "qwen3.8-27b" = @{
        "name" = "qwen3.8-27b-q4_k_m.gguf"
        "size_gb" = "~17 Go"
        "type" = "Dense Programmation & Agent (27B - Multimodal & Code)"
        "url" = "https://huggingface.co/bartowski/Qwen3.8-27B-GGUF/resolve/main/Qwen3.8-27B-Q4_K_M.gguf"
    }
    "qwen3.6-35b-a3b" = @{
        "name" = "qwen3.6-35b-a3b-q4_k_m.gguf"
        "size_gb" = "~23 Go"
        "type" = "MoE Raisonnement Mathématique (35B total / 3B actif - High Speed Split)"
        "url" = "https://huggingface.co/bartowski/Qwen_Qwen3.6-35B-A3B-GGUF/resolve/main/Qwen_Qwen3.6-35B-A3B-Q4_K_M.gguf"
    }
}

$target = $modelCatalog[$Model]
$targetPath = Join-Path $DestinationDir $target.name

Write-Host "======================================================" -ForegroundColor Cyan
Write-Host " LOCAL-IA : Téléchargement Modèle GGUF pour Llama.cpp " -ForegroundColor Cyan
Write-Host " Modèle      : $($target.name)" -ForegroundColor Yellow
Write-Host " Type        : $($target.type)" -ForegroundColor Yellow
Write-Host " Taille      : $($target.size_gb)" -ForegroundColor Yellow
Write-Host " Destination : $targetPath" -ForegroundColor Yellow
Write-Host "======================================================" -ForegroundColor Cyan

if (Test-Path $targetPath) {
    Write-Host "[INFO] Le fichier $targetPath existe déjà localement." -ForegroundColor Green
    return $targetPath
}

Write-Host "[INFO] Début du téléchargement depuis HuggingFace (curl avec reprise possible)..." -ForegroundColor Cyan
Write-Host "[URL] $($target.url)" -ForegroundColor DarkGray

# Utilisation de curl.exe natif Windows qui gère parfaitement les redirections CDN HuggingFace et la reprise
$curlArgs = @("-L", "-C", "-", "--progress-bar", "-o", "`"$targetPath`"", "`"$($target.url)`"")
$process = Start-Process -FilePath "curl.exe" -ArgumentList $curlArgs -NoNewWindow -Wait -PassThru

if ($process.ExitCode -eq 0 -and (Test-Path $targetPath)) {
    Write-Host "`n[SUCCÈS] Téléchargement terminé avec succès !" -ForegroundColor Green
    return $targetPath
} else {
    Write-Host "`n[ERREUR] Échec du téléchargement (Code de sortie: $($process.ExitCode))." -ForegroundColor Red
    if (Test-Path $targetPath) {
        $item = Get-Item $targetPath
        if ($item.Length -lt 100MB) {
            Remove-Item $targetPath -Force -ErrorAction SilentlyContinue
        }
    }
    return $null
}
