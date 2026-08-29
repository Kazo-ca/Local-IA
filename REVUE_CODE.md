# Revue de code — LOCAL-IA Desktop Manager

Revue effectuée le 2026-08-28 sur `src/` (8467 lignes, ~120 fichiers), après la fin des 8 phases
du plan. Base de référence au moment de la revue : build propre (0 avertissement), 52/52 tests
xUnit au vert.

Méthode : 6 relecteurs indépendants, un par périmètre, chacun consigne pour retenir un constat :
« ne remonter que ce qui a été vérifié en lisant réellement le code, avec fichier + ligne, et en
cherchant activement à réfuter son propre constat avant de le garder ». Plusieurs constats ont été
vérifiés empiriquement (harnais de test jetables, calculs rejoués à la main) plutôt que par simple
lecture. Les doublons entre relecteurs ont été fusionnés.

Statut : ⬜ à corriger · ✅ corrigé · ❓ décision utilisateur nécessaire · 🔍 à vérifier en externe

**État final (2026-08-29) : les 41 constats sont corrigés.** Build propre (0 avertissement),
69/69 tests xUnit au vert (52 initiaux + 17 nouveaux, dont un fichier de test binaire GGUF créé
de toutes pièces pour verrouiller M15). Chaque correctif a été suivi d'un build + passage complet
des tests avant de passer au suivant. Les correctifs touchant l'UI (C2, H6, H7) ont en plus été
vérifiés en direct sur l'application réelle.

**Trouvé en testant en direct, au-delà des constats d'origine** : deux bugs de réentrance dans le
nouveau gestionnaire `Window.Closing` (H6), tous deux causés par la même règle WPF (« impossible
d'appeler Close() pendant qu'une fenêtre est déjà en cours de fermeture ») :
1. Cliquer plusieurs fois sur Fermer pendant que l'invite « modifications non enregistrées » est
   déjà affichée empilait plusieurs boîtes de dialogue — corrigé avec un drapeau `_closePromptOpen`
   qui empêche d'en empiler une seconde.
2. Même après ce premier correctif, rappeler `Close()` en synchrone (dans la même invocation de
   `Closing`, y compris juste après un clic sur Non/Oui) levait la même exception — corrigé en
   reportant ce second appel via `Dispatcher.BeginInvoke(Close)` plutôt qu'un appel direct.

Les deux fois, l'exception a été interceptée proprement par le filet de sécurité global déjà en
place (`DispatcherUnhandledException`) plutôt que de faire planter l'application — mais les deux
étaient de vrais bugs qui auraient empêché de fermer l'app normalement. Vérifié en direct après
correction : fermeture propre sans modification en attente, et fermeture propre après clic sur
Non avec une modification en attente. Deux branches non testées de `GgufFileRoleClassifier`
(suffixes `-mtp`/`-draft`) ont aussi été couvertes au passage.

**Trouvé en rédigeant la documentation développeur, en re-décrivant chaque correctif** : deux
gaps découverts en vérifiant mes propres affirmations avant de les écrire noir sur blanc.
1. Le correctif M4 n'avait été appliqué qu'à `OllamaProcessManager` — `LlamaCppProcessManager`
   avait toujours la même course. Une application mécanique du même correctif y aurait en plus
   causé un blocage silencieux : `StartAsync` y appelle `RefreshStatusAsync` en réentrance en
   détenant déjà `_gate` (non réentrant), donc un second `WaitAsync` non bloquant y aurait
   toujours échoué et sauté à tort le rafraîchissement demandé. Corrigé en extrayant
   `RefreshStatusCoreAsync` (sans acquisition de `_gate`), appelé directement par `StartAsync`.
2. `LlamaCppProcessManager.StartAsync` avait le même défaut que M12 (`Win32Exception` non
   interceptée sur `process.Start()`) — non repéré à l'origine car ce code était alors mort (C2).
   Devenu atteignable une fois le bouton « Démarrer llama.cpp » câblé, avec un chemin
   d'exécutable configurable seulement par édition manuelle de JSON (donc plus sujet à une faute
   de frappe qu'un champ d'UI validé) — corrigé par le même `catch (Win32Exception)` que
   `OllamaProcessManager`.

---

## Critique

### [C1] ✅ Palier « détaché » après ajout depuis Recherche Hugging Face — pertes de réglages silencieuses
**Fichier** : `LocalIA.App/ViewModels/HuggingFaceSearchViewModel.cs:287-306`

`AddTierAndNavigateAsync` sauvegarde le nouveau palier sur disque, recharge `ProfilesViewModel`
avec de **nouvelles instances**, mais passe ensuite l'**ancienne** variable locale `tier` à
`ModelConfigurationViewModel.LoadTier(tier)`. Tout réglage fait immédiatement après l'ajout d'un
modèle (le flux normal : ajouter → configurer) est fait sur un objet détaché ; au prochain
« Enregistrer » depuis Profils, c'est l'objet rechargé (non modifié) qui est sérialisé — le
réglage disparaît silencieusement, sans erreur ni avertissement.

**Correctif** : après `RefreshFromDiskIfClean()`, retrouver le palier par `Id` dans la config
rechargée avant d'appeler `LoadTier` (le même pattern existe déjà dans `AttachAuxiliaryFileAsync`
un peu plus bas dans le même fichier).

### [C2] ✅ `LlamaCppProcessManager.StartAsync` n'était appelé nulle part dans l'application
**Fichiers** : `LocalIA.Infrastructure/Engines/LlamaCppProcessManager.cs`,
`LocalIA.Core/Models/LlamaCppLaunchSettings.cs`, `LocalIA.App/ViewModels/ModelConfigurationViewModel.cs`

Décision utilisateur : câbler un vrai démarrage. `LlamaCppLaunchSettings` a été réécrit avec une
factory `LlamaCppLaunchSettingsFactory.FromTier(...)` qui résout la source du modèle (fichier
local → `-m`, sinon `-hf`/`-hff`/`-hft`) puis délègue tous les autres flags à
`LlamaCppArgumentBuilder.Build()` (le même générateur que l'aperçu — plus de second générateur
séparé). Un bouton « Démarrer llama.cpp avec cette configuration » a été ajouté dans l'onglet
GPU & Offload de « Configuration du modèle », visible seulement pour un palier llama.cpp.
Vérifié en direct : le bouton apparaît/disparaît correctement selon le moteur, et le clic
déclenche bien toute la chaîne (chargement config, résolution MoE, construction des arguments,
appel à `StartAsync`) jusqu'au message d'erreur attendu quand `llama-server.exe` n'est pas encore
configuré. `LlamaCppLaunchSettingsTests.cs` a été entièrement réécrit (6 tests) pour la nouvelle
factory.

---

## Haute

### [H1] ✅ Double comptage de `NonLayerTensorsSizeBytes` dans le calcul GPU (modèle dense)
**Fichier** : `LocalIA.Core/Advisor/ConfigurationAdvisor.cs:107-114`

`bytesPerLayer` inclut `NonLayerTensorsSizeBytes` dans sa moyenne par couche, alors que `budget`
le soustrait *déjà* séparément. Le coût des tenseurs hors-couches est déduit deux fois avant de
décider combien de couches tiennent en VRAM — un modèle dense qui tiendrait réellement en entier
peut être annoncé comme ne tenant que partiellement.

### [H2] ✅ `BlockCount == 0` rend le verdict dense trivialement « Confortable »
**Fichier** : `LocalIA.Core/Advisor/ConfigurationAdvisor.cs:107-114` — **vérifié empiriquement**

Si les métadonnées GGUF sont dégénérées (architecture non reconnue → toutes les clés préfixées
retombent à 0), `bytesPerLayer = 0` → `gpuLayers = 0` → `fits = (0 >= 0) = true`. L'app annonce
« tient entièrement en VRAM (0 couches) » alors qu'elle n'a aucune information exploitable.

### [H3] ✅ `GgufMetadataReader` ne gère pas les GGUF multi-shards
**Fichier** : `LocalIA.Core/Gguf/GgufMetadataReader.cs:21,89`

Aucune lecture des clés `split.count`/`split.no`. Sur un modèle découpé en plusieurs fichiers,
les couches situées dans les shards non lus restent à zéro silencieusement, et
`TotalFileSizeBytes` ne mesure que le premier fichier. Confirmé atteignable : la recherche
Hugging Face ne garde qu'un seul fichier par groupe de shards, et le téléchargeur ne récupère
que ce fichier.

### [H4] ✅ Sauvegardes concurrentes non coordonnées entre ViewModels
**Fichiers** : `LocalIA.App/ViewModels/ProfilesViewModel.cs:70-76,147-154`,
`HuggingFaceSearchViewModel.cs:287-306`

Éditer un profil dans Profils (sans enregistrer) puis ajouter un modèle via Recherche Hugging
Face, puis revenir enregistrer l'édition Profils : le `SaveAsync` final écrase le palier HF déjà
persisté sur disque, avec la copie mémoire périmée de Profils. Le modèle téléchargé (potentiellement
plusieurs Go) devient orphelin de toute configuration.

### [H5] ✅ Le fichier `.backup` est écrit mais jamais relu ; une erreur I/O réinitialise la config
**Fichier** : `LocalIA.Infrastructure/Configuration/AppConfigRepository.cs:24-44,86-89`

`LoadAsync` ne catch que `JsonException` (pas `IOException`/`UnauthorizedAccessException`, qui
remontent alors comme exception non observée depuis un fire-and-forget dans
`ProfilesViewModel` constructeur). Et même en cas de `JsonException`, le `.backup` écrit à
chaque sauvegarde n'est jamais consulté avant de retomber sur une config vide.

### [H6] ✅ Fermer l'app ne prévient jamais des modifications non enregistrées
**Fichiers** : `LocalIA.App/App.xaml.cs:46-55`, `MainWindow.xaml.cs`,
`LocalIA.App/Configuration/SettingsFieldDescriptor.cs:83-104`

Aucun gestionnaire `Window.Closing`. Pire : `IsDirty` (le drapeau qui piloterait un tel
avertissement) n'est jamais mis à jour quand on édite un des ~90 champs de réglage via
« Configuration du modèle » — seuls Ajouter/Supprimer profil/palier le déclenchent.

**Correctif** : `ModelConfigurationViewModel` expose un événement `SettingsChanged` (levé sur
toute édition de champ ou changement de moteur), auquel `ProfilesViewModel` s'abonne pour marquer
`IsDirty=true`. `MainWindow` a désormais un gestionnaire `Closing` qui invite à
enregistrer/ignorer/annuler si des modifications sont en attente. Bug de réentrance découvert et
corrigé en testant en direct : cliquer plusieurs fois sur Fermer pendant que l'invite est déjà
affichée empilait plusieurs boîtes de dialogue jusqu'à provoquer une `InvalidOperationException`
(`Close()` appelé pendant la fermeture) — un drapeau `_closePromptOpen` empêche maintenant
d'empiler une seconde invite.

### [H7] ✅ `MoeViewModel` (singleton) réaffiche les métadonnées du palier précédent après navigation
**Fichier** : `LocalIA.App/ViewModels/MoeViewModel.cs:69-83`

Quand le nouveau palier n'a pas de `LocalFilePath`, `GgufFilePath` n'est jamais remis à `null` —
la page MoE réutilise le fichier GGUF de l'ancien palier consulté. Un « Enregistrer » ultérieur
peut persister des indices de couches CPU calculés sur le mauvais modèle.

### [H8] ✅ Décalage de culture lecture/écriture sur les champs numériques → perte silencieuse sur machine fr-FR
**Fichier** : `LocalIA.App/Configuration/SettingsFieldDescriptor.cs:83-104`

`TextValue` (setter) parse en `CultureInfo.InvariantCulture` (point décimal) mais le getter fait
un `.ToString()` nu (virgule décimale sur Windows fr-FR/fr-CA). Taper une virgule dans un champ
comme Température fait échouer le `TryParse` → la valeur repasse à `null` → le champ se vide sous
les yeux de l'utilisateur, sans message.

### [H9] ✅ Timeout HTTP Ollama (5 min) insuffisant pour `/api/pull` et `/api/create` sur un gros modèle
**Fichier** : `LocalIA.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs:34-38`

Le commentaire du code affirme que 5 minutes couvrent le streaming de `/api/pull`, mais
`HttpClient.Timeout` s'applique à la durée totale de la requête, corps compris. Un modèle de
plusieurs Go sur une connexion domestique dépasse couramment 5 minutes de téléchargement — le
client dédié aux téléchargements GGUF utilise correctement `Timeout.InfiniteTimeSpan` pour la
même raison, mais ce client Ollama partagé ne l'a pas.

### [H10] ✅ Générateur de groupes de réglages dupliqué 3 fois — risque de divergence Ollama/llama.cpp
**Fichiers** : `LocalIA.Core/Configuration/LlamaCppArgumentBuilder.cs:101-111`,
`OllamaParameterBuilder.cs:69-79`, `LocalIA.App/ViewModels/ModelConfigurationViewModel.cs:95-102,115-117`

La liste des 8 groupes de `ModelTierSettings` à parcourir par réflexion est copiée à l'identique
à 3 endroits. Un futur groupe ajouté à un seul endroit serait appliqué à un moteur (ou affiché
dans l'UI) mais pas à l'autre, silencieusement.

### [H11] ✅ Import du Modelfile legacy : seules 4 clés `PARAMETER` sur une douzaine sont reconnues
**Fichier** : `LocalIA.Infrastructure/Configuration/LegacyConfigImporter.cs:96-116`

`ParseModelfile` ne reconnaît que `temperature`/`top_p`/`repeat_penalty`/`num_ctx`. Toute autre
clé (`stop`, `seed`, `mirostat`, `num_gpu`…) d'un Modelfile existant est ignorée sans log ni
message — l'import « réussit » en ne récupérant qu'une fraction des réglages.

---

## Moyenne

### [M1] ✅ Dimension de tête KV dérivée par simple division, sans lire `key_length`/`value_length`
**Fichiers** : `LocalIA.Core/MoeOffload/MoeVramCalculator.cs:106`, `Advisor/ConfigurationAdvisor.cs:159`

`headDim = EmbeddingLength / AttentionHeadCount` — les clés GGUF standard
`{arch}.attention.key_length`/`value_length` (qui existent précisément parce que cette division
ne suffit pas pour certaines architectures) ne sont jamais lues par `GgufMetadataReader`.

### [M2] ✅ Le type de cache K/V réellement configuré n'atteint jamais le calculateur — et la formule est dupliquée avec divergence
**Fichiers** : `Advisor/ConfigurationAdvisor.cs:81-87,152-163`, `App/ViewModels/MoeViewModel.cs:157-163`,
`Core/MoeOffload/MoeVramCalculator.cs:99-112`

`ContextMemorySettings.CacheTypeK/V` est un réglage réel et testé, mais aucun des deux points de
construction de `MoeVramCalculatorInput` ne le renseigne (F16 par défaut systématiquement) — donc
configurer un cache quantifié pour gagner de la VRAM sur une carte à 12 Go n'a aucun effet sur la
recommandation. De plus, `ConfigurationAdvisor` réécrit sa propre version de l'estimation KV,
codée en dur en F16, séparée de celle (paramétrable) de `MoeVramCalculator` — contredisant le
commentaire de classe qui affirme l'absence de logique dupliquée.

### [M3] ✅ Réentrance sur `OllamaProcessManager.EnsureRunningAsync` — `IsOwnedProcess` corrompu
**Fichier** : `LocalIA.Infrastructure/Engines/OllamaProcessManager.cs:59-65`

Contrairement à `LlamaCppProcessManager.StartAsync`, ne vérifie pas `_ownedProcess is {
HasExited: false }` en premier. Un double-clic sur « Démarrer Ollama » (le bouton n'est jamais
désactivé pendant l'opération) fait repasser `IsOwnedProcess` à `false` et démarre une seconde
source de logs en doublon.

### [M4] ✅ `RefreshStatusAsync` non protégé par `_gate` — course avec Start/Stop
**Fichiers** : `OllamaProcessManager.cs:33-52`, `LlamaCppProcessManager.cs:30-55`

Le tick de monitoring (toutes les 2,5 s) peut lire l'état en même temps qu'un Start/Stop
utilisateur et réafficher transitoirement un statut incohérent (« Starting » avec un PID déjà
tué). S'autocorrige au tick suivant, mais visible quelques secondes.

**Correction en cours de rédaction de la documentation développeur** : le premier correctif
n'avait été appliqué qu'à `OllamaProcessManager` — `LlamaCppProcessManager` était resté avec la
même course. Une application mécanique du même correctif y aurait en plus causé un blocage :
`LlamaCppProcessManager.StartAsync` appelle `RefreshStatusAsync` en réentrance en détenant déjà
`_gate` (un `SemaphoreSlim` n'est pas réentrant), donc un second `WaitAsync`, même non bloquant,
y échouerait toujours et sauterait à tort le rafraîchissement demandé. Corrigé en extrayant la
logique dans `RefreshStatusCoreAsync` (sans acquisition de `_gate`) : la méthode publique
`RefreshStatusAsync` acquiert `_gate` en non bloquant puis délègue au cœur ; `StartAsync` appelle
directement le cœur puisqu'il détient déjà `_gate`.

### [M5] ✅ Tâches fire-and-forget dont l'exception disparaît silencieusement
**Fichiers** : `ChatViewModel.cs:53,56`, `ConfigurationAdvisorViewModel.cs:73`, `DashboardViewModel.cs:100`

`_ = RefreshAvailableModelsAsync()` / `_ = EvaluateAsync()` / `_ = RefreshAutostartStateAsync()`
sans try/catch dans les méthodes visées : au premier lancement (avant que l'utilisateur ait
démarré un moteur), l'appel réseau échoue et disparaît sans le moindre message.

### [M6] ✅ `_computer.Open()` (LibreHardwareMonitor) hors du try/finally
**Fichier** : `LocalIA.Infrastructure/Hardware/HardwareMonitoringService.cs:59`

Si `Open()` lève après une initialisation partielle des capteurs, `Close()` (dans le `finally`
juste en dessous) ne s'exécute jamais.

### [M7] ✅ `App.xaml.cs.OnStartup` : aucune barrière d'exception → processus fantôme sans fenêtre
**Fichier** : `LocalIA.App/App.xaml.cs:14-30`

Si `_host.StartAsync()` ou la résolution de `MainWindow` lève (ex. conséquence de M6), le
`DispatcherUnhandledException` global intercepte bien l'exception et empêche la fermeture
automatique — mais `mainWindow.Show()` n'a jamais été atteint. L'utilisateur voit le message
d'erreur, clique OK, et l'application continue de tourner indéfiniment sans aucune fenêtre
visible.

### [M8] ✅ `SchemaVersion` défini et sérialisé, mais jamais lu
**Fichier** : `LocalIA.Core/Models/AppConfig.cs:5`

Aucun mécanisme de migration ne s'appuie dessus — un futur changement de forme d'`AppConfig`
tomberait directement dans le scénario H5 (perte de configuration).

### [M9] ✅ Le bouton « Importer depuis config/config.json » devient inopérant après la première sauvegarde
**Fichier** : `LocalIA.App/ViewModels/ProfilesViewModel.cs:159-186`

`ImportLegacyAsync` relit `app-config.json` (déjà sauvegardé) au lieu du fichier legacy dès que
`app-config.json` existe — le fichier legacy n'est plus jamais consulté après la toute première
sauvegarde de l'app.

### [M10] ✅ `JsonException` non rattrapée dans `ConfigurationAdvisorViewModel.AskAiAsync`
**Fichier** : `LocalIA.App/ViewModels/ConfigurationAdvisorViewModel.cs:180-196`

Même famille que le bug déjà corrigé (timeout Ollama) : une réponse 200 non conforme d'Ollama
lève `JsonException`, non couverte par les deux clauses `catch` existantes.

### [M11] ✅ `JsonException` non rattrapée dans `HuggingFaceClient`, combinée à des appels fire-and-forget
**Fichier** : `LocalIA.Infrastructure/HuggingFace/HuggingFaceClient.cs:26,49`

Tous les appelants de `SearchModelsAsync`/`ListRepoFilesAsync` sont invoqués en tâche perdue
(`_ = ...`). Une réponse HF mal formée disparaît sans le moindre message à l'utilisateur.

### [M12] ✅ `OllamaProcessManager.EnsureRunningAsync` non protégé contre `Win32Exception`
**Fichier** : `LocalIA.Infrastructure/Engines/OllamaProcessManager.cs:54-112`

Si `ollama` n'est pas dans le PATH, `Process.Start()` lève `Win32Exception` au lieu de retourner
`false` — non catchée ici (alors que `OllamaApiClient.UnloadModelAsync` le fait déjà pour le même
exécutable).

### [M13] ✅ `HuggingFaceSearchViewModel.AddToOllamaAsync` — aucun `catch` du tout
**Fichier** : `LocalIA.App/ViewModels/HuggingFaceSearchViewModel.cs:172-214`

Contrairement à ses méthodes sœurs du même fichier (`DownloadNowAsync`,
`AttachAuxiliaryFileAsync`), aucune gestion d'erreur — un tag `hf.co/...` invalide remonte
jusqu'au popup générique global.

### [M14] ✅ `GgmlTypeTraits.ComputeSizeBytes` : repli à 0 octet sur type inconnu, jamais testé
**Fichier** : `LocalIA.Core/Gguf/GgmlType.cs:85-106`

Comportement documenté comme voulu (ne pas planter sur un type futur), mais aucun test ne
verrouille ce repli — une régression future pourrait le changer sans qu'aucun test ne le détecte,
avec pour conséquence une sous-estimation silencieuse de la VRAM nécessaire.

### [M15] ✅ Classification MoE/dense par nom de tenseur — ordre critique non verrouillé par un test
**Fichier** : `LocalIA.Core/Gguf/GgufMetadataReader.cs:110-121`

Un vrai tenseur d'expert (`blk.0.ffn_gate_exps.weight`) contient à la fois `"_exps."` et
`".ffn_"` — l'ordre du `if`/`else if` est donc significatif mais non documenté ni testé. Un
réordonnancement anodin reclasserait silencieusement tout modèle MoE en dense.

### [M16] ✅ `ChooseLargestFittingContext` peut renvoyer un contexte jamais vérifié comme tenant
**Fichier** : `LocalIA.Core/Advisor/ConfigurationAdvisor.cs:128-150`

`best` est initialisé au plus petit candidat *avant* toute vérification VRAM. Si même ce plus
petit candidat ne tient pas, la méthode le retourne quand même comme s'il avait été validé.

---

## Basse

### [B1] ✅ `OnExit` sans try/finally autour de `_host.Dispose()`
`LocalIA.App/App.xaml.cs:46-55` — si `StopAsync` lève, aucun singleton `IDisposable` n'est nettoyé.

### [B2] ✅ `_ownedProcess` (objet `Process` .NET) jamais disposé
`OllamaProcessManager.cs:88,128,238-242`, `LlamaCppProcessManager.cs:93,137,198` — fuite mineure
par cycle démarrage/arrêt, récupérée par le finaliseur.

### [B3] ✅ `_logTailCts` annulé mais jamais disposé
`OllamaProcessManager.cs:120-121,240` — impact quasi nul (pas de handle noyau alloué ici).

### [B4] ✅ `ChatView` : fuite mémoire par abonnement `CollectionChanged` jamais désabonné
`LocalIA.App/Views/ChatView.xaml.cs:10-27` — chaque navigation vers l'onglet Chat recrée une
instance (le `ContentControl` ne réutilise jamais l'ancienne), sans jamais désabonner l'ancienne.

### [B5] ✅ Formatage octets → Go dupliqué
`Converters/BytesToGigabytesConverter.cs:17-18` vs `ConfigurationAdvisorViewModel.cs:242`.

### [B6] ✅ `LocalIA.Core/Class1.cs` et `LocalIA.Infrastructure/Class1.cs` — squelettes morts
Reliquats de `dotnet new classlib`, aucune référence ailleurs.

### [B7] ✅ `GpuLayerSpec.ToString()` mort
`LocalIA.Core/Configuration/SettingsEnums.cs:15-19` — jamais appelé, tous les usages excluent
explicitement ce type de la boucle réflexive et bindent `.Mode`/`.ExplicitCount` séparément.

### [B8] ✅ `TryKill` dupliqué à l'identique entre les deux gestionnaires de process
`OllamaProcessManager.cs:138-149`, `LlamaCppProcessManager.cs:148-159`.

### [B9] ✅ Renommer le profil auto-généré « Hugging Face » crée un doublon au prochain ajout
`HuggingFaceSearchViewModel.cs:290-295` — recherché par correspondance exacte de nom, pas par id stable.

### [B10] ✅ `OllamaApiClient.CopyModelAsync` ne rattrape pas `TaskCanceledException`
`LocalIA.Infrastructure/Clients/OllamaApiClient.cs:104-115` — impact faible (`/api/copy` est rapide).

### [B11] ✅ `MoeViewModel.BrowseFile` peut désynchroniser la sélection CPU/GPU du fichier réellement configuré
`LocalIA.App/ViewModels/MoeViewModel.cs:85-93` — cas d'usage marginal (parcourir un autre fichier
que celui du palier pour comparer).

---

## Vérifié en externe

### [R1] ✅ `AdapterSettings` mappait l'adaptateur LoRA sur le paramètre Ollama `"adapter"`
**Fichier** : `LocalIA.Core/Configuration/AdapterSettings.cs:7`

Confirmé via la documentation de l'API Ollama : `/api/create` attend un champ de premier niveau
`"adapters"` (dictionnaire nom de fichier → digest SHA256 d'un blob déjà téléversé via
`/api/blobs/sha256:<digest>`), pas une clé dans `parameters`. L'ancien mapping produisait donc une
clé `adapter` silencieusement ignorée par Ollama — sans effet, sans erreur. Implémenter le
téléversement de blob complet serait une vraie fonctionnalité nouvelle (hors périmètre d'une
revue de bugs) ; le correctif proportionné retenu retire le mapping incorrect
(`OllamaParameter = null`), pour que l'UI existante affiche correctement ce champ comme non
supporté par Ollama (comme toute autre option llama.cpp-uniquement) plutôt que de laisser croire
qu'il fait quelque chose.

---

## Non retenu après contre-vérification

Pour transparence — pistes explorées puis écartées par les relecteurs eux-mêmes car non
atteignables ou déjà gérées correctement : double `_computer.Close()`, exception dans
`ChatViewModel.SendAsync` avant le `try` (chemin non atteignable avec les 2 seules valeurs
d'`EngineKind` existantes), fuite via abonnements `IMessenger`/événements sur les ViewModels
(tous singletons, même durée de vie que leurs éditeurs), duplication de la détection de process
(en réalité déjà centralisée dans `ProcessLookup`), convertisseurs WPF non enregistrés (tous
vérifiés présents), `Run.Text` sans `Mode=OneWay` (tous les cas restants pointent vers des
propriétés à setter réel ou `init`), copie superficielle lors d'une duplication de profil/palier
(cette fonctionnalité n'existe pas dans le code).
