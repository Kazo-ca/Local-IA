using System.Globalization;

namespace LocalIA.Core.Configuration;

/// <summary>
/// Placement des experts MoE entre GPU et CPU. Granularité réelle = la couche MoE entière (tous
/// les experts d'une couche sont fusionnés en un seul tenseur GGUF, impossible de cibler un
/// expert individuel). CpuLayerIndices est la seule source de vérité ; le curseur simple n'est
/// qu'un raccourci qui y écrit un préfixe contigu {0..k-1}.
/// </summary>
public sealed class MoeOffloadSettings
{
    public HashSet<int> CpuLayerIndices { get; set; } = [];

    public IReadOnlyList<string> BuildLlamaCppArgs(int totalMoeLayers)
    {
        if (CpuLayerIndices.Count == 0 || totalMoeLayers == 0)
        {
            return [];
        }

        if (CpuLayerIndices.Count >= totalMoeLayers)
        {
            return ["--cpu-moe"];
        }

        var sortedCpu = CpuLayerIndices.OrderBy(i => i).ToList();
        var isContiguousPrefix = sortedCpu[^1] == sortedCpu.Count - 1;
        if (isContiguousPrefix)
        {
            return ["--n-cpu-moe", sortedCpu.Count.ToString(CultureInfo.InvariantCulture)];
        }

        // Sélection non-contiguë : les deux côtés sont listés explicitement via des motifs
        // disjoints, plutôt que de s'appuyer sur une éventuelle priorité entre --cpu-moe et
        // --override-tensor (non documentée de façon fiable) — transparent et sans ambiguïté.
        var gpuIndices = Enumerable.Range(0, totalMoeLayers).Where(i => !CpuLayerIndices.Contains(i)).ToList();
        var cpuPattern = $"blk\\.({string.Join("|", sortedCpu)})\\.ffn_.*_exps\\.weight=CPU";
        var gpuPattern = $"blk\\.({string.Join("|", gpuIndices)})\\.ffn_.*_exps\\.weight=CUDA0";
        return ["--override-tensor", cpuPattern, "--override-tensor", gpuPattern];
    }
}
