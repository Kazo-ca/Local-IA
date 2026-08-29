namespace LocalIA.Core.Models;

public sealed class AppConfig
{
    /// <summary>Version actuelle produite par cette build de l'app — comparée au chargement pour
    /// repérer un fichier plus récent/plus ancien qu'attendu (voir AppConfigRepository.LoadAsync).</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public StorageSettings Storage { get; set; } = new();
    public OllamaServerSettings OllamaServer { get; set; } = new();
    public LlamaCppServerSettings LlamaCppServer { get; set; } = new();
    public List<ModelProfile> Profiles { get; set; } = [];
    public AppPreferences Preferences { get; set; } = new();
}

public sealed class StorageSettings
{
    public string OllamaModelsPath { get; set; } = @"E:\ollama_models";
    public string LlamaModelsPath { get; set; } = @"E:\llama_models";
}

public sealed class OllamaServerSettings
{
    public string Host { get; set; } = "127.0.0.1:11434";
    public string KeepAlive { get; set; } = "60m";
    public int NumParallel { get; set; } = 2;
}

public sealed class LlamaCppServerSettings
{
    public string ExecutablePath { get; set; } = "";
    public string DefaultHost { get; set; } = "127.0.0.1";
    public int DefaultPort { get; set; } = 8080;
}

public sealed class AppPreferences
{
    public bool StopEnginesOnExit { get; set; }
    public string? HuggingFaceApiToken { get; set; }
}
