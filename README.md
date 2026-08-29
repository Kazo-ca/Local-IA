*[English](#english) below · [Version française](#francais) plus bas*

<a id="english"></a>
# 🚀 LOCAL-IA: High-Capacity Local Model Hub for VS Code Copilot

Welcome to the **LOCAL-IA** environment. This setup is specifically tuned for your hardware:
- **GPU**: NVIDIA GeForce RTX 3060 (12 GB VRAM)
- **System RAM**: 64 GB DDR4
- **CPU**: AMD Ryzen 7 3700X (8C / 16T)
- **NVMe storage**: `E:\ollama_models` (dedicated models folder)

The absolute priority of this setup is **reasoning ability and answer accuracy**, organized around **3 specialized profiles**, each available in **2 power tiers** (14B and 32B).

---

## 🎯 The 3 Specialized Profiles

### 1. 💻 C# & .NET Expert Profile (`csharp`)
- **Focus**: C# 12/13, .NET 8/9, EF Core, LINQ, clean architectures (Clean Architecture, DDD), asynchronous programming, and memory optimization (`Span<T>`, `ValueTask`).
- **32B model (maximum accuracy)**: `local-ia-csharp:32b` (based on `qwen2.5-coder:32b-instruct`)
- **14B model (100% VRAM, ultra-smooth)**: `local-ia-csharp:14b` (based on `qwen2.5-coder:14b-instruct`)

### 2. 🧠 Deep Reasoning Profile (`deep_reasoning`)
- **Focus**: Rigorous mathematical deduction, quantum physics, Hilbert-space formalism, formal logic, and step-by-step (Chain-of-Thought) problem solving with **full tool support and VS Code Agent mode**.
- **32B model (maximum accuracy + tools)**: `local-ia-deep-reasoning:32b` (based on `qwq:32b`)
- **14B model (100% VRAM, ultra-smooth + tools)**: `local-ia-deep-reasoning:14b` (based on `qwen2.5:14b-instruct`)

### 3. 🛠️ Tools & Agents Profile (`agent_tools`)
- **Focus**: Strict JSON schema compliance, deterministic function calling, task decomposition, and multi-agent orchestration without tool hallucination.
- **32B model (maximum accuracy)**: `local-ia-agent-tools:32b` (based on `qwen2.5:32b-instruct`)
- **14B model (100% VRAM, ultra-smooth)**: `local-ia-agent-tools:14b` (based on `qwen2.5:14b-instruct`)

---

## ⚡ Llama.cpp Engine (MoE CPU Experts + GPU VRAM)

This option uses **`llama-server.exe`** compiled with NVIDIA CUDA 12.4 and Flash Attention:
* **Principle**: Shared attention layers are placed in VRAM (your RTX 3060's 12 GB), while **MoE experts** (inactive weights) stay in your 64 GB of CPU RAM.
* **Benefit**: Massive speed gains on MoE architectures (such as **DeepSeek-Coder-V2 Lite, 16B total/2.4B active** or **Mixtral-8x7B**) while still benefiting from giant-model accuracy.
* **Endpoint**: `http://127.0.0.1:8080/v1` (OpenAI-compatible / VS Code).

---

## 📁 Project Structure

```text
local-ia/
├── .vscode/
│   └── settings.json             # VS Code Copilot endpoint configuration
├── config/
│   ├── config.json               # Profile and model metadata
│   └── Modelfiles/               # System prompts and inference parameters
│       ├── Modelfile.csharp-14b
│       ├── Modelfile.csharp-32b
│       ├── Modelfile.deep-reasoning-14b
│       ├── Modelfile.deep-reasoning-32b
│       ├── Modelfile.agent-tools-14b
│       └── Modelfile.agent-tools-32b
├── scripts/
│   ├── Start-OllamaService.ps1   # Starts the server against the E:\ollama_models cache
│   ├── Install-Models.ps1        # Downloads and builds the models
│   ├── Set-ActiveProfile.ps1     # Switches the active profile and updates VS Code
│   └── Test-Models.ps1           # Single-model streaming test
└── README.md
```

---

## ⚡ Quick Start Guide

### Recommended option: the 1-click interactive menu
Simply launch [Run-LocalIA.bat](file:///c:/Users/clefw/repos/source/local-ia/Run-LocalIA.bat) (double-click), or from PowerShell:
```powershell
.\Run-LocalIA.ps1
```

The menu lets you:
1. **Immediately start the model of your choice** (32B or 14B on Ollama, or MoE on Llama.cpp).
2. **[S] Live dashboard & stats**: view CPU, GPU (RTX 3060), VRAM (12 GB), and RAM (64 GB) status, plus the models currently loaded in memory.
3. **[C] VS Code audit & sync**: automatically compares locally installed models against your `chatLanguageModels.json` file, flags missing or outdated models, and lets you open that file in VS Code with one click.
4. **[X] Shutdown & memory-release manager**: unload a model or stop services with a single click to free 100% of VRAM/RAM.
5. **[L] Llama.cpp MoE engine**: offloads MoE experts to CPU and attention to GPU for maximum performance.

---

---

### Step 3: Switch Between Active Profiles

To change the model VS Code uses by default:
```powershell
# Activate the C# 32B profile
.\scripts\Set-ActiveProfile.ps1 -Profile csharp -Tier 32b

# Activate the Deep Reasoning profile (Math & Quantum Physics)
.\scripts\Set-ActiveProfile.ps1 -Profile deep_reasoning -Tier 32b

# Activate the Tools & Agents profile
.\scripts\Set-ActiveProfile.ps1 -Profile agent_tools -Tier 32b
```

---

### Step 4: Test a Model
You can test a profile directly against a concrete case from the command line:
```powershell
# Test C# generation
.\scripts\Test-Models.ps1 -Profile csharp

# Test the quantum physics demonstration
.\scripts\Test-Models.ps1 -Profile deep_reasoning

# Test a JSON tool call
.\scripts\Test-Models.ps1 -Profile agent_tools
```

---

## 🔌 VS Code & Copilot Integration

The local server exposes the standard **OpenAI** API at `http://localhost:11434/v1` and the native Ollama API at `http://localhost:11434`.

In VS Code:
1. **GitHub Copilot Chat custom endpoints**: preconfigured in `.vscode/settings.json`.
2. **Using other AI extensions (e.g. Continue, Cline, Roo Code)**:
   - **Provider**: `Ollama` or `OpenAI Compatible`
   - **Base URL**: `http://localhost:11434/v1`
   - **API Key**: `ollama` (not required locally)
   - **Model Name**: `local-ia-active` (or the exact name, e.g. `local-ia-csharp:32b`)

---

<a id="francais"></a>
# 🚀 LOCAL-IA : Hub de Modèles Locaux Haute Capacité pour VS Code Copilot

Bienvenue dans l'environnement **LOCAL-IA**. Cette configuration est spécifiquement calibrée pour votre matériel :
- **GPU** : NVIDIA GeForce RTX 3060 (12 Go VRAM)
- **RAM Système** : 64 Go DDR4
- **CPU** : AMD Ryzen 7 3700X (8C / 16T)
- **Stockage NVMe** : `E:\ollama_models` (Dossier dédié aux modèles)

La priorité absolue de cette configuration est **la capacité de réflexion et l'exactitude des réponses**, organisée autour de **3 profils d'excellence** déclinés en **2 niveaux de puissance** (14B et 32B).

---

## 🎯 Les 3 Profils Spécialisés

### 1. 💻 Profil C# & .NET Expert (`csharp`)
- **Focus** : C# 12/13, .NET 8/9, EF Core, LINQ, architectures propres (Clean Architecture, DDD), programmation asynchrone et optimisation mémoire (`Span<T>`, `ValueTask`).
- **Modèle 32B (Précision Maximale)** : `local-ia-csharp:32b` (base `qwen2.5-coder:32b-instruct`)
- **Modèle 14B (100% VRAM Ultra-fluide)** : `local-ia-csharp:14b` (base `qwen2.5-coder:14b-instruct`)

### 2. 🧠 Profil Réflexion Profonde (`deep_reasoning`)
- **Focus** : Déduction mathématique rigoureuse, physique quantique, formalisme d'espace de Hilbert, logique formelle et résolution pas-à-pas (*Chain-of-Thought*) avec **support complet des outils et du mode Agent de VS Code**.
- **Modèle 32B (Précision Maximale + Tools)** : `local-ia-deep-reasoning:32b` (base `qwq:32b`)
- **Modèle 14B (100% VRAM Ultra-fluide + Tools)** : `local-ia-deep-reasoning:14b` (base `qwen2.5:14b-instruct`)

### 3. 🛠️ Profil Outils & Agents (`agent_tools`)
- **Focus** : Conformité stricte aux schémas JSON, Function Calling déterministe, décomposition de tâches et orchestration multi-agents sans hallucination d'outils.
- **Modèle 32B (Précision Maximale)** : `local-ia-agent-tools:32b` (base `qwen2.5:32b-instruct`)
- **Modèle 14B (100% VRAM Ultra-fluide)** : `local-ia-agent-tools:14b` (base `qwen2.5:14b-instruct`)

---

## ⚡ Moteur Llama.cpp (MoE CPU Experts + GPU VRAM)

Cette option utilise **`llama-server.exe`** compilé avec NVIDIA CUDA 12.4 et Flash Attention :
* **Principe** : Les couches d'attention partagées sont placées dans la VRAM (12 Go de votre RTX 3060), et les **experts MoE** (les poids inactifs) restent dans vos 64 Go de RAM CPU.
* **Avantage** : Gains de vitesse massifs sur les architectures MoE (comme **DeepSeek-Coder-V2 Lite 16B/2.4B actif** ou **Mixtral-8x7B**) tout en bénéficiant de l'exactitude de modèles géants.
* **Endpoint** : `http://127.0.0.1:8080/v1` (compatible OpenAI / VS Code).

---

## 📁 Structure du Projet

```text
local-ia/
├── .vscode/
│   └── settings.json             # Configuration des endpoints VS Code Copilot
├── config/
│   ├── config.json               # Métadonnées des profils et modèles
│   └── Modelfiles/               # Prompts système et paramètres d'inférence
│       ├── Modelfile.csharp-14b
│       ├── Modelfile.csharp-32b
│       ├── Modelfile.deep-reasoning-14b
│       ├── Modelfile.deep-reasoning-32b
│       ├── Modelfile.agent-tools-14b
│       └── Modelfile.agent-tools-32b
├── scripts/
│   ├── Start-OllamaService.ps1   # Démarrage du serveur avec cache E:\ollama_models
│   ├── Install-Models.ps1        # Téléchargement et compilation des modèles
│   ├── Set-ActiveProfile.ps1     # Bascule de profil et mise à jour de VS Code
│   └── Test-Models.ps1           # Test streaming unitaire des modèles
└── README.md
```

---

## ⚡ Guide de Démarrage Rapide

### Option Recommandée : Le Menu Interactif en 1 Clic
Lancez simplement le fichier [Run-LocalIA.bat](file:///c:/Users/clefw/repos/source/local-ia/Run-LocalIA.bat) (par double-clic) ou dans PowerShell :
```powershell
.\Run-LocalIA.ps1
```

Le menu vous permet de :
1. **Démarrer immédiatement le modèle de votre choix** (32B ou 14B sur Ollama, ou MoE sur Llama.cpp).
2. **[S] Tableau de bord & Stats en direct** : Visualisez l'état du CPU, GPU RTX 3060, VRAM (12 Go), RAM (64 Go) et les modèles actuellement en mémoire.
3. **[C] Audit & Synchronisation VS Code** : Compare automatiquement les modèles installés localement avec votre fichier `chatLanguageModels.json`, signale les modèles manquants ou obsolètes, et permet d'ouvrir le fichier dans VS Code en 1 clic.
4. **[X] Gestionnaire d'arrêt & Libération mémoire** : Déchargez un modèle ou arrêtez les services d'un simple clic pour libérer 100% de la VRAM/RAM.
5. **[L] Moteur Llama.cpp MoE** : Offload des experts MoE sur CPU et attention sur GPU pour des performances maximales.

---

---

### Étape 3 : Basculer entre les Profils Actifs

Pour changer le modèle actif utilisé par défaut par VS Code :
```powershell
# Activer le profil C# 32B
.\scripts\Set-ActiveProfile.ps1 -Profile csharp -Tier 32b

# Activer le profil Réflexion Profonde (Maths & Physique Quantique)
.\scripts\Set-ActiveProfile.ps1 -Profile deep_reasoning -Tier 32b

# Activer le profil Outils & Agents
.\scripts\Set-ActiveProfile.ps1 -Profile agent_tools -Tier 32b
```

---

### Étape 4 : Tester un Modèle
Vous pouvez tester directement un profil avec un cas concret en ligne de commande :
```powershell
# Tester la génération C#
.\scripts\Test-Models.ps1 -Profile csharp

# Tester la démonstration de physique quantique
.\scripts\Test-Models.ps1 -Profile deep_reasoning

# Tester un appel d'outil JSON
.\scripts\Test-Models.ps1 -Profile agent_tools
```

---

## 🔌 Intégration avec VS Code & Copilot

Le serveur local expose l'API standard **OpenAI** sur `http://localhost:11434/v1` et l'API native Ollama sur `http://localhost:11434`.

Dans VS Code :
1. **GitHub Copilot Chat Custom Endpoints** : Préconfiguré dans `.vscode/settings.json`.
2. **Utilisation avec d'autres extensions IA (ex: Continue, Cline, Roo Code)** :
   - **Provider** : `Ollama` ou `OpenAI Compatible`
   - **Base URL** : `http://localhost:11434/v1`
   - **API Key** : `ollama` (non requise en local)
   - **Model Name** : `local-ia-active` (ou le nom exact, ex: `local-ia-csharp:32b`)
