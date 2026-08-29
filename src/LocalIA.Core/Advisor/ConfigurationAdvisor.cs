using LocalIA.Core.Configuration;
using LocalIA.Core.Gguf;
using LocalIA.Core.Models;
using LocalIA.Core.MoeOffload;

namespace LocalIA.Core.Advisor;

public enum FitVerdict
{
    ComfortableFit,
    TightFit,
    RamOnlyWillBeSlow,
    DoesNotFit,
}

public sealed class CandidateModelFacts
{
    public required string DisplayName { get; init; }
    public long FileSizeBytes { get; init; }
    public string? QuantizationLabel { get; init; }
    public GgufModelMetadata? GgufMetadata { get; init; }
    public bool HasMultimodalProjector { get; init; }
    public bool HasDraftModel { get; init; }
    public CacheQuantType CacheTypeK { get; init; } = CacheQuantType.F16;
    public CacheQuantType CacheTypeV { get; init; } = CacheQuantType.F16;

    public bool IsMoe => GgufMetadata?.IsMoe ?? false;
}

public sealed class AdvisorRecommendation
{
    public required FitVerdict Verdict { get; init; }
    public int? RecommendedGpuLayers { get; init; }
    public MoeVramRecommendation? MoeRecommendation { get; init; }
    public required int RecommendedContextSize { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = [];
    public required string Summary { get; init; }
}

public interface IConfigurationAdvisor
{
    AdvisorRecommendation Evaluate(HardwareSnapshot hardware, CandidateModelFacts model, int desiredContextSize);
}

/// <summary>
/// Calculateur déterministe, instantané, sans appel réseau : délègue à
/// <see cref="IMoeVramCalculator"/> pour les modèles MoE (aucune logique dupliquée), sinon estime
/// un partage GPU/CPU par couche pour les modèles denses selon le même principe de seuil VRAM.
/// </summary>
public sealed class ConfigurationAdvisor(IMoeVramCalculator moeCalculator) : IConfigurationAdvisor
{
    private static readonly int[] ContextCandidates = [4096, 8192, 16384, 32768, 65536, 131072];
    private const long DefaultSafetyMarginBytes = 1024L * 1024 * 1024;

    /// <summary>
    /// RAM considérée indisponible pour l'IA (système d'exploitation + applications de fond),
    /// quelle que soit la RAM réellement libre à l'instant T — un modèle ne doit pas être jugé
    /// "tient en RAM" ou non sur la base d'une valeur d'utilisation live qui fluctue (cache disque
    /// Windows, etc.). Partagée avec <see cref="App.ViewModels.ConfigurationAdvisorViewModel"/>
    /// pour que le résumé envoyé à l'IA ("Demander à l'IA") reste cohérent avec ce calcul.
    /// </summary>
    public const long SystemRamReserveBytes = 12L * 1024 * 1024 * 1024;

    public AdvisorRecommendation Evaluate(HardwareSnapshot hardware, CandidateModelFacts model, int desiredContextSize)
    {
        var availableVramBytes = Math.Max(0, hardware.TotalVramBytes - hardware.UsedVramBytes);
        var availableRamBytes = Math.Max(0, hardware.TotalRamBytes - SystemRamReserveBytes);
        var notes = new List<string>();

        if (model.HasMultimodalProjector)
        {
            notes.Add("Ce dépôt propose un projecteur multimodal (vision) — llama.cpp peut l'activer automatiquement.");
        }

        if (model.HasDraftModel)
        {
            notes.Add("Ce dépôt propose un modèle brouillon pour le décodage spéculatif (MTP), qui peut accélérer la génération sans perte de qualité.");
        }

        if (model.GgufMetadata is not { } metadata || metadata.BlockCount <= 0)
        {
            double Go(long bytes) => bytes / 1024.0 / 1024.0 / 1024.0;
            return new AdvisorRecommendation
            {
                Verdict = model.FileSizeBytes <= availableRamBytes ? FitVerdict.RamOnlyWillBeSlow : FitVerdict.DoesNotFit,
                RecommendedContextSize = desiredContextSize,
                Notes = notes,
                Summary = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Métadonnées du modèle indisponibles ou inexploitables (architecture non reconnue) — impossible d'estimer précisément le partage GPU/CPU. Taille du fichier ({0:0.00} Go) comparée à la RAM disponible pour l'IA ({1:0.00} Go sur {2:0.00} Go, {3:0.00} Go réservés au système).",
                    Go(model.FileSizeBytes), Go(availableRamBytes), Go(hardware.TotalRamBytes), Go(SystemRamReserveBytes)),
            };
        }

        if (metadata.IsMoe)
        {
            var (contextSize, contextVerified) = ChooseLargestFittingContext(hardware, availableVramBytes, metadata, desiredContextSize, dense: false, model.CacheTypeK, model.CacheTypeV);
            var moeRecommendation = moeCalculator.Recommend(new MoeVramCalculatorInput
            {
                AvailableVramBytes = availableVramBytes,
                SafetyMarginBytes = DefaultSafetyMarginBytes,
                Model = metadata,
                ContextSize = contextSize,
                CacheTypeK = model.CacheTypeK,
                CacheTypeV = model.CacheTypeV,
            });

            // EstimatedHeadroomBytes est mesuré avant déduction de la marge de sécurité (contrairement
            // au calcul dense ci-dessous, où `budget` l'a déjà soustraite) : on la retranche ici pour
            // que DetermineVerdict compare des grandeurs homogènes entre les deux branches.
            var moeHeadroomBeyondMargin = moeRecommendation.EstimatedHeadroomBytes - DefaultSafetyMarginBytes;
            var verdict = DetermineVerdict(moeRecommendation.FitsInBudget, moeHeadroomBeyondMargin, model.FileSizeBytes, availableRamBytes);
            var summary = contextVerified
                ? moeRecommendation.Explanation
                : $"Même le plus petit contexte testé ({contextSize} tokens) n'est pas garanti de tenir. {moeRecommendation.Explanation}";
            return new AdvisorRecommendation
            {
                Verdict = verdict,
                MoeRecommendation = moeRecommendation,
                RecommendedContextSize = contextSize,
                Notes = notes,
                Summary = summary,
            };
        }

        // Modèle dense : même principe de seuil VRAM, mais sur la totalité des couches plutôt que
        // sur les seuls experts MoE.
        var (denseContextSize, denseContextVerified) = ChooseLargestFittingContext(hardware, availableVramBytes, metadata, desiredContextSize, dense: true, model.CacheTypeK, model.CacheTypeV);
        // NonLayerTensorsSizeBytes est déjà déduit une fois dans `budget` ci-dessous : ne pas
        // aussi l'inclure dans la moyenne par couche, sous peine de le compter deux fois.
        var bytesPerLayer = metadata.Layers.Sum(l => l.TotalSizeBytes) / metadata.BlockCount;
        var kvCacheBytes = MoeVramCalculator.EstimateKvCacheBytes(metadata, denseContextSize, model.CacheTypeK, model.CacheTypeV);
        var budget = availableVramBytes - DefaultSafetyMarginBytes - metadata.NonLayerTensorsSizeBytes - kvCacheBytes;
        var gpuLayers = bytesPerLayer > 0 ? (int)Math.Clamp(budget / bytesPerLayer, 0, metadata.BlockCount) : 0;
        var fits = gpuLayers >= metadata.BlockCount;
        var headroomBytes = budget - gpuLayers * bytesPerLayer;

        var contextCaveat = denseContextVerified ? "" : $" (même {denseContextSize} tokens n'est pas garanti de tenir)";
        return new AdvisorRecommendation
        {
            Verdict = DetermineVerdict(fits, headroomBytes, model.FileSizeBytes, availableRamBytes),
            RecommendedGpuLayers = gpuLayers,
            RecommendedContextSize = denseContextSize,
            Notes = notes,
            Summary = fits
                ? $"Le modèle dense tient entièrement en VRAM ({metadata.BlockCount} couches) avec un contexte de {denseContextSize} tokens{contextCaveat}."
                : $"Seules {gpuLayers}/{metadata.BlockCount} couches tiennent en VRAM avec un contexte de {denseContextSize} tokens{contextCaveat} ; le reste s'exécutera sur CPU (plus lent).",
        };
    }

    private (int ContextSize, bool Verified) ChooseLargestFittingContext(HardwareSnapshot hardware, long availableVramBytes, GgufModelMetadata metadata, int desiredContextSize, bool dense, CacheQuantType cacheTypeK, CacheQuantType cacheTypeV)
    {
        var maxTrainedContext = metadata.ContextLengthTrained > 0 ? metadata.ContextLengthTrained : int.MaxValue;
        var candidates = ContextCandidates.Where(c => c <= maxTrainedContext)
            .Append(Math.Min(desiredContextSize, maxTrainedContext))
            .Distinct().OrderBy(c => c).ToList();

        var best = candidates.FirstOrDefault();
        var verified = false;
        foreach (var candidate in candidates)
        {
            var kvCacheBytes = MoeVramCalculator.EstimateKvCacheBytes(metadata, candidate, cacheTypeK, cacheTypeV);
            var fixedBytes = dense
                ? metadata.NonLayerTensorsSizeBytes + metadata.Layers.Sum(l => l.TotalSizeBytes)
                : metadata.NonLayerTensorsSizeBytes + metadata.Layers.Sum(l => l.AttentionSizeBytes + l.DenseFfnSizeBytes);

            if (fixedBytes + kvCacheBytes <= availableVramBytes - DefaultSafetyMarginBytes)
            {
                best = candidate;
                verified = true;
            }
        }

        // Si même le plus petit candidat ne tient pas, `best` reste ce plus petit candidat mais
        // `verified` reste false : l'appelant sait qu'aucune taille de contexte n'a été confirmée.
        return (best, verified);
    }

    private static FitVerdict DetermineVerdict(bool fitsInVram, long headroomBytes, long fileSizeBytes, long availableRamBytes)
    {
        if (fitsInVram)
        {
            // "Confortable" si la marge de sécurité elle-même tient encore une deuxième fois dans
            // la place restante, "Serré" sinon — matérialise la différence entre "ça passe tout
            // juste" et "il reste vraiment de la place".
            return headroomBytes >= DefaultSafetyMarginBytes ? FitVerdict.ComfortableFit : FitVerdict.TightFit;
        }

        return fileSizeBytes < availableRamBytes ? FitVerdict.RamOnlyWillBeSlow : FitVerdict.DoesNotFit;
    }
}
