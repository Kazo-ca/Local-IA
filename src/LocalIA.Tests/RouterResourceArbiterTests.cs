using LocalIA.Core.Abstractions;
using LocalIA.Core.Models;
using LocalIA.Infrastructure.Router;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalIA.Tests;

public class RouterResourceArbiterTests
{
    private const long OneGb = 1024L * 1024 * 1024;

    private static ModelTier NewOllamaTier(string customModelName, string label = "défaut") => new()
    {
        Engine = EngineKind.Ollama,
        OllamaCustomModelName = customModelName,
        Label = label,
    };

    private static ModelTier NewLlamaCppTier(string localFilePath, string label) => new()
    {
        Engine = EngineKind.LlamaCpp,
        Label = label,
        LlamaCppSource = new LlamaCppModelSource { LocalFilePath = localFilePath },
    };

    private static RouterResourceArbiter BuildArbiter(
        FakeHardwareMonitor hardware,
        FakeResourceEstimator estimator,
        FakeOllamaApiClient ollamaApi,
        FakeOllamaProcessManager ollamaProcess,
        FakeLlamaCppProcessManager llamaCppProcess,
        RouterConnectionTracker tracker,
        FakeModelResolver resolver,
        AppConfig config,
        RouterConflictResolution conflictResolution = RouterConflictResolution.CancelIncoming) => new(
            estimator, hardware, tracker, resolver, ollamaProcess, ollamaApi, llamaCppProcess,
            new FakeLaunchPlanner(), new FakeConflictPrompter(conflictResolution), new FakeConfigRepository(config),
            NullLogger<RouterResourceArbiter>.Instance);

    private static AppConfig NewConfig(long vramSafetyMarginBytes = 0) => new()
    {
        Router = new RouterSettings { VramSafetyMarginBytes = vramSafetyMarginBytes },
    };

    [Fact]
    public async Task Ollama_AlreadyLoaded_ReturnsAlreadyLoaded_WithoutEstimatingOrEvicting()
    {
        var tier = NewOllamaTier("mon-modele");
        var profile = new ModelProfile { Name = "Profil" };
        profile.Tiers.Add(tier);
        var resolution = new RouterModelResolution("mon-modele", profile, tier, EngineKind.Ollama, "127.0.0.1", 11434);

        var ollamaApi = new FakeOllamaApiClient();
        ollamaApi.Running.Add(new OllamaRunningModelInfo("mon-modele", 1000, 1000, null));

        var arbiter = BuildArbiter(
            new FakeHardwareMonitor(OneGb, OneGb), // VRAM pleine — si le code vérifiait quand même, ça échouerait
            new FakeResourceEstimator(new()),
            ollamaApi,
            new FakeOllamaProcessManager(),
            new FakeLlamaCppProcessManager(),
            new RouterConnectionTracker(),
            new FakeModelResolver(new Dictionary<string, RouterModelResolution> { ["mon-modele"] = resolution }),
            NewConfig());

        var result = await arbiter.EnsureLoadedAsync(resolution);

        Assert.Equal(RouterLoadOutcome.AlreadyLoaded, result.Outcome);
    }

    [Fact]
    public async Task Ollama_FitsInFreeVram_LoadsDirectly_WithoutEviction()
    {
        var tier = NewOllamaTier("mon-modele");
        var profile = new ModelProfile { Name = "Profil" };
        profile.Tiers.Add(tier);
        var resolution = new RouterModelResolution("mon-modele", profile, tier, EngineKind.Ollama, "127.0.0.1", 11434);

        var estimator = new FakeResourceEstimator(new() { ["mon-modele"] = 2 * OneGb });
        var arbiter = BuildArbiter(
            new FakeHardwareMonitor(total: 12 * OneGb, used: 2 * OneGb), // 10 Go libres, largement assez
            estimator,
            new FakeOllamaApiClient(),
            new FakeOllamaProcessManager(),
            new FakeLlamaCppProcessManager(),
            new RouterConnectionTracker(),
            new FakeModelResolver(new Dictionary<string, RouterModelResolution> { ["mon-modele"] = resolution }),
            NewConfig());

        var result = await arbiter.EnsureLoadedAsync(resolution);

        Assert.Equal(RouterLoadOutcome.Loaded, result.Outcome);
    }

    [Fact]
    public async Task Ollama_DoesNotFit_EvictsIdleCandidate_ThenLoads()
    {
        var idleTier = NewOllamaTier("modele-idle", "idle");
        var targetTier = NewOllamaTier("modele-cible", "cible");
        var profile = new ModelProfile { Name = "Profil" };
        profile.Tiers.Add(idleTier);
        profile.Tiers.Add(targetTier);

        var idleResolution = new RouterModelResolution("modele-idle", profile, idleTier, EngineKind.Ollama, "127.0.0.1", 11434);
        var targetResolution = new RouterModelResolution("modele-cible", profile, targetTier, EngineKind.Ollama, "127.0.0.1", 11434);

        var ollamaApi = new FakeOllamaApiClient();
        ollamaApi.Running.Add(new OllamaRunningModelInfo("modele-idle", 3 * OneGb, 3 * OneGb, null));

        var estimator = new FakeResourceEstimator(new()
        {
            ["modele-idle"] = 3 * OneGb,
            ["modele-cible"] = 4 * OneGb,
        });

        var tracker = new RouterConnectionTracker();
        // Le palier idle a déjà été chargé par le routeur une fois (bail relâché) — sinon il ne
        // serait pas un candidat suivi.
        tracker.BeginLease(idleTier.Id).Dispose();

        var resolver = new FakeModelResolver(new Dictionary<string, RouterModelResolution>
        {
            ["modele-idle"] = idleResolution,
            ["modele-cible"] = targetResolution,
        });

        // Carte de 6 Go, 3 Go déjà utilisés par modele-idle : 3 Go libres, insuffisant pour
        // modele-cible (4 Go) — mais une fois modele-idle évincé, les 6 Go redeviennent libres.
        var arbiter = BuildArbiter(
            new FakeHardwareMonitor(total: 6 * OneGb, used: 3 * OneGb),
            estimator, ollamaApi, new FakeOllamaProcessManager(), new FakeLlamaCppProcessManager(),
            tracker, resolver, NewConfig());

        var result = await arbiter.EnsureLoadedAsync(targetResolution);

        Assert.Equal(RouterLoadOutcome.EvictedIdleThenLoaded, result.Outcome);
        Assert.Contains("modele-idle", ollamaApi.UnloadedModels);
    }

    [Fact]
    public async Task Ollama_DoesNotFit_NoIdleCandidate_ReturnsConflictCancelled()
    {
        var busyTier = NewOllamaTier("modele-occupe", "occupe");
        var targetTier = NewOllamaTier("modele-cible", "cible");
        var profile = new ModelProfile { Name = "Profil" };
        profile.Tiers.Add(busyTier);
        profile.Tiers.Add(targetTier);

        var busyResolution = new RouterModelResolution("modele-occupe", profile, busyTier, EngineKind.Ollama, "127.0.0.1", 11434);
        var targetResolution = new RouterModelResolution("modele-cible", profile, targetTier, EngineKind.Ollama, "127.0.0.1", 11434);

        var ollamaApi = new FakeOllamaApiClient();
        ollamaApi.Running.Add(new OllamaRunningModelInfo("modele-occupe", 3 * OneGb, 3 * OneGb, null));

        var estimator = new FakeResourceEstimator(new()
        {
            ["modele-occupe"] = 3 * OneGb,
            ["modele-cible"] = 4 * OneGb,
        });

        var tracker = new RouterConnectionTracker();
        var busyLease = tracker.BeginLease(busyTier.Id); // Jamais disposé : connexion active.

        var resolver = new FakeModelResolver(new Dictionary<string, RouterModelResolution>
        {
            ["modele-occupe"] = busyResolution,
            ["modele-cible"] = targetResolution,
        });

        var arbiter = BuildArbiter(
            new FakeHardwareMonitor(total: 6 * OneGb, used: 3 * OneGb),
            estimator, ollamaApi, new FakeOllamaProcessManager(), new FakeLlamaCppProcessManager(),
            tracker, resolver, NewConfig());

        var result = await arbiter.EnsureLoadedAsync(targetResolution);

        Assert.Equal(RouterLoadOutcome.ConflictCancelled, result.Outcome);
        Assert.DoesNotContain("modele-occupe", ollamaApi.UnloadedModels);
        busyLease.Dispose();
    }

    [Fact]
    public async Task Ollama_DoesNotFit_BusyOccupant_UserDropsExisting_EvictsAndLoads()
    {
        var busyTier = NewOllamaTier("modele-occupe", "occupe");
        var targetTier = NewOllamaTier("modele-cible", "cible");
        var profile = new ModelProfile { Name = "Profil" };
        profile.Tiers.Add(busyTier);
        profile.Tiers.Add(targetTier);

        var busyResolution = new RouterModelResolution("modele-occupe", profile, busyTier, EngineKind.Ollama, "127.0.0.1", 11434);
        var targetResolution = new RouterModelResolution("modele-cible", profile, targetTier, EngineKind.Ollama, "127.0.0.1", 11434);

        var ollamaApi = new FakeOllamaApiClient();
        ollamaApi.Running.Add(new OllamaRunningModelInfo("modele-occupe", 3 * OneGb, 3 * OneGb, null));

        var estimator = new FakeResourceEstimator(new()
        {
            ["modele-occupe"] = 3 * OneGb,
            ["modele-cible"] = 4 * OneGb,
        });

        var tracker = new RouterConnectionTracker();
        var busyLease = tracker.BeginLease(busyTier.Id); // Jamais disposé : connexion active.

        var resolver = new FakeModelResolver(new Dictionary<string, RouterModelResolution>
        {
            ["modele-occupe"] = busyResolution,
            ["modele-cible"] = targetResolution,
        });

        var arbiter = BuildArbiter(
            new FakeHardwareMonitor(total: 6 * OneGb, used: 3 * OneGb),
            estimator, ollamaApi, new FakeOllamaProcessManager(), new FakeLlamaCppProcessManager(),
            tracker, resolver, NewConfig(), RouterConflictResolution.DropExisting);

        var result = await arbiter.EnsureLoadedAsync(targetResolution);

        Assert.Equal(RouterLoadOutcome.ConflictDropped, result.Outcome);
        Assert.Contains("modele-occupe", ollamaApi.UnloadedModels);
        busyLease.Dispose();
    }

    [Fact]
    public async Task LlamaCpp_SameModelAlreadyLoaded_ReturnsAlreadyLoaded()
    {
        var tier = NewLlamaCppTier(@"E:\models\mon-modele.gguf", "défaut");
        var profile = new ModelProfile { Name = "Profil" };
        profile.Tiers.Add(tier);
        var resolution = new RouterModelResolution("mon-modele", profile, tier, EngineKind.LlamaCpp, "127.0.0.1", 8080);

        var llamaCppProcess = new FakeLlamaCppProcessManager { CurrentModelPath = @"E:\models\mon-modele.gguf", Status = EngineStatus.Running };

        var arbiter = BuildArbiter(
            new FakeHardwareMonitor(OneGb, OneGb), new FakeResourceEstimator(new()), new FakeOllamaApiClient(),
            new FakeOllamaProcessManager(), llamaCppProcess, new RouterConnectionTracker(),
            new FakeModelResolver(new Dictionary<string, RouterModelResolution> { ["mon-modele"] = resolution }),
            NewConfig());

        var result = await arbiter.EnsureLoadedAsync(resolution);

        Assert.Equal(RouterLoadOutcome.AlreadyLoaded, result.Outcome);
        Assert.False(llamaCppProcess.StopCalled);
    }

    [Fact]
    public async Task LlamaCpp_DifferentModelLoadedAndBusy_ReturnsConflictCancelled_NeverStops()
    {
        var currentTier = NewLlamaCppTier(@"E:\models\actuel.gguf", "actuel");
        var targetTier = NewLlamaCppTier(@"E:\models\cible.gguf", "cible");
        var profile = new ModelProfile { Name = "Profil" };
        profile.Tiers.Add(currentTier);
        profile.Tiers.Add(targetTier);

        var currentResolution = new RouterModelResolution("actuel", profile, currentTier, EngineKind.LlamaCpp, "127.0.0.1", 8080);
        var targetResolution = new RouterModelResolution("cible", profile, targetTier, EngineKind.LlamaCpp, "127.0.0.1", 8080);

        var llamaCppProcess = new FakeLlamaCppProcessManager { CurrentModelPath = @"E:\models\actuel.gguf", Status = EngineStatus.Running };
        var tracker = new RouterConnectionTracker();
        var busyLease = tracker.BeginLease(currentTier.Id);

        var resolver = new FakeModelResolver(new Dictionary<string, RouterModelResolution>
        {
            ["actuel"] = currentResolution,
            ["cible"] = targetResolution,
        });

        var arbiter = BuildArbiter(
            new FakeHardwareMonitor(12 * OneGb, 0), new FakeResourceEstimator(new()), new FakeOllamaApiClient(),
            new FakeOllamaProcessManager(), llamaCppProcess, tracker, resolver, NewConfig());

        var result = await arbiter.EnsureLoadedAsync(targetResolution);

        Assert.Equal(RouterLoadOutcome.ConflictCancelled, result.Outcome);
        Assert.False(llamaCppProcess.StopCalled);
        busyLease.Dispose();
    }

    [Fact]
    public async Task LlamaCpp_DifferentModelLoadedAndBusy_UserDropsExisting_StopsAndStartsNew()
    {
        var currentTier = NewLlamaCppTier(@"E:\models\actuel.gguf", "actuel");
        var targetTier = NewLlamaCppTier(@"E:\models\cible.gguf", "cible");
        var profile = new ModelProfile { Name = "Profil" };
        profile.Tiers.Add(currentTier);
        profile.Tiers.Add(targetTier);

        var currentResolution = new RouterModelResolution("actuel", profile, currentTier, EngineKind.LlamaCpp, "127.0.0.1", 8080);
        var targetResolution = new RouterModelResolution("cible", profile, targetTier, EngineKind.LlamaCpp, "127.0.0.1", 8080);

        var llamaCppProcess = new FakeLlamaCppProcessManager { CurrentModelPath = @"E:\models\actuel.gguf", Status = EngineStatus.Running };
        var tracker = new RouterConnectionTracker();
        var busyLease = tracker.BeginLease(currentTier.Id);

        var resolver = new FakeModelResolver(new Dictionary<string, RouterModelResolution>
        {
            ["actuel"] = currentResolution,
            ["cible"] = targetResolution,
        });

        var arbiter = BuildArbiter(
            new FakeHardwareMonitor(12 * OneGb, 0), new FakeResourceEstimator(new()), new FakeOllamaApiClient(),
            new FakeOllamaProcessManager(), llamaCppProcess, tracker, resolver, NewConfig(), RouterConflictResolution.DropExisting);

        // La connexion sur "actuel" reste active pendant tout l'appel — TryBeginEviction doit
        // échouer et le choix "libérer" de l'utilisateur (via la boîte de dialogue) doit malgré
        // tout arrêter le modèle en cours, sans passer par le tracker.
        var result = await arbiter.EnsureLoadedAsync(targetResolution);

        Assert.Equal(RouterLoadOutcome.EvictedIdleThenLoaded, result.Outcome);
        Assert.True(llamaCppProcess.StopCalled);
        Assert.Equal(@"E:\models\cible.gguf", llamaCppProcess.CurrentModelPath);
        busyLease.Dispose();
    }

    [Fact]
    public async Task LlamaCpp_DifferentModelLoadedAndIdle_EvictsThenStartsNew()
    {
        var currentTier = NewLlamaCppTier(@"E:\models\actuel.gguf", "actuel");
        var targetTier = NewLlamaCppTier(@"E:\models\cible.gguf", "cible");
        var profile = new ModelProfile { Name = "Profil" };
        profile.Tiers.Add(currentTier);
        profile.Tiers.Add(targetTier);

        var currentResolution = new RouterModelResolution("actuel", profile, currentTier, EngineKind.LlamaCpp, "127.0.0.1", 8080);
        var targetResolution = new RouterModelResolution("cible", profile, targetTier, EngineKind.LlamaCpp, "127.0.0.1", 8080);

        var llamaCppProcess = new FakeLlamaCppProcessManager { CurrentModelPath = @"E:\models\actuel.gguf", Status = EngineStatus.Running };
        var tracker = new RouterConnectionTracker();
        tracker.BeginLease(currentTier.Id).Dispose(); // Suivi par le routeur, mais inactif.

        var resolver = new FakeModelResolver(new Dictionary<string, RouterModelResolution>
        {
            ["actuel"] = currentResolution,
            ["cible"] = targetResolution,
        });

        var arbiter = BuildArbiter(
            new FakeHardwareMonitor(12 * OneGb, 0), new FakeResourceEstimator(new()), new FakeOllamaApiClient(),
            new FakeOllamaProcessManager(), llamaCppProcess, tracker, resolver, NewConfig());

        var result = await arbiter.EnsureLoadedAsync(targetResolution);

        Assert.Equal(RouterLoadOutcome.EvictedIdleThenLoaded, result.Outcome);
        Assert.True(llamaCppProcess.StopCalled);
        Assert.Equal(@"E:\models\cible.gguf", llamaCppProcess.CurrentModelPath);
    }

    [Fact]
    public async Task LlamaCpp_NeverLoadedByRouter_TreatedAsProtected_ReturnsConflictCancelled()
    {
        // Un modèle chargé manuellement (Dashboard/Chat) n'a aucune entrée dans le tracker — le
        // routeur ne doit jamais l'évincer silencieusement, même s'il est en réalité inactif.
        var currentTier = NewLlamaCppTier(@"E:\models\charge-manuellement.gguf", "manuel");
        var targetTier = NewLlamaCppTier(@"E:\models\cible.gguf", "cible");
        var profile = new ModelProfile { Name = "Profil" };
        profile.Tiers.Add(currentTier);
        profile.Tiers.Add(targetTier);

        var targetResolution = new RouterModelResolution("cible", profile, targetTier, EngineKind.LlamaCpp, "127.0.0.1", 8080);
        var currentResolution = new RouterModelResolution("manuel", profile, currentTier, EngineKind.LlamaCpp, "127.0.0.1", 8080);

        var llamaCppProcess = new FakeLlamaCppProcessManager { CurrentModelPath = @"E:\models\charge-manuellement.gguf", Status = EngineStatus.Running };

        var resolver = new FakeModelResolver(new Dictionary<string, RouterModelResolution>
        {
            ["manuel"] = currentResolution,
            ["cible"] = targetResolution,
        });

        var arbiter = BuildArbiter(
            new FakeHardwareMonitor(12 * OneGb, 0), new FakeResourceEstimator(new()), new FakeOllamaApiClient(),
            new FakeOllamaProcessManager(), llamaCppProcess, new RouterConnectionTracker(), resolver, NewConfig());

        var result = await arbiter.EnsureLoadedAsync(targetResolution);

        Assert.Equal(RouterLoadOutcome.ConflictCancelled, result.Outcome);
        Assert.False(llamaCppProcess.StopCalled);
    }

    private sealed class FakeHardwareMonitor(long total, long used) : IHardwareMonitorService
    {
        public HardwareSnapshot Current { get; } = new() { TotalVramBytes = total, UsedVramBytes = used };
    }

    private sealed class FakeResourceEstimator(Dictionary<string, long> footprintsByModelId) : IRouterResourceEstimator
    {
        public Task<long?> EstimateVramBytesAsync(ModelTier tier, CancellationToken ct = default)
        {
            var id = LocalIA.Core.Models.ModelIdentifier.GetId(tier);
            return Task.FromResult(id is not null && footprintsByModelId.TryGetValue(id, out var value) ? (long?)value : null);
        }
    }

    private sealed class FakeConfigRepository(AppConfig config) : IAppConfigRepository
    {
        public Task<AppConfig> LoadAsync(CancellationToken ct = default) => Task.FromResult(config);
        public Task SaveAsync(AppConfig configToSave, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ModelProfile>> ImportLegacyAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ModelProfile>>([]);
    }

    private sealed class FakeModelResolver(IReadOnlyDictionary<string, RouterModelResolution> index) : IRouterModelResolver
    {
        public Task<RouterModelResolution?> ResolveAsync(string modelId, CancellationToken ct = default) =>
            Task.FromResult(index.TryGetValue(modelId, out var resolution) ? resolution : null);

        public Task<IReadOnlyDictionary<string, RouterModelResolution>> GetIndexAsync(CancellationToken ct = default) =>
            Task.FromResult(index);
    }

    private sealed class FakeOllamaApiClient : IOllamaApiClient
    {
        public List<OllamaRunningModelInfo> Running { get; } = [];
        public List<OllamaTagInfo> Tags { get; } = [];
        public List<string> UnloadedModels { get; } = [];

        public Task<bool> IsReachableAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<IReadOnlyList<OllamaTagInfo>> ListTagsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OllamaTagInfo>>(Tags);

        public Task<IReadOnlyList<OllamaRunningModelInfo>> ListRunningModelsAsync(CancellationToken ct = default, string? hostOverride = null) =>
            Task.FromResult<IReadOnlyList<OllamaRunningModelInfo>>(Running);

        public Task<bool> UnloadModelAsync(string modelName, CancellationToken ct = default, string? hostOverride = null)
        {
            UnloadedModels.Add(modelName);
            Running.RemoveAll(m => m.Name == modelName);
            return Task.FromResult(true);
        }

        public Task<bool> CopyModelAsync(string sourceModel, string destinationModel, CancellationToken ct = default) => Task.FromResult(true);

        public IAsyncEnumerable<OllamaOperationProgress> PullModelStreamAsync(string modelName, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<OllamaOperationProgress> CreateModelStreamAsync(OllamaCreateRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<string> ChatOnceAsync(ChatRequest request, string? jsonSchema = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<ChatStreamToken> StreamChatAsync(ChatRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeOllamaProcessManager : IOllamaProcessManager
    {
        public bool EnsureRunningResult { get; set; } = true;

        public EngineKind Kind => EngineKind.Ollama;
        public EngineStatus Status => EngineStatus.Running;
        public int? ProcessId => null;
        public bool IsOwnedProcess => false;

        public event EventHandler<EngineStatusChangedEventArgs>? StatusChanged;
        public event EventHandler<EngineLogLineEventArgs>? LogLineReceived;
        public event EventHandler<int>? ProcessExited;

        public Task RefreshStatusAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> StopAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> EnsureRunningAsync(OllamaStartOptions options, CancellationToken ct = default) => Task.FromResult(EnsureRunningResult);
    }

    private sealed class FakeLlamaCppProcessManager : ILlamaCppProcessManager
    {
        public string? CurrentModelPath { get; set; }
        public bool StartResult { get; set; } = true;
        public bool StopCalled { get; private set; }

        public EngineKind Kind => EngineKind.LlamaCpp;
        public EngineStatus Status { get; set; } = EngineStatus.Stopped;
        public int? ProcessId => null;
        public bool IsOwnedProcess => false;

        public event EventHandler<EngineStatusChangedEventArgs>? StatusChanged;
        public event EventHandler<EngineLogLineEventArgs>? LogLineReceived;
        public event EventHandler<int>? ProcessExited;

        public Task RefreshStatusAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> StartAsync(LlamaCppLaunchSettings settings, CancellationToken ct = default)
        {
            CurrentModelPath = settings.ModelPath;
            Status = EngineStatus.Running;
            return Task.FromResult(StartResult);
        }

        public Task<bool> StopAsync(CancellationToken ct = default)
        {
            StopCalled = true;
            CurrentModelPath = null;
            Status = EngineStatus.Stopped;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeConflictPrompter(RouterConflictResolution resolution) : IRouterConflictPrompter
    {
        public List<RouterConflictContext> Prompts { get; } = [];

        public Task<RouterConflictResolution> PromptAsync(RouterConflictContext context, CancellationToken ct = default)
        {
            Prompts.Add(context);
            return Task.FromResult(resolution);
        }
    }

    private sealed class FakeLaunchPlanner : ILlamaCppLaunchPlanner
    {
        public Task<LlamaCppLaunchSettings> BuildAsync(ModelTier tier, AppConfig config, CancellationToken ct = default)
        {
            var label = LlamaCppLaunchSettingsFactory.ComputeModelLabel(tier)
                ?? throw new InvalidOperationException($"Le palier « {tier.Label} » n'a aucune source configurée.");
            return Task.FromResult(new LlamaCppLaunchSettings { ExecutablePath = "llama-server.exe", ModelPath = label, Arguments = [] });
        }
    }
}
