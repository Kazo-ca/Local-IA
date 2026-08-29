<#
.SYNOPSIS
    Analyse, compare et synchronise les modèles locaux (Ollama et Llama.cpp) avec chatLanguageModels.json de VS Code.
.DESCRIPTION
    Désérialise chatLanguageModels.json, compare avec les modèles réels (sur disque NVMe et en mémoire),
    signale les différences et permet d'écrire/synchroniser automatiquement la configuration dans VS Code.
#>

[CmdletBinding()]
param()

$chatLMPath = "$env:APPDATA\Code\User\chatLanguageModels.json"

function Get-LocalEngineModels {
    $engineModels = @()

    # 1. Modèles Ollama (En mémoire API ou sur disque NVMe)
    $ollamaNames = @()
    try {
        $res = Invoke-RestMethod -Uri "http://127.0.0.1:11434/api/tags" -Method Get -TimeoutSec 2 -ErrorAction Stop
        if ($res.models) {
            foreach ($m in $res.models) { $ollamaNames += $m.name }
        }
    } catch {
        # Si Ollama est éteint, lire les manifests sur disque
        $manifestDir = "E:\ollama_models\manifests\registry.ollama.ai\library"
        if (Test-Path $manifestDir) {
            $files = Get-ChildItem -Path $manifestDir -Recurse -File -ErrorAction SilentlyContinue
            foreach ($f in $files) {
                $tag = $f.Name
                $modelFolder = $f.Directory.Name
                $fullName = "${modelFolder}:${tag}"
                $ollamaNames += $fullName
            }
        }
    }

    # Liste par défaut des profils configurés dans local-ia
    $configuredProfiles = @("local-ia-active:latest", "local-ia-deep-reasoning:32b", "local-ia-csharp:32b", "local-ia-agent-tools:32b")
    foreach ($cp in $configuredProfiles) {
        if ($ollamaNames -notcontains $cp) { $ollamaNames += $cp }
    }

    foreach ($name in ($ollamaNames | Select-Object -Unique)) {
        $engineModels += [PSCustomObject]@{
            Id = $name
            Engine = "Ollama"
            Url = "http://127.0.0.1:11434/v1"
            ToolCalling = $true
        }
    }

    # 2. Modèles GGUF Llama.cpp
    $llamaDir = "E:\llama_models"
    if (Test-Path $llamaDir) {
        $ggufs = Get-ChildItem -Path $llamaDir -Filter "*.gguf" -ErrorAction SilentlyContinue
        foreach ($g in $ggufs) {
            $baseId = [System.IO.Path]::GetFileNameWithoutExtension($g.Name).ToLower()
            $cleanName = switch -Regex ($baseId) {
                "qwq" { "qwq-32b" }
                "deepseek-coder" { "deepseek-coder-v2-lite" }
                "qwen2.5-coder" { "qwen2.5-coder-32b" }
                "mixtral" { "mixtral-8x7b" }
                "qwen3\.8-27b" { "qwen3.8-27b" }
                "qwen3\.6-35b" { "qwen3.6-35b-a3b" }
                default { $baseId }
            }
            $engineModels += [PSCustomObject]@{
                Id = $cleanName
                Engine = "Llama.cpp"
                Url = "http://127.0.0.1:8080/v1"
                ToolCalling = $true
            }
            if ($cleanName -ne $baseId) {
                $engineModels += [PSCustomObject]@{
                    Id = $baseId
                    Engine = "Llama.cpp"
                    Url = "http://127.0.0.1:8080/v1"
                    ToolCalling = $true
                }
            }
        }
    }

    return $engineModels
}

function Get-ConfiguredVsCodeModels {
    if (-not (Test-Path $chatLMPath)) {
        return @()
    }

    try {
        $raw = Get-Content -Path $chatLMPath -Raw -Encoding UTF8
        $json = $raw | ConvertFrom-Json
        $configured = @()

        foreach ($provider in $json) {
            if ($provider.models) {
                foreach ($m in $provider.models) {
                    $configured += [PSCustomObject]@{
                        Id = $m.id
                        Name = $m.name
                        ProviderName = $provider.name
                        Url = $m.url
                        ToolCalling = $m.toolCalling
                    }
                }
            }
        }
        return $configured
    } catch {
        return @()
    }
}

function Save-SyncJson {
    if (-not (Test-Path $chatLMPath)) { return }

    try {
        $raw = Get-Content -Path $chatLMPath -Raw -Encoding UTF8
        $json = $raw | ConvertFrom-Json -AsHashtable
        
        # Conserver les providers existants (Google, OpenRouter, Copilot)
        $preserved = @()
        foreach ($p in $json) {
            if ($p.name -ne "Ollama" -and $p.name -ne "LlamaCpp-MoE" -and $p.name -ne "OllamaLocal") {
                $preserved += $p
            }
        }

        # Ajouter le bloc Ollama propre
        $preserved += @{
            name = "Ollama"
            vendor = "customendpoint"
            apiKey = "ollama"
            apiType = "chat-completions"
            models = @(
                @{
                    id = "local-ia-active:latest"
                    name = "Local IA (Profil Actif en cours)"
                    url = "http://127.0.0.1:11434/v1"
                    toolCalling = $true
                    vision = $false
                    maxInputTokens = 32768
                    maxOutputTokens = 8192
                },
                @{
                    id = "local-ia-deep-reasoning:32b"
                    name = "Deep Reasoning 32B (Maths et Physique)"
                    url = "http://127.0.0.1:11434/v1"
                    toolCalling = $true
                    vision = $false
                    maxInputTokens = 32768
                    maxOutputTokens = 16000
                },
                @{
                    id = "local-ia-csharp:32b"
                    name = "C# et .NET Expert 32B"
                    url = "http://127.0.0.1:11434/v1"
                    toolCalling = $true
                    vision = $false
                    maxInputTokens = 32768
                    maxOutputTokens = 8192
                },
                @{
                    id = "local-ia-agent-tools:32b"
                    name = "Agents et Tools 32B"
                    url = "http://127.0.0.1:11434/v1"
                    toolCalling = $true
                    vision = $false
                    maxInputTokens = 32768
                    maxOutputTokens = 8192
                }
            )
        }

        # Ajouter le bloc Llama.cpp propre
        $preserved += @{
            name = "LlamaCpp-MoE"
            vendor = "customendpoint"
            apiKey = "llama"
            apiType = "chat-completions"
            models = @(
                @{
                    id = "qwen3.8-27b"
                    name = "Llama.cpp - Qwen 3.8 27B (Programmation et Agent)"
                    url = "http://127.0.0.1:8080/v1"
                    toolCalling = $true
                    vision = $true
                    maxInputTokens = 32768
                    maxOutputTokens = 8192
                },
                @{
                    id = "qwen3.6-35b-a3b"
                    name = "Llama.cpp - Qwen 3.6 35B-A3B MoE (Maths et Physique)"
                    url = "http://127.0.0.1:8080/v1"
                    toolCalling = $true
                    vision = $true
                    maxInputTokens = 32768
                    maxOutputTokens = 16000
                },
                @{
                    id = "qwq-32b"
                    name = "Llama.cpp - QwQ 32B (MoE CUDA)"
                    url = "http://127.0.0.1:8080/v1"
                    toolCalling = $true
                    vision = $false
                    maxInputTokens = 32768
                    maxOutputTokens = 16000
                },
                @{
                    id = "deepseek-coder-v2-lite"
                    name = "Llama.cpp - DeepSeek Coder V2 MoE (C#)"
                    url = "http://127.0.0.1:8080/v1"
                    toolCalling = $true
                    vision = $false
                    maxInputTokens = 32768
                    maxOutputTokens = 8192
                }
            )
        }

        $newJson = $preserved | ConvertTo-Json -Depth 10
        Set-Content -Path $chatLMPath -Value $newJson -Encoding UTF8
        Write-Host "`n[SUCCÈS] chatLanguageModels.json a été synchronisé avec succès !" -ForegroundColor Green
    } catch {
        Write-Host "`n[ERREUR] Impossible de sauvegarder : $_" -ForegroundColor Red
    }
}

function Show-SyncView {
    Clear-Host
    Write-Host "=========================================================================" -ForegroundColor Cyan
    Write-Host "       AUDIT ET SYNCHRONISATION : chatLanguageModels.json (VS Code)       " -ForegroundColor Cyan
    Write-Host "=========================================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Fichier cible : $chatLMPath" -ForegroundColor DarkGray
    Write-Host ""

    $engineModels = Get-LocalEngineModels
    $configuredModels = Get-ConfiguredVsCodeModels

    $engineModelIds = @($engineModels | ForEach-Object { $_.Id })
    $configuredModelIds = @($configuredModels | ForEach-Object { $_.Id })

    # 1. Comparaison
    $inEngineNotJson = @($engineModels | Where-Object { $configuredModelIds -notcontains $_.Id })
    $inJsonNotEngine = @($configuredModels | Where-Object { $engineModelIds -notcontains $_.Id })
    $synced = @($engineModels | Where-Object { $configuredModelIds -contains $_.Id })

    Write-Host "RÉSULTAT DE LA COMPARAISON :" -ForegroundColor Yellow
    Write-Host "-------------------------------------------------------------------------" -ForegroundColor DarkGray

    # Synchronisés
    if ($synced.Count -gt 0) {
        Write-Host "Modèles locaux synchronisés dans VS Code ($($synced.Count)) :" -ForegroundColor Green
        foreach ($s in $synced) {
            Write-Host "   - $($s.Id) [$($s.Engine)]" -ForegroundColor Green
        }
        Write-Host ""
    }

    # Dans moteur mais pas dans JSON
    if ($inEngineNotJson.Count -gt 0) {
        Write-Host "Modèles installés localement mais ABSENTS de chatLanguageModels.json ($($inEngineNotJson.Count)) :" -ForegroundColor Yellow
        foreach ($m in $inEngineNotJson) {
            Write-Host "   + $($m.Id) [$($m.Engine)] -> Appuyez sur [S] pour le rajouter automatiquement" -ForegroundColor Yellow
        }
        Write-Host ""
    }

    # Dans JSON mais pas dans le moteur
    if ($inJsonNotEngine.Count -gt 0) {
        Write-Host "Modèles déclarés dans VS Code mais NON TROUVÉS sur le serveur local ($($inJsonNotEngine.Count)) :" -ForegroundColor Red
        foreach ($m in $inJsonNotEngine) {
            Write-Host "   - $($m.Id) (Déclaré dans $($m.ProviderName))" -ForegroundColor Red
        }
        Write-Host ""
    }

    if ($inEngineNotJson.Count -eq 0 -and $inJsonNotEngine.Count -eq 0) {
        Write-Host "Parfait ! Tous vos modèles locaux sont 100% synchronisés avec VS Code." -ForegroundColor Green
        Write-Host ""
    }

    Write-Host ""
    Write-Host "ACTIONS :" -ForegroundColor Yellow
    Write-Host " [S] Synchroniser automatiquement le fichier (Mise à jour directe)" -ForegroundColor Green
    Write-Host " [O] Ouvrir chatLanguageModels.json dans VS Code (code)" -ForegroundColor White
    Write-Host " [R] Réanalyser et rafraîchir" -ForegroundColor Cyan
    Write-Host " [Q] Retour au Menu Principal" -ForegroundColor Gray
    Write-Host "-------------------------------------------------------------------------" -ForegroundColor DarkGray
}

do {
    Show-SyncView
    $action = Read-Host "Votre choix"

    switch ($action) {
        "S" {
            Save-SyncJson
            Start-Sleep -Seconds 2
        }
        "O" {
            if (Test-Path $chatLMPath) {
                Write-Host "[INFO] Ouverture de $chatLMPath dans VS Code..." -ForegroundColor Green
                code $chatLMPath
            } else {
                Write-Host "[ERREUR] Fichier introuvable : $chatLMPath" -ForegroundColor Red
            }
            Start-Sleep -Seconds 1
        }
    }
} while ($action -notmatch "^[qQ]")
