using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalIA.Core.Configuration;

public sealed partial class AdapterSettings : ObservableObject
{
    // Pas d'OllamaParameter ici, volontairement : /api/create attend un champ de premier niveau
    // "adapters" (dictionnaire nom de fichier -> digest SHA256 d'un blob déjà téléversé via
    // /api/blobs/sha256:<digest>), pas une simple clé dans "parameters". Le mapper malgré tout
    // produirait une clé "adapter" ignorée silencieusement par Ollama — sans effet, sans erreur.
    // Tant que le téléversement de blob n'est pas implémenté, ce réglage reste llama.cpp uniquement
    // (déjà correct via --lora) ; côté Ollama, l'UI l'affiche comme non supporté par ce moteur.
    [property: EngineFlag(LlamaCppFlag = "--lora", DisplayName = "Adaptateur LoRA (fichier)")]
    [ObservableProperty]
    private string? loraPath;

    [property: EngineFlag(LlamaCppFlag = "--lora-scaled", DisplayName = "Poids de l'adaptateur LoRA (0-1)")]
    [ObservableProperty]
    private double? loraScale;

    [property: EngineFlag(LlamaCppFlag = "--control-vector", DisplayName = "Vecteur de contrôle (fichier)")]
    [ObservableProperty]
    private string? controlVectorPath;
}
