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
