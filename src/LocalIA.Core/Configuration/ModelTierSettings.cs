namespace LocalIA.Core.Configuration;

// Regroupe l'ensemble de la surface de paramètres Ollama/llama.cpp par thème (un thème = un onglet
// dans l'UI). Ajouter un nouveau flag = une propriété + [EngineFlag] dans le groupe concerné ; les
// builders et l'UI le prennent en charge automatiquement par réflexion.
public sealed class ModelTierSettings
{
    public SamplingSettings Sampling { get; set; } = new();
    public ContextMemorySettings ContextMemory { get; set; } = new();
    public GpuOffloadSettings GpuOffload { get; set; } = new();
    public MoeOffloadSettings MoeOffload { get; set; } = new();
    public ServerNetworkingSettings ServerNetworking { get; set; } = new();
    public MultimodalSettings Multimodal { get; set; } = new();
    public SpeculativeDecodingSettings SpeculativeDecoding { get; set; } = new();
    public ReasoningSettings Reasoning { get; set; } = new();
    public AdapterSettings Adapters { get; set; } = new();
    public RawOverrides Raw { get; set; } = new();

    /// <summary>
    /// Les groupes parcourus par réflexion pour générer les flags CLI/paramètres Ollama et pour
    /// peupler l'UI de configuration — MoeOffload et Raw en sont délibérément exclus car traités à
    /// part (constructions dédiées, pas de simple mapping [EngineFlag] par propriété). Point
    /// d'entrée UNIQUE : un nouveau groupe ajouté ici est automatiquement pris en compte par les
    /// deux builders ET par l'UI, sans risque qu'un des trois soit mis à jour et pas les autres.
    /// </summary>
    public IEnumerable<object> GetEngineFlagGroups() =>
    [
        Sampling,
        ContextMemory,
        GpuOffload,
        ServerNetworking,
        Multimodal,
        SpeculativeDecoding,
        Reasoning,
        Adapters,
    ];
}
