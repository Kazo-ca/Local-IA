<#
.SYNOPSIS
    Teste les modèles locaux via l'API REST d'Ollama avec des cas de test représentatifs.
.DESCRIPTION
    Envoie un prompt de test adapté au profil (C#, Maths/Physique Quantique, Tool Calling) et affiche le résultat.
.EXAMPLE
    .\Test-Models.ps1 -Profile csharp
    .\Test-Models.ps1 -Profile deep_reasoning
    .\Test-Models.ps1 -Profile agent_tools
#>

[CmdletBinding()]
param(
    [ValidateSet("csharp", "deep_reasoning", "agent_tools", "active")]
    [string]$Profile = "active",

    [string]$CustomPrompt = ""
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir
$configFile = Join-Path $rootDir "config\config.json"

$modelName = "local-ia-active"
$testPrompt = ""

switch ($Profile) {
    "csharp" {
        $modelName = "local-ia-csharp:32b"
        $testPrompt = "Écris une classe générique en C# .NET 9 'ConcurrentPipeline<TIn, TOut>' avec Channel<T>, gestion d'annulation CancellationToken et traitement asynchrone par batch. Fournis uniquement le code et de courtes explications."
    }
    "deep_reasoning" {
        $modelName = "local-ia-deep-reasoning:32b"
        $testPrompt = "Explique et démontre formellement le principe de superposition quantique dans un espace de Hilbert bidimensionnel (Qubit), en détaillant l'action des portes de Hadamard et Pauli-Z sur la matrice de densité."
    }
    "agent_tools" {
        $modelName = "local-ia-agent-tools:32b"
        $testPrompt = "Tu as accès à une fonction 'ExecuteDatabaseQuery(sql_query: string, timeout_ms: int)'. L'utilisateur demande : 'Récupère les 10 derniers utilisateurs actifs ayant créé une commande ce mois-ci'. Génère l'appel d'outil JSON exact."
    }
    "active" {
        $modelName = "local-ia-active"
        if ([string]::IsNullOrWhiteSpace($CustomPrompt)) {
            $testPrompt = "Présente-toi brièvement et confirme tes directives système actuelles."
        } else {
            $testPrompt = $CustomPrompt
        }
    }
}

if (-not [string]::IsNullOrWhiteSpace($CustomPrompt)) {
    $testPrompt = $CustomPrompt
}

Write-Host "======================================================" -ForegroundColor Cyan
Write-Host " LOCAL-IA : Test du Modèle '$modelName'                " -ForegroundColor Cyan
Write-Host " Prompt : $testPrompt" -ForegroundColor Yellow
Write-Host "======================================================" -ForegroundColor Cyan

$body = @{
    model = $modelName
    prompt = $testPrompt
    stream = $true
} | ConvertTo-Json

try {
    $uri = "http://127.0.0.1:11434/api/generate"
    
    $request = [System.Net.HttpWebRequest]::Create($uri)
    $request.Method = "POST"
    $request.ContentType = "application/json"
    
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
    $request.ContentLength = $bytes.Length
    $stream = $request.GetRequestStream()
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Close()
    
    $response = $request.GetResponse()
    $reader = New-Object System.IO.StreamReader($response.GetResponseStream())
    
    Write-Host "`n--- Réponse du Modèle ---`n" -ForegroundColor Green
    while (-not $reader.EndOfStream) {
        $line = $reader.ReadLine()
        if (-not [string]::IsNullOrWhiteSpace($line)) {
            $json = $line | ConvertFrom-Json
            Write-Host -NoNewline $json.response
        }
    }
    Write-Host "`n`n--- Fin de la réponse ---" -ForegroundColor Green
    $reader.Close()
    $response.Close()
} catch {
    Write-Host "[ERREUR] Échec de la communication avec le modèle : $_" -ForegroundColor Red
}
