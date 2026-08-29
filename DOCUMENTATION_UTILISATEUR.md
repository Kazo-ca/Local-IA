*[English](#english) below · [Version française](#francais) plus bas*

<a id="english"></a>
# LOCAL-IA Desktop Manager — User Guide

A desktop application (.NET 10 / WPF) for managing your local LLMs (Ollama and llama.cpp) without
touching the command line. It replaces day-to-day use of the PowerShell toolkit
(`Run-LocalIA.ps1` and the scripts in the `scripts/` folder), which remains intact and functional
as a fallback — the two can coexist, since the app never modifies `config/config.json` or the
existing Modelfiles.

Target machine: RTX 3060 (12 GB VRAM), Ryzen 7 3700X, 64 GB RAM, models on `E:\ollama_models` and
`E:\llama_models`.

---

## Launching the Application

From `src/`:

```bash
dotnet run --project LocalIA.App/LocalIA.App.csproj
```

On the very first launch (before any save), the app automatically imports the 3 profiles and 6
tiers from the existing `config/config.json` — it's a copy, not a link: anything you change
afterward in the app never affects the legacy file. Your app-specific configuration then lives in
`%APPDATA%\LocalIA\app-config.json` (plus a `.backup` safety copy next to it, used automatically
if the main file becomes unreadable).

---

## Tour of the Screens

The window has a left-hand navigation bar with 7 sections.

### Dashboard

Real-time overview: CPU/RAM usage, GPU/VRAM/temperature (refreshed every 1 to 2.5 seconds
depending on the metric), Ollama and llama.cpp status (Stopped / Starting / Running / Crashed),
models currently loaded in memory, and a live log stream.

Action buttons: Start Ollama, Unload Models, Stop Ollama, Stop llama.cpp, Stop All, and a checkbox
for automatic startup with Windows.

The app detects an engine already running outside of it (for example via the PowerShell scripts,
or started manually) and attaches to it instead of launching a second instance.

### Profiles / Models

Manages profiles (thematic groupings, e.g. "C# & .NET Expert") and their tiers (a configured
model, e.g. "max_accuracy"). Create/delete a profile or a tier, choose the engine (Ollama or
llama.cpp) and the base model, and set the system prompt.

Ollama-specific actions: download the base model (`ollama pull`), create/update the custom model
with your settings (`ollama create`), and set it as active.

The **Import from config/config.json** button re-reads the legacy toolkit on demand — useful if
you edit a Modelfile on the PowerShell side and want to pull those changes into the app.

An orange dot (**● unsaved changes**) appears as soon as a change hasn't been saved yet — click
**Save** to write it to disk. Closing the app with pending changes shows a prompt to save,
discard, or cancel the close.

### Chat / Test

Multi-turn conversation with token-by-token streaming, to test a model directly inside the app.
Choose the engine and model, type your message (Enter sends, Shift+Enter inserts a line break).
Performance statistics (tokens/s) are shown after each response. Generation can be cancelled
mid-stream.

### Model Configuration

The densest screen: nearly the entire Ollama and llama.cpp parameter surface (~150 flags),
organized into tabs — Sampling, Context & Memory, GPU & Offload, Networking & Server, Multimodal,
Speculative Decoding, Reasoning, Adapters, Advanced/Raw.

Each field has a "set/inherited" checkbox: checked = the value is sent to the engine; unchecked =
the engine uses its own default. A field not supported by the currently selected engine appears
greyed out with a tooltip. A preview at the bottom of the screen shows exactly the CLI arguments
(llama.cpp) or the JSON body (Ollama) that would be generated.

On the **GPU & Offload** tab, for a llama.cpp tier with a configured model source (local file or
Hugging Face repo), the **Start llama.cpp with this configuration** button actually launches
`llama-server.exe` with all of these settings — see the Limitations section below for the
prerequisite setup. A llama.cpp tier created by hand from Profiles (the + Add button) has **no**
model source until one is attached to it via the Hugging Face Search screen (the only place that
fills in a local file or a Hugging Face repo for a tier) — otherwise the button will clearly say
so.

At the bottom of the screen, the **Configuration Advisor** panel (see below) gives an automatic
opinion on how well the chosen model fits your hardware.

### MoE

A page dedicated to Mixture-of-Experts models (e.g. Qwen3.6-A3B, DeepSeek-Coder-V2-Lite). Shows
how much VRAM is available, reads the GGUF file's metadata (number of expert layers, size of
each), and calculates how many layers can stay in VRAM before some must be offloaded to CPU.

Simple mode: a slider that picks a contiguous prefix of layers to offload. Advanced mode: a grid
with one row per actual layer, with an individual GPU/CPU toggle. The app automatically generates
either `--n-cpu-moe N` or `--override-tensor` patterns depending on your selection.

### Hugging Face Search

Search for GGUF models on Hugging Face, with a list of the available quantizations and their
size. Actions: **+ Ollama** (downloads via `ollama pull hf.co/...`), **+ llama.cpp** (lets
`llama-server.exe` resolve/download it itself on first launch), **Download now** (fetches the file
right away — useful so the MoE page can read its metadata before the first launch).

When a repo contains companion files — a multimodal projector (`mmproj-*.gguf`) or a draft model
for speculative decoding (`mtp-*.gguf`, `draft-*.gguf`) — they appear in a separate section with
an **Attach** button, which automatically links them to the right tier setting (multimodal or
speculative decoding).

Models added here land in an automatically-created "Hugging Face" profile.

### Settings

**Not implemented yet** — the screen shows a placeholder message. Two global settings therefore
have no UI for now and must be edited directly in `%APPDATA%\LocalIA\app-config.json` (close the
app before editing, by hand or in a text editor):

```json
{
  "LlamaCppServer": {
    "ExecutablePath": "C:\\Users\\clefw\\repos\\source\\local-ia\\bin\\llama-cpp\\llama-server.exe"
  },
  "Preferences": {
    "HuggingFaceApiToken": "hf_..."
  }
}
```

Watch the exact casing (capitalized first letter of each word): the file doesn't use the usual
JSON camelCase, and a misspelled key is silently ignored with no error. `ExecutablePath` is
**required** for the "Start llama.cpp" button on the Model Configuration screen to work.
`HuggingFaceApiToken` is only needed for private/rate-limited Hugging Face repos.

---

## The Configuration Advisor

A panel built into the bottom of the "Model Configuration" screen. Two levels:

- **Instant calculation** (Recalculate button): no network call, based on already-read GGUF
  metadata and detected hardware. Verdict: Comfortable fit / Tight fit / Will fit in RAM (slower) /
  Doesn't fit. Recommends a number of GPU layers (dense model) or a MoE placement, and the largest
  context size that fits.
- **Ask the AI** (optional): sends a summary of the calculation to an already-installed Ollama
  model (the smallest one by default, so it doesn't compete for VRAM) for a second opinion in
  natural language. Requires Ollama to be running. Never applies a suggestion automatically — it's
  always up to you to change the settings if you agree.

If the source Hugging Face repo offers a multimodal projector or a draft model, the advisor
mentions it in its remarks.

---

## Known Limitations

- **Settings screen not implemented** — see above for the required manual configuration.
- **LoRA adapters**: the field exists for both engines, but only has a real effect for llama.cpp
  (`--lora`). Ollama expects a blob-upload mechanism that isn't implemented here yet — the field is
  greyed out on the Ollama side to avoid any confusion.
- **Custom host/port for llama.cpp**: if you change `--host`/`--port` in a tier's networking
  settings, the post-launch "is the server ready" check still targets the default address
  (`127.0.0.1:8080`) — the server starts correctly, but the app may wrongly show "Crashed" in this
  specific case. Leave host/port at their defaults unless you have a specific need.
- **Only one llama.cpp model at a time** — starting a new llama.cpp tier while another is running
  intentionally fails (so an ongoing generation is never cut off); stop the active instance from
  the Dashboard first.
- **Multi-file (sharded) GGUF**: if a model is split across several `.gguf` files (rare, mostly for
  very large models), metadata reading fails with an explicit message rather than silently
  returning a partial, incorrect result.

---

## Quick Troubleshooting

| Symptom | What to check |
|---|---|
| "Start llama.cpp" says "Path not configured" | Set `ExecutablePath` in `app-config.json` (see Settings above) |
| "Start Ollama" does nothing / errors out | Check that `ollama` is on the system PATH |
| A numeric field clears itself when typing a comma | Fixed — now accepts either a period or a comma |
| Nothing happens when searching on Hugging Face | Check your network connection; an error message should otherwise appear — report it if it doesn't |
| Changes lost after closing | The orange "unsaved changes" dot and the close-time prompt now warn you about this — click Save before closing if you see it |

---

<a id="francais"></a>
# LOCAL-IA Desktop Manager — Guide utilisateur

Application de bureau (.NET 10 / WPF) pour gérer tes modèles LLM locaux (Ollama et llama.cpp)
sans passer par la ligne de commande. Elle remplace au quotidien le toolkit PowerShell
(`Run-LocalIA.ps1` et les scripts du dossier `scripts/`), qui reste intact et fonctionnel comme
filet de secours — les deux peuvent coexister, l'app ne modifie jamais `config/config.json` ni
les Modelfiles existants.

Machine ciblée : RTX 3060 (12 Go VRAM), Ryzen 7 3700X, 64 Go RAM, modèles sur `E:\ollama_models`
et `E:\llama_models`.

---

## Lancer l'application

Depuis `src/` :

```bash
dotnet run --project LocalIA.App/LocalIA.App.csproj
```

Au tout premier lancement (avant toute sauvegarde), l'app importe automatiquement les 3 profils
et 6 paliers du `config/config.json` existant — c'est une copie, pas un lien : modifier quelque
chose ensuite dans l'app n'affecte jamais le fichier legacy. Ta configuration propre à l'app vit
ensuite dans `%APPDATA%\LocalIA\app-config.json` (plus une copie de secours `.backup` à côté,
utilisée automatiquement si le fichier principal devient illisible).

---

## Tour des écrans

La fenêtre a une barre de navigation à gauche avec 7 sections.

### Tableau de bord

Vue d'ensemble en temps réel : utilisation CPU/RAM, GPU/VRAM/température (rafraîchis toutes les
1 à 2,5 secondes selon la métrique), statut d'Ollama et de llama.cpp (Arrêté / Démarrage / Actif /
Planté), modèles actuellement chargés en mémoire, journal des logs en direct.

Boutons d'action : Démarrer Ollama, Décharger les modèles, Arrêter Ollama, Arrêter llama.cpp,
Tout arrêter, et une case à cocher pour le démarrage automatique avec Windows.

L'app détecte un moteur déjà lancé en dehors d'elle (par exemple via les scripts PowerShell ou
manuellement) et s'y attache au lieu d'en relancer un second.

### Profils / Modèles

Gestion des profils (regroupements thématiques, ex. « C# & .NET Expert ») et de leurs paliers
(un modèle configuré, ex. « max_accuracy »). Créer/supprimer un profil ou un palier, choisir le
moteur (Ollama ou llama.cpp) et le modèle de base, régler l'invite système.

Actions spécifiques à Ollama : télécharger le modèle de base (`ollama pull`), créer/mettre à jour
le modèle personnalisé avec tes réglages (`ollama create`), le définir comme actif.

Le bouton **Importer depuis config/config.json** relit le toolkit legacy à la demande — utile si
tu modifies un Modelfile côté PowerShell et veux récupérer les changements côté app.

Un point orange (**● modifications non enregistrées**) apparaît dès qu'un changement n'est pas
encore sauvegardé — clique **Enregistrer** pour l'écrire sur disque. Fermer l'app avec des
modifications en attente affiche une invite pour enregistrer, ignorer, ou annuler la fermeture.

### Chat / Test

Discussion multi-tours avec streaming token par token, pour tester un modèle directement dans
l'app. Choisis le moteur et le modèle, tape ton message (Entrée envoie, Maj+Entrée fait un saut
de ligne). Les statistiques de performance (tokens/s) s'affichent après chaque réponse.
L'annulation en cours de génération est possible.

### Configuration du modèle

L'écran le plus dense : la quasi-totalité des paramètres Ollama et llama.cpp (~150 flags),
organisés en onglets — Sampling, Contexte & Mémoire, GPU & Offload, Réseau & Serveur, Multimodal,
Décodage spéculatif, Raisonnement, Adaptateurs, Avancé/Brut.

Chaque champ a une case à cocher « défini/hérité » : coché = la valeur est envoyée au moteur ;
décoché = le moteur utilise son propre défaut. Un champ non supporté par le moteur actuellement
sélectionné apparaît grisé avec une info-bulle. Un aperçu en bas de l'écran montre exactement les
arguments CLI (llama.cpp) ou le corps JSON (Ollama) qui seraient générés.

Dans l'onglet **GPU & Offload**, pour un palier llama.cpp avec une source de modèle configurée
(fichier local ou dépôt Hugging Face), le bouton **Démarrer llama.cpp avec cette configuration**
lance réellement `llama-server.exe` avec tous ces réglages — voir la section Limitations
ci-dessous pour la configuration préalable nécessaire. Un palier llama.cpp créé à la main depuis
Profils (bouton + Ajouter) n'a **pas** de source de modèle tant qu'on ne lui en a pas attaché une
via l'écran Recherche Hugging Face (c'est le seul endroit qui renseigne un fichier local ou un
dépôt Hugging Face pour un palier) — le bouton affichera sinon un message l'indiquant clairement.

En bas de l'écran, le panneau **Conseiller de configuration** (voir plus loin) donne un avis
automatique sur l'adéquation matérielle du modèle choisi.

### MoE

Page dédiée aux modèles à experts (Mixture of Experts, ex. Qwen3.6-A3B, DeepSeek-Coder-V2-Lite).
Indique combien de VRAM est disponible, lit les métadonnées du fichier GGUF (nombre de couches
d'experts, taille de chacune), et calcule combien de couches peuvent rester en VRAM avant de
devoir en décharger sur CPU.

Mode simple : un curseur qui choisit un préfixe contigu de couches à décharger. Mode avancé :
une grille avec une ligne par couche réelle, bascule GPU/CPU individuelle. L'app génère
automatiquement soit `--n-cpu-moe N`, soit des motifs `--override-tensor` selon ta sélection.

### Recherche Hugging Face

Recherche de modèles GGUF sur Hugging Face, avec liste des quantifications disponibles et leur
taille. Actions : **+ Ollama** (télécharge via `ollama pull hf.co/...`), **+ llama.cpp** (laisse
`llama-server.exe` résoudre/télécharger lui-même au premier lancement), **Télécharger
maintenant** (récupère le fichier tout de suite, utile pour que la page MoE puisse lire ses
métadonnées avant le premier lancement).

Quand un dépôt contient des fichiers complémentaires — un projecteur multimodal (`mmproj-*.gguf`)
ou un modèle brouillon pour décodage spéculatif (`mtp-*.gguf`, `draft-*.gguf`) — ils apparaissent
dans une section séparée avec un bouton **Attacher**, qui les associe automatiquement au bon
réglage du palier (multimodal ou décodage spéculatif).

Les modèles ajoutés ici atterrissent dans un profil « Hugging Face » créé automatiquement.

### Paramètres

**Non implémenté pour l'instant** — l'écran affiche un message d'attente. Deux réglages globaux
n'ont donc pas d'interface pour l'instant et doivent être modifiés directement dans
`%APPDATA%\LocalIA\app-config.json` (fermer l'app avant d'éditer, à la main ou dans un éditeur de
texte) :

```json
{
  "LlamaCppServer": {
    "ExecutablePath": "C:\\Users\\clefw\\repos\\source\\local-ia\\bin\\llama-cpp\\llama-server.exe"
  },
  "Preferences": {
    "HuggingFaceApiToken": "hf_..."
  }
}
```

Attention à la casse exacte (majuscule en début de chaque mot) : le fichier n'utilise pas le
camelCase JSON habituel, une clé mal orthographiée est silencieusement ignorée sans erreur.
`ExecutablePath` est **obligatoire** pour que le bouton « Démarrer llama.cpp » de l'écran
Configuration du modèle fonctionne. `HuggingFaceApiToken` n'est nécessaire que pour les dépôts
Hugging Face privés/limités en accès.

---

## Le conseiller de configuration

Panneau intégré en bas de l'écran « Configuration du modèle ». Deux niveaux :

- **Calcul instantané** (bouton Recalculer) : sans appel réseau, à partir des métadonnées GGUF
  déjà lues et du matériel détecté. Verdict : Confortable / Serré / Tiendra en RAM (plus lent) /
  Ne tient pas. Recommande un nombre de couches GPU (modèle dense) ou un placement MoE, et la plus
  grande taille de contexte qui tient.
- **Demander à l'IA** (optionnel) : envoie un résumé du calcul à un modèle Ollama déjà installé
  (le plus petit par défaut, pour ne pas rivaliser en VRAM) pour un second avis en langage
  naturel. Nécessite qu'Ollama soit démarré. N'applique jamais automatiquement une suggestion —
  c'est toujours à toi de modifier les réglages si tu es d'accord.

Si le dépôt Hugging Face source propose un projecteur multimodal ou un modèle brouillon, le
conseiller le mentionne dans ses remarques.

---

## Limitations connues

- **Écran Paramètres non implémenté** — voir ci-dessus pour la configuration manuelle nécessaire.
- **Adaptateurs LoRA** : le champ existe pour les deux moteurs, mais n'a d'effet réel que pour
  llama.cpp (`--lora`). Ollama attend un mécanisme de téléversement de blob non encore implémenté
  ici — le champ est grisé côté Ollama pour éviter toute confusion.
- **Hôte/port personnalisés pour llama.cpp** : si tu changes `--host`/`--port` dans les réglages
  réseau d'un palier, la vérification « le serveur est-il prêt » après le démarrage continue de
  cibler l'adresse par défaut (`127.0.0.1:8080`) — le serveur démarre correctement mais l'app peut
  afficher « Planté » à tort dans ce cas précis. Laisse host/port sur leurs valeurs par défaut sauf
  besoin spécifique.
- **Un seul modèle llama.cpp à la fois** — démarrer un nouveau palier llama.cpp pendant qu'un
  autre tourne échoue intentionnellement (pour ne jamais couper une génération en cours) ; arrête
  d'abord l'instance active depuis le Tableau de bord.
- **GGUF multi-fichiers (sharded)** : si un modèle est découpé en plusieurs fichiers `.gguf`
  (rare, plutôt pour de très gros modèles), la lecture des métadonnées échoue avec un message
  explicite plutôt que de donner un résultat partiel silencieusement faux.

---

## Dépannage rapide

| Symptôme | Piste |
|---|---|
| « Démarrer llama.cpp » dit « Chemin non configuré » | Renseigner `ExecutablePath` dans `app-config.json` (voir Paramètres ci-dessus) |
| « Démarrer Ollama » ne fait rien / erreur | Vérifier qu'`ollama` est dans le PATH système |
| Un champ numérique se vide en tapant une virgule | Corrigé — utilise le point ou la virgule indifféremment maintenant |
| Rien ne se passe en cherchant sur Hugging Face | Vérifier la connexion réseau ; un message d'erreur devrait s'afficher sinon, à signaler si absent |
| Modifications perdues après fermeture | Le point orange « modifications non enregistrées » et l'invite à la fermeture préviennent maintenant de ce cas — clique Enregistrer avant de fermer si tu le vois |
