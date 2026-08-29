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
