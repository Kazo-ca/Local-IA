*[English](#english) below · [Version française](#francais) plus bas*

<a id="english"></a>
# LOCAL-IA Desktop Manager — Developer Documentation

A .NET 10 / WPF (MVVM) application under `src/`, which replaces the historical PowerShell toolkit
(`scripts/`, `config/`, left intact) for managing local LLM inference engines (Ollama,
llama.cpp/CUDA) on a personal machine (RTX 3060 12 GB, 64 GB RAM).

See also: [REVUE_CODE.md](REVUE_CODE.md) (the full history of a code review — 41 findings fixed,
with evidence and failure scenarios — French only) and the original implementation plan at
`C:\Users\clefw\.claude\plans\crystalline-hopping-puddle.md`.

---

## Architecture at a Glance

```
src/
├── LocalIA.Core/            net10.0, ZERO Windows/WPF dependency — pure, testable logic
├── LocalIA.Infrastructure/  net10.0-windows — HTTP, Process, WMI, LibreHardwareMonitor
├── LocalIA.App/             net10.0-windows, WPF — ViewModels, Views, DI composition root
└── LocalIA.Tests/           xUnit — tests Core + Infrastructure (no WPF tests)
```

Dependency direction: `App → Infrastructure → Core`, and `App → Core` directly for ViewModels
that only need pure logic. `Core` never references `Infrastructure` or `App`. This separation is
what lets `LocalIA.Tests` test the GGUF parser, the MoE calculator, the AI advisor, and the
argument builders without any WPF dependency.

**Composition root**: `LocalIA.App/App.xaml.cs`, via `Microsoft.Extensions.Hosting` (Generic
Host). `builder.Services.AddLocalIaInfrastructure()` (in
`LocalIA.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`) and
`.AddLocalIaApp()` (in `LocalIA.App/DependencyInjection/AppServiceCollectionExtensions.cs`). Page
ViewModels are registered as singletons (state — chat, selection, loaded config — must survive
navigation); `MainWindow` is transient.

**Global safety net**: `App.xaml.cs` intercepts `DispatcherUnhandledException` (log + popup +
`e.Handled = true`) so an error in a secondary feature never closes the entire application.
`OnStartup` has its own separate try/catch guard, because an exception before `mainWindow.Show()`
would otherwise leave a ghost process with no window (the global `DispatcherUnhandledException`
only catches what happens AFTER that point).

---

## The Core Mechanism: `EngineFlagAttribute`

This is what makes the ~150-parameter Ollama/llama.cpp surface manageable. Each setting is a
nullable `[ObservableProperty]` property (in a class under `LocalIA.Core/Configuration/`, grouped
by theme — `SamplingSettings`, `ContextMemorySettings`, `GpuOffloadSettings`, etc.), annotated with
`[property: EngineFlag(OllamaParameter: "...", LlamaCppFlag: "...", DisplayName: "...")]`. `null`
on either one means "not supported by that engine".

**To add a new parameter**: a property + the attribute, in the relevant theme group. No other
file to touch — both builders and the UI pick it up automatically through reflection:

- `LlamaCppArgumentBuilder.Build(ModelTierSettings, totalMoeLayers)` → `List<string>` of CLI
  arguments.
- `OllamaParameterBuilder.Build(ModelTierSettings)` → `Dictionary<string, object>` for the
  `/api/create` JSON body.
- `SettingsFieldDescriptor.FromGroup(...)` (in `LocalIA.App/Configuration/`) → generically feeds
  the UI (a single `DataTemplate` for every field, no dedicated control per field).

Both builders and `ModelConfigurationViewModel` iterate the same list of 8 groups via
`ModelTierSettings.GetEngineFlagGroups()` — a single entry point, so that a group added later is
automatically picked up everywhere (see the history in REVUE_CODE.md, finding H10: this was a
real source of bugs in the past, when three copies of this same list had drifted apart).

`MoeOffloadSettings` and `RawOverrides` are deliberately excluded from this generic loop
(dedicated logic: `--n-cpu-moe`/`--override-tensor` generation, and a free-form escape hatch).
`GpuLayerSpec` (Auto/All/Explicit mode) is also handled separately in both builders.

---

## Configuration Model and Persistence

```
AppConfig (SchemaVersion, Storage, OllamaServer, LlamaCppServer, Profiles, Preferences)
  └─ ModelProfile (Id, Name, Description, Tiers)
       └─ ModelTier (Id, Label, Engine, OllamaBaseModel/CustomModelName, LlamaCppSource, Settings)
            └─ ModelTierSettings (8 EngineFlag groups + MoeOffload + Raw)
```

Persisted to `%APPDATA%\LocalIA\app-config.json` via `AppConfigRepository`
(`LocalIA.Infrastructure/Configuration/`) — atomic write (`.tmp` then `File.Move`), a `.backup`
safety copy on every save, and automatically **re-read** from that backup if the main file is
unreadable (corrupted JSON, transient I/O lock) before falling back to a default config.

`config/config.json` (the legacy toolkit) is **never written** by the app — `LegacyConfigImporter`
reads it read-only, once on the very first launch (`AppConfigRepository.LoadAsync` when
`app-config.json` doesn't exist yet), or on demand via `IAppConfigRepository.ImportLegacyAsync()`
(the "Import" button in Profiles, independent of whether the app file exists — that wasn't always
the case, see finding M9 of the review).

`LegacyConfigImporter.ParseModelfile` recognizes a Modelfile's `PARAMETER` keys via a reverse
lookup table built by reflection over the same `EngineFlag` attributes (not a hardcoded list of
keys) — so any parameter already modeled elsewhere in the app is automatically recognized on
import.

**Careful when changing this model**: `ProfilesViewModel` and `HuggingFaceSearchViewModel` each
load/save their own independent copy of `AppConfig` (no centralized shared state).
`ProfilesViewModel.RefreshFromDiskIfClean()` merges by `Id` any profiles/tiers that appeared on
disk in the meantime, rather than overwriting unsaved local changes — if you add a third write
path, you'll likely need to fold it into this same merge mechanism to avoid losing data.

---

## Key Abstractions

| Interface | Implementation | Role |
|---|---|---|
| `IOllamaProcessManager` / `ILlamaCppProcessManager` | `OllamaProcessManager` / `LlamaCppProcessManager` | Start/stop, attach-rather-than-duplicate, log capture |
| `IOllamaApiClient` / `ILlamaCppApiClient` | `OllamaApiClient` / `LlamaCppApiClient` | REST + streaming (NDJSON / SSE) |
| `IHardwareMonitorService` | `HardwareMonitoringService` (`BackgroundService`) | 3 independent loops (CPU/RAM 1s, GPU 1.5s, engines 2.5s) |
| `IGgufMetadataReader` | `GgufMetadataReader` | Binary GGUF parser (header + tensors, never the weights) |
| `IMoeVramCalculator` | `MoeVramCalculator` | Computes the smallest prefix of MoE layers to offload to CPU |
| `IConfigurationAdvisor` | `ConfigurationAdvisor` | Delegates to `IMoeVramCalculator` for MoE models; estimates the GPU/CPU split itself for dense models |
| `IHuggingFaceClient` / `IGgufDownloader` | `HuggingFaceClient` / `GgufDownloader` | HF search/file tree, resumable download (`Range`) |
| `IAppConfigRepository` | `AppConfigRepository` | Persistence + legacy import |

`OllamaApiClient`/`LlamaCppApiClient` are registered with a 5-minute HTTP timeout (not infinite,
not 5 seconds) — long enough for a cold model load before the first response, short enough to
never mask a real problem indefinitely. `/api/pull` and `/api/create` use a **separate** HTTP
client with no timeout at all (`OllamaApiClient.LongRunningHttpClientName`), because a multi-GB
download routinely exceeds 5 minutes, and `HttpClient.Timeout` applies to the entire request —
streaming body included — not just the headers.

---

## GGUF Parser — Points of Attention

`GgufMetadataReader.Read` reads the magic number + version + counts, then the metadata key/value
pairs, then the tensor info (name/dims/type/offset) — never the weights themselves (a few KB to a
few MB read, not the file's gigabytes).

Dense-vs-MoE layer classification is done **by tensor name**, not by metadata (more robust, varies
less across architectures):

```csharp
if (name.Contains("_exps."))       → MoE expert (contributes to MoeExpertsSizeBytes)
else if (name.Contains(".ffn_"))   → dense FFN
else                                → attention
```

**The order of this `if`/`else if` matters**: a genuine expert tensor
(`blk.0.ffn_gate_exps.weight`) contains both `"_exps."` and `".ffn_"`. An accidental reordering
would silently reclassify every MoE model as dense. This is locked in by
`GgufMetadataReaderTests.Read_ExpertTensorName_ClassifiedAsMoeNotDense` — this test builds a
minimal binary GGUF from scratch (the `GgufBuilder` class in the same file) rather than depending
on a real file on disk.

Tensor sizes come from `GgmlTypeTraits` (a `(blockSize, typeSizeBytes)` table per ggml type),
empirically validated at 752/752 against a real file during the original implementation — **never
guess these memory constants, always check them against `ggml-common.h`** if a new quantization
type needs to be added. An unknown type silently falls back to 0 bytes (documented, intentional
behavior, locked in by a test) rather than crashing.

A model split across multiple files (`split.count > 1` in the metadata) explicitly raises an
`InvalidDataException` rather than producing silently-wrong partial metadata — multi-file support
is not implemented (see Limitations).

---

## MoE Calculator and AI Advisor

`MoeVramCalculator.Recommend`: the dense part (attention + dense FFN + embeddings/head) is
assumed to always fit entirely in VRAM; only the number of MoE expert layers kept on the GPU is
optimized, layer by layer (actual sizes, not an average). It looks for the smallest N (prefix of
layers offloaded to CPU) such that `fixed + KV_cache + Σ(experts remaining on GPU) ≤ available VRAM
− margin`.

`MoeVramCalculator.EstimateKvCacheBytes` is **public static** and shared with
`ConfigurationAdvisor` (dense branch included) — a single formula, not two that could drift apart.
It prefers `AttentionKeyLength`/`AttentionValueLength` (read from
`{arch}.attention.key_length`/`value_length` when the GGUF provides them) over the plain division
`EmbeddingLength / AttentionHeadCount`, which is necessary for certain architectures where the
real head_dim differs from that calculation.

`ConfigurationAdvisor.Evaluate`: verdict `ComfortableFit` (the safety margin still fits a second
time in the remaining space) / `TightFit` (fits, but barely) / `RamOnlyWillBeSlow` (fits in RAM but
not in VRAM) / `DoesNotFit`. `ChooseLargestFittingContext` tests a ladder of context sizes (4k →
128k, capped by the model's training context) and returns a `(ContextSize, Verified)` pair —
`Verified=false` if even the smallest candidate tested isn't guaranteed to fit, so the caller can
flag that rather than displaying a number that looks validated when it isn't.

The "Ask the AI" button uses `POST /api/chat` with `stream: false` and a `format` JSON schema
(`AdvisorPromptBuilder.JsonSchema`) for a structured response, falling back to free-form text if
JSON parsing fails (a small local model doesn't always honor a strict schema).

---

## Engine Orchestration

"Attach rather than duplicate" philosophy: `EnsureRunningAsync`/`StartAsync` first check whether a
process of the same name is already running (name-prefix lookup via `ProcessLookup`, shared
between both managers) and whether its API responds, before launching anything. A
`SemaphoreSlim(1,1)` per manager serializes concurrent calls (double-click, a monitoring tick
during a Start/Stop). `RefreshStatusAsync` takes that same semaphore in non-blocking mode
(`WaitAsync(0, ct)`) — if a Start/Stop is in progress, it simply skips that monitoring cycle
rather than reading state that's mid-mutation.

For an external process (not launched by the app), stdout/stderr log capture isn't possible;
`OllamaProcessManager` falls back to tailing the `%LOCALAPPDATA%\Ollama\server.log` file.

`LlamaCppLaunchSettingsFactory.FromTier(tier, executablePath, huggingFaceApiToken,
totalMoeLayers)` (in `LocalIA.Core/Models/LlamaCppLaunchSettings.cs`) builds the full command
line: it resolves the model source (`-m <local path>` if downloaded, otherwise
`-hf <repo>[:quant]` + optional `-hff`/`-hft`) then delegates the rest to
`LlamaCppArgumentBuilder.Build()` — the same generator used by the "Model Configuration" preview,
not a second one generated separately.

---

## Tests

```bash
cd src
dotnet test LocalIA.Tests/LocalIA.Tests.csproj
```

69 tests covering `LocalIA.Core` (GGUF parser, MoE calculator, advisor, builders, HF file-role
classification) and `LocalIA.Infrastructure` (legacy import, end-to-end against a real temp
folder). No WPF tests (a deliberate project constraint, not a gap — the App layer is verified by
actually running the app, see below).

`GgufMetadataReaderTests.cs` contains a small binary GGUF builder (`GgufBuilder`) for testing the
parser without depending on a real file on disk — reusable for any new test touching
`GgufMetadataReader`.

`LegacyConfigImporterTests.cs` creates a temp folder with a synthetic `config.json` + Modelfile
(`Path.GetTempPath()`, cleaned up via `IDisposable`) rather than depending on the repo's real
`config/config.json`.

---

## WPF Pitfalls Already Hit (and Their Fixes)

- **`Run.Text`** has `BindsTwoWayByDefault=true` (unlike most WPF properties) — binding it to a
  computed property with no real setter (`=>`) throws an immediate `XamlParseException`. Always
  use an explicit `Mode=OneWay` for a `<Run Text="{Binding ...}">` pointed at a read-only
  property.
- **`ContentControl` never reuses the old instance** when `Content` changes (even when coming
  back to the same object) — a view that subscribes to an event on a singleton ViewModel
  (`Messages.CollectionChanged` in `ChatView`, for example) must explicitly unsubscribe on
  `Unloaded`, otherwise every navigation to that tab leaks a view instance.
- **`ModelTier` does not implement `INotifyPropertyChanged`** — a direct XAML binding on
  `Tier.Engine` won't refresh itself if `Engine` changes some other way than through that specific
  binding. Hence `RefreshForEngineChangeCommand` (triggered from the engine ComboBox's
  code-behind) and dedicated observable properties like `IsLlamaCppEngine` on
  `ModelConfigurationViewModel`, updated explicitly rather than relying on a nested binding.
- **Calling `Window.Close()` while `Closing` is being handled** (including synchronously, within
  the same invocation, even after an `await`) throws `InvalidOperationException` — WPF considers
  the window "closing" for the entire duration of the event. Re-invoking `Close()` must go through
  `Dispatcher.BeginInvoke(Close)`, never a direct call. See `MainWindow.xaml.cs.OnClosing` for the
  full pattern (plus a `_closePromptOpen` flag, to prevent stacking a second prompt if the user
  clicks Close several times in a row while the first one is already showing).
- **Converters not registered in `App.xaml`** slip through the build unnoticed and only crash at
  runtime, when the view that uses them opens — check every new `{StaticResource ...Converter}`
  against the list in `App.xaml` before considering a view finished.

**Recommended verification method for any UI-touching change**: launch the app (`dotnet run
--project LocalIA.App/LocalIA.App.csproj`) and drive it via UI Automation
(`System.Windows.Automation` from PowerShell — `AutomationElement.FromHandle`, `InvokePattern`,
`SelectionItemPattern`, etc.) rather than relying on the build alone. This method is what caught
most of the bugs listed in REVUE_CODE.md — the compiler and unit tests can't see a broken XAML
binding.

---

## Known Limitations / Ideas for Later

- **Settings screen**: `PlaceholderViewModel`, never implemented. `LlamaCppServer.ExecutablePath`
  and `Preferences.HuggingFaceApiToken` can only be changed by hand-editing `app-config.json`.
- **LoRA adapters on the Ollama side**: `/api/create` expects a top-level `adapters` field
  (dictionary of filename → SHA256 digest of a blob already uploaded via
  `/api/blobs/sha256:<digest>`), not a key under `parameters`. `AdapterSettings.LoraPath`
  deliberately has no mapped `OllamaParameter` (see the comment in
  `LocalIA.Core/Configuration/AdapterSettings.cs`) — implementing blob upload would be a genuinely
  new feature, not a fix.
- **Multi-file (sharded) GGUF**: detected and explicitly rejected (`InvalidDataException`), not
  merged. Implementing merge support would require fetching every file in the group on the HF
  download side (currently only one file per shard group is even offered in search results), in
  addition to the parser itself.
- **Custom llama.cpp host/port**: `ILlamaCppApiClient` targets a fixed address
  (`DefaultLlamaCppBaseAddress`, configured once in DI) — a tier with a custom `--host`/`--port`
  starts correctly, but the readiness check (`WaitForReadyAsync`/`RefreshStatusAsync`) keeps
  probing the default address. Fixing this properly would require making the HTTP client
  reconfigurable per call (`IHttpClientFactory` with a dynamic base address, or an explicit
  per-method parameter).
- **`SchemaVersion`** exists and is compared on load (logged if different), but no actual
  migration is implemented — only one schema version has existed so far. The day `AppConfig`'s
  shape changes, an extension point needs to be added to
  `AppConfigRepository.TryLoadFromFileAsync`.

---

<a id="francais"></a>
# LOCAL-IA Desktop Manager — Documentation développeur

Application .NET 10 / WPF (MVVM) sous `src/`, qui remplace le toolkit PowerShell historique
(`scripts/`, `config/`, resté intact) pour la gestion de moteurs d'inférence LLM locaux (Ollama,
llama.cpp/CUDA) sur une machine personnelle (RTX 3060 12 Go, 64 Go RAM).

Voir aussi : [REVUE_CODE.md](REVUE_CODE.md) (historique complet d'une revue de code — 41 constats
corrigés, avec preuves et scénarios d'échec) et le plan d'implémentation d'origine dans
`C:\Users\clefw\.claude\plans\crystalline-hopping-puddle.md`.

---

## Architecture en un coup d'œil

```
src/
├── LocalIA.Core/            net10.0, ZÉRO dépendance Windows/WPF — logique pure, testable
├── LocalIA.Infrastructure/  net10.0-windows — HTTP, Process, WMI, LibreHardwareMonitor
├── LocalIA.App/             net10.0-windows, WPF — ViewModels, Views, DI composition root
└── LocalIA.Tests/           xUnit — teste Core + Infrastructure (pas de tests WPF)
```

Direction des dépendances : `App → Infrastructure → Core`, et `App → Core` directement pour les
ViewModels qui n'ont besoin que de logique pure. `Core` ne référence jamais `Infrastructure` ni
`App`. Cette séparation est ce qui permet à `LocalIA.Tests` de tester le parseur GGUF, le
calculateur MoE, le conseiller IA et les builders d'arguments sans dépendance WPF.

**Composition root** : `LocalIA.App/App.xaml.cs`, via `Microsoft.Extensions.Hosting` (Generic
Host). `builder.Services.AddLocalIaInfrastructure()` (dans
`LocalIA.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`) et
`.AddLocalIaApp()` (dans `LocalIA.App/DependencyInjection/AppServiceCollectionExtensions.cs`).
Les ViewModels de page sont enregistrés en singleton (l'état — chat, sélection, config chargée —
doit survivre à la navigation) ; `MainWindow` est transient.

**Filet de sécurité global** : `App.xaml.cs` intercepte `DispatcherUnhandledException` (log +
popup + `e.Handled = true`) pour qu'une erreur dans une fonctionnalité secondaire ne ferme jamais
toute l'application. `OnStartup` a sa propre barrière try/catch séparée, parce qu'une exception
avant `mainWindow.Show()` laisserait sinon un processus fantôme sans fenêtre (le
`DispatcherUnhandledException` global n'intercepte que ce qui se produit APRÈS ce point).

---

## Le mécanisme central : `EngineFlagAttribute`

C'est ce qui rend la surface de ~150 paramètres Ollama/llama.cpp gérable. Chaque réglage est une
propriété `[ObservableProperty]` nullable (dans une classe de `LocalIA.Core/Configuration/`,
regroupées par thème — `SamplingSettings`, `ContextMemorySettings`, `GpuOffloadSettings`, etc.),
annotée `[property: EngineFlag(OllamaParameter: "...", LlamaCppFlag: "...", DisplayName: "...")]`.
`null` sur l'un des deux = non supporté par ce moteur.

**Pour ajouter un nouveau paramètre** : une propriété + l'attribut, dans le groupe thématique
concerné. Aucun autre fichier à toucher — les deux builders et l'UI le prennent en compte
automatiquement par réflexion :

- `LlamaCppArgumentBuilder.Build(ModelTierSettings, totalMoeLayers)` → `List<string>` d'arguments
  CLI.
- `OllamaParameterBuilder.Build(ModelTierSettings)` → `Dictionary<string, object>` pour le corps
  JSON de `/api/create`.
- `SettingsFieldDescriptor.FromGroup(...)` (dans `LocalIA.App/Configuration/`) → alimente
  génériquement l'UI (une seule `DataTemplate` pour tous les champs, pas de contrôle dédié par
  champ).

Les deux builders et `ModelConfigurationViewModel` itèrent la même liste de 8 groupes via
`ModelTierSettings.GetEngineFlagGroups()` — point d'entrée unique, pour qu'un groupe ajouté un
jour soit automatiquement pris en compte partout (voir historique dans REVUE_CODE.md, constat
H10 : ça a été une vraie source de bug par le passé, trois copies de cette même liste avaient
divergé).

`MoeOffloadSettings` et `RawOverrides` sont volontairement exclus de cette boucle générique
(constructions dédiées : génération de `--n-cpu-moe`/`--override-tensor`, et échappatoire libre).
`GpuLayerSpec` (mode Auto/All/Explicit) est aussi traité à part dans les deux builders.

---

## Modèle de configuration et persistance

```
AppConfig (SchemaVersion, Storage, OllamaServer, LlamaCppServer, Profiles, Preferences)
  └─ ModelProfile (Id, Name, Description, Tiers)
       └─ ModelTier (Id, Label, Engine, OllamaBaseModel/CustomModelName, LlamaCppSource, Settings)
            └─ ModelTierSettings (8 groupes EngineFlag + MoeOffload + Raw)
```

Persisté dans `%APPDATA%\LocalIA\app-config.json` via `AppConfigRepository`
(`LocalIA.Infrastructure/Configuration/`) — écriture atomique (`.tmp` puis `File.Move`), copie de
secours `.backup` à chaque sauvegarde, et **relue** automatiquement si le fichier principal est
illisible (JSON corrompu, verrou I/O transitoire) avant de retomber sur une config par défaut.

`config/config.json` (le toolkit legacy) n'est **jamais écrit** par l'app — `LegacyConfigImporter`
le lit en lecture seule, une fois au tout premier lancement (`AppConfigRepository.LoadAsync`
quand `app-config.json` n'existe pas encore), ou à la demande via
`IAppConfigRepository.ImportLegacyAsync()` (bouton « Importer » dans Profils, indépendant de
l'existence du fichier app — ça n'a pas toujours été le cas, voir constat M9 de la revue).

`LegacyConfigImporter.ParseModelfile` reconnaît les clés `PARAMETER` d'un Modelfile via une table
inversée construite par réflexion sur les mêmes attributs `EngineFlag` (pas une liste de clés
codée en dur) — tout paramètre déjà modélisé ailleurs dans l'app est donc automatiquement
reconnu à l'import.

**Attention en modifiant ce modèle** : `ProfilesViewModel` et `HuggingFaceSearchViewModel`
chargent/sauvegardent chacun leur propre copie d'`AppConfig` indépendamment (pas d'état partagé
centralisé). `ProfilesViewModel.RefreshFromDiskIfClean()` fusionne par `Id` les profils/paliers
apparus sur disque entre-temps plutôt que d'écraser les modifications locales non enregistrées —
si tu ajoutes un troisième point d'écriture, il faudra probablement l'intégrer à ce même
mécanisme de fusion pour éviter de perdre des données.

---

## Abstractions clés

| Interface | Implémentation | Rôle |
|---|---|---|
| `IOllamaProcessManager` / `ILlamaCppProcessManager` | `OllamaProcessManager` / `LlamaCppProcessManager` | Démarrage/arrêt, attache-plutôt-que-duplique, capture de logs |
| `IOllamaApiClient` / `ILlamaCppApiClient` | `OllamaApiClient` / `LlamaCppApiClient` | REST + streaming (NDJSON / SSE) |
| `IHardwareMonitorService` | `HardwareMonitoringService` (`BackgroundService`) | 3 boucles indépendantes (CPU/RAM 1s, GPU 1.5s, moteurs 2.5s) |
| `IGgufMetadataReader` | `GgufMetadataReader` | Parseur binaire GGUF (en-tête + tenseurs, jamais les poids) |
| `IMoeVramCalculator` | `MoeVramCalculator` | Calcule le plus petit préfixe de couches MoE à décharger sur CPU |
| `IConfigurationAdvisor` | `ConfigurationAdvisor` | Délègue à `IMoeVramCalculator` pour les modèles MoE ; estime lui-même le partage GPU/CPU pour les modèles denses |
| `IHuggingFaceClient` / `IGgufDownloader` | `HuggingFaceClient` / `GgufDownloader` | Recherche/arborescence HF, téléchargement avec reprise (`Range`) |
| `IAppConfigRepository` | `AppConfigRepository` | Persistance + import legacy |

`OllamaApiClient`/`LlamaCppApiClient` sont enregistrés avec un timeout HTTP de 5 minutes (pas
d'infini, pas 5 secondes) — assez long pour un chargement à froid de modèle avant la première
réponse, assez court pour ne jamais masquer un vrai problème indéfiniment. `/api/pull` et
`/api/create` utilisent un client HTTP **séparé**, sans timeout du tout
(`OllamaApiClient.LongRunningHttpClientName`), parce qu'un téléchargement de plusieurs Go dépasse
couramment 5 minutes et que `HttpClient.Timeout` s'applique à la requête entière, corps en
streaming compris — pas seulement aux en-têtes.

---

## Parseur GGUF — points d'attention

`GgufMetadataReader.Read` lit magic + version + compteurs, puis les paires clé/valeur de
métadonnées, puis les infos de tenseurs (nom/dims/type/offset) — jamais les poids eux-mêmes
(quelques Ko à quelques Mo lus, pas les Go du fichier).

Classification couche dense vs MoE **par nom de tenseur**, pas par métadonnée (plus robuste,
varie moins selon l'architecture) :

```csharp
if (name.Contains("_exps."))       → expert MoE (contribue à MoeExpertsSizeBytes)
else if (name.Contains(".ffn_"))   → FFN dense
else                                → attention
```

**L'ordre de ce `if`/`else if` est significatif** : un vrai tenseur d'expert
(`blk.0.ffn_gate_exps.weight`) contient à la fois `"_exps."` et `".ffn_"`. Un réordonnancement
accidentel reclasserait silencieusement tout modèle MoE en dense. Verrouillé par
`GgufMetadataReaderTests.Read_ExpertTensorName_ClassifiedAsMoeNotDense` — ce test construit un
GGUF binaire minimal de toutes pièces (classe `GgufBuilder` dans le même fichier) plutôt que de
dépendre d'un vrai fichier sur disque.

Les tailles de tenseurs viennent de `GgmlTypeTraits` (table `(blockSize, typeSizeBytes)` par type
ggml), validée empiriquement à 752/752 contre un vrai fichier lors de l'implémentation d'origine —
**ne jamais deviner ces constantes de mémoire, toujours les vérifier contre `ggml-common.h`** si
un nouveau type de quantification doit être ajouté. Un type inconnu retombe silencieusement sur 0
octet (comportement documenté et voulu, verrouillé par test) plutôt que de planter.

Un modèle découpé en plusieurs fichiers (`split.count > 1` dans les métadonnées) fait lever une
`InvalidDataException` explicite plutôt que de produire des métadonnées partielles silencieusement
fausses — le support multi-fichiers n'est pas implémenté (voir Limitations).

---

## Calculateur MoE et conseiller IA

`MoeVramCalculator.Recommend` : la partie dense (attention + FFN dense + embeddings/tête) est
supposée toujours tenir entièrement en VRAM ; seul le nombre de couches d'experts MoE côté GPU est
optimisé, couche par couche (tailles réelles, pas une moyenne). Cherche le plus petit N (préfixe
de couches déchargées sur CPU) tel que `fixe + cache_KV + Σ(experts restants sur GPU) ≤ VRAM
disponible − marge`.

`MoeVramCalculator.EstimateKvCacheBytes` est **public static** et partagé avec
`ConfigurationAdvisor` (branche dense y compris) — une seule formule, pas deux qui pourraient
diverger. Elle préfère `AttentionKeyLength`/`AttentionValueLength` (lus depuis
`{arch}.attention.key_length`/`value_length` quand le GGUF les fournit) à la simple division
`EmbeddingLength / AttentionHeadCount`, nécessaire pour certaines architectures où le head_dim
réel diffère de ce calcul.

`ConfigurationAdvisor.Evaluate` : verdict `ComfortableFit` (marge de sécurité tient encore une
deuxième fois dans la place restante) / `TightFit` (tient, mais de justesse) /
`RamOnlyWillBeSlow` (tient en RAM mais pas en VRAM) / `DoesNotFit`. `ChooseLargestFittingContext`
teste une échelle de tailles de contexte (4k → 128k, plafonnée par le contexte d'entraînement du
modèle) et renvoie un couple `(ContextSize, Verified)` — `Verified=false` si même le plus petit
candidat testé n'est pas garanti de tenir, pour que l'appelant puisse le signaler plutôt que
d'afficher un chiffre qui a l'air validé sans l'être.

Le bouton « Demander à l'IA » utilise `POST /api/chat` avec `stream: false` et un `format` JSON
schema (`AdvisorPromptBuilder.JsonSchema`) pour une réponse structurée, avec repli en texte libre
si le parsing JSON échoue (un petit modèle local ne respecte pas toujours un schéma strict).

---

## Orchestration des moteurs

Philosophie « attache plutôt que duplique » : `EnsureRunningAsync`/`StartAsync` vérifient d'abord
si un process du même nom tourne déjà (recherche par préfixe de nom via `ProcessLookup`, partagé
entre les deux gestionnaires) et si son API répond, avant de lancer quoi que ce soit. Un
`SemaphoreSlim(1,1)` par gestionnaire sérialise les appels concurrents (double-clic, tick de
monitoring pendant un Start/Stop). `RefreshStatusAsync` prend ce même sémaphore en mode
non-bloquant (`WaitAsync(0, ct)`) — si un Start/Stop est en cours, il saute simplement ce cycle de
monitoring plutôt que de lire un état en cours de mutation.

Pour un process externe (pas lancé par l'app), la capture de logs stdout/stderr n'est pas
possible ; `OllamaProcessManager` retombe sur un tail du fichier
`%LOCALAPPDATA%\Ollama\server.log`.

`LlamaCppLaunchSettingsFactory.FromTier(tier, executablePath, huggingFaceApiToken,
totalMoeLayers)` (dans `LocalIA.Core/Models/LlamaCppLaunchSettings.cs`) construit la ligne de
commande complète : résout la source du modèle (`-m <chemin local>` si téléchargé, sinon
`-hf <repo>[:quant]` + `-hff`/`-hft` optionnels) puis délègue le reste à
`LlamaCppArgumentBuilder.Build()` — le même générateur que l'aperçu de « Configuration du
modèle », pas un second généré séparément.

---

## Tests

```bash
cd src
dotnet test LocalIA.Tests/LocalIA.Tests.csproj
```

69 tests couvrant `LocalIA.Core` (parseur GGUF, calculateur MoE, conseiller, builders,
classification de rôle de fichier HF) et `LocalIA.Infrastructure` (import legacy, bout en bout
avec un vrai dossier temporaire). Aucun test WPF (contrainte du projet, pas une lacune — la
couche App est vérifiée en lançant l'app réellement, voir plus bas).

`GgufMetadataReaderTests.cs` contient un petit constructeur de GGUF binaire (`GgufBuilder`) pour
tester le parseur sans dépendre d'un fichier réel sur disque — réutilisable pour tout nouveau
test touchant `GgufMetadataReader`.

`LegacyConfigImporterTests.cs` crée un dossier temporaire avec un `config.json` + Modelfile
synthétiques (`Path.GetTempPath()`, nettoyé via `IDisposable`) plutôt que de dépendre du vrai
`config/config.json` du dépôt.

---

## Pièges WPF déjà rencontrés (et leurs correctifs)

- **`Run.Text`** a `BindsTwoWayByDefault=true` (contrairement à la plupart des propriétés WPF) —
  binder sur une propriété calculée sans setter réel (`=>`) lève une `XamlParseException`
  immédiate. Toujours `Mode=OneWay` explicite pour un `<Run Text="{Binding ...}">` vers une
  propriété en lecture seule.
- **`ContentControl` ne réutilise jamais l'ancienne instance** en changeant de `Content` (même en
  revenant au même objet) — une vue qui s'abonne à un événement d'un ViewModel singleton
  (`Messages.CollectionChanged` dans `ChatView`, par ex.) doit se désabonner explicitement sur
  `Unloaded`, sinon chaque navigation vers cet onglet fuit une instance de vue.
- **`ModelTier` n'implémente pas `INotifyPropertyChanged`** — un binding XAML direct sur
  `Tier.Engine` ne se rafraîchit pas tout seul si `Engine` change ailleurs que par ce binding
  précis. D'où l'existence de `RefreshForEngineChangeCommand` (déclenché depuis le code-behind de
  la ComboBox moteur) et de propriétés observables dédiées comme `IsLlamaCppEngine` sur
  `ModelConfigurationViewModel`, mises à jour explicitement plutôt que de compter sur un binding
  imbriqué.
- **`Window.Close()` appelé pendant le traitement de `Closing`** (y compris en synchrone, dans la
  même invocation, même après un `await`) lève `InvalidOperationException` — WPF considère la
  fenêtre « en cours de fermeture » pour toute la durée de l'événement. Rappeler `Close()` doit
  passer par `Dispatcher.BeginInvoke(Close)`, jamais un appel direct. Voir
  `MainWindow.xaml.cs.OnClosing` pour le patron complet (avec un drapeau `_closePromptOpen` en
  plus, pour empêcher d'empiler une seconde invite si l'utilisateur clique Fermer plusieurs fois
  de suite pendant que la première est déjà affichée).
- **Convertisseurs non enregistrés dans `App.xaml`** passent inaperçus à la compilation et
  plantent seulement à l'exécution, à l'ouverture de la vue qui les utilise — vérifier chaque
  nouveau `{StaticResource ...Converter}` contre la liste dans `App.xaml` avant de considérer une
  vue terminée.

**Méthode de vérification recommandée pour tout changement touchant l'UI** : lancer l'app
(`dotnet run --project LocalIA.App/LocalIA.App.csproj`) et piloter via UI Automation
(`System.Windows.Automation` depuis PowerShell — `AutomationElement.FromHandle`, `InvokePattern`,
`SelectionItemPattern`, etc.) plutôt que de se fier uniquement à la compilation. C'est cette
méthode qui a détecté la plupart des bugs listés dans REVUE_CODE.md — le compilateur et les
tests unitaires ne voient pas les bindings XAML cassés.

---

## Limitations connues / pistes pour la suite

- **Écran Paramètres** : `PlaceholderViewModel`, jamais implémenté. `LlamaCppServer.ExecutablePath`
  et `Preferences.HuggingFaceApiToken` ne sont modifiables qu'en éditant `app-config.json` à la
  main.
- **Adaptateurs LoRA côté Ollama** : `/api/create` attend un champ de premier niveau `adapters`
  (dictionnaire nom de fichier → digest SHA256 d'un blob déjà téléversé via
  `/api/blobs/sha256:<digest>`), pas une clé dans `parameters`. `AdapterSettings.LoraPath` n'a
  volontairement pas de `OllamaParameter` mappé (voir commentaire dans
  `LocalIA.Core/Configuration/AdapterSettings.cs`) — implémenter le téléversement de blob serait
  une vraie fonctionnalité nouvelle, pas un correctif.
- **GGUF multi-fichiers (sharded)** : détecté et rejeté explicitement (`InvalidDataException`),
  pas fusionné. Implémenter la fusion demanderait de récupérer tous les fichiers du groupe côté
  téléchargement HF (actuellement un seul fichier par groupe de shards est même proposé dans la
  recherche) en plus du parseur lui-même.
- **Host/port llama.cpp personnalisés** : `ILlamaCppApiClient` cible une adresse fixe
  (`DefaultLlamaCppBaseAddress`, configurée une fois en DI) — un palier avec un `--host`/`--port`
  personnalisé démarre correctement mais la vérification de disponibilité
  (`WaitForReadyAsync`/`RefreshStatusAsync`) continue de sonder l'adresse par défaut. Corriger
  proprement demanderait de rendre le client HTTP reconfigurable par appel (`IHttpClientFactory`
  avec une base address dynamique, ou un paramètre explicite par méthode).
- **`SchemaVersion`** existe et est comparé au chargement (log si différent), mais aucune
  migration réelle n'est implémentée — une seule version du schéma a existé jusqu'ici. Le jour où
  la forme d'`AppConfig` change, un point d'extension est à ajouter dans
  `AppConfigRepository.TryLoadFromFileAsync`.
