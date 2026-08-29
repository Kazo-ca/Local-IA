namespace LocalIA.Core.Configuration;

/// <summary>
/// Conversion explicite enum -> valeur textuelle attendue par llama.cpp/Ollama. Volontairement
/// explicite plutôt qu'une règle générique (minuscule + tirets) : les valeurs attendues mélangent
/// tirets ("mmap+mlock", "deepseek-legacy") et underscores ("q8_0"), une seule règle générique
/// produirait des valeurs incorrectes pour certains types.
/// </summary>
public static class EnumWireFormat
{
    public static string ToWire(Enum value) => value switch
    {
        MemoryLoadMode m => m switch
        {
            MemoryLoadMode.None => "none",
            MemoryLoadMode.Mmap => "mmap",
            MemoryLoadMode.Mlock => "mlock",
            MemoryLoadMode.MmapAndMlock => "mmap+mlock",
            MemoryLoadMode.DirectIo => "dio",
            _ => "none",
        },
        NumaPolicy n => n switch
        {
            NumaPolicy.Distribute => "distribute",
            NumaPolicy.Isolate => "isolate",
            NumaPolicy.NumaCtl => "numactl",
            _ => "distribute",
        },
        FlashAttentionMode f => f switch
        {
            FlashAttentionMode.On => "on",
            FlashAttentionMode.Off => "off",
            _ => "auto",
        },
        RopeScalingType r => r switch
        {
            RopeScalingType.None => "none",
            RopeScalingType.Linear => "linear",
            RopeScalingType.Yarn => "yarn",
            _ => "none",
        },
        SplitMode s => s switch
        {
            SplitMode.None => "none",
            SplitMode.Layer => "layer",
            SplitMode.Row => "row",
            SplitMode.Tensor => "tensor",
            _ => "layer",
        },
        CacheQuantType c => c switch
        {
            CacheQuantType.F32 => "f32",
            CacheQuantType.F16 => "f16",
            CacheQuantType.Bf16 => "bf16",
            CacheQuantType.Q8_0 => "q8_0",
            CacheQuantType.Q4_0 => "q4_0",
            CacheQuantType.Q4_1 => "q4_1",
            CacheQuantType.Iq4Nl => "iq4_nl",
            CacheQuantType.Q5_0 => "q5_0",
            CacheQuantType.Q5_1 => "q5_1",
            _ => "f16",
        },
        ReasoningFormat r => r switch
        {
            ReasoningFormat.None => "none",
            ReasoningFormat.DeepSeek => "deepseek",
            ReasoningFormat.DeepSeekLegacy => "deepseek-legacy",
            _ => "none",
        },
        SpeculativeType s => s switch
        {
            SpeculativeType.None => "none",
            SpeculativeType.DraftSimple => "draft-simple",
            SpeculativeType.DraftEagle3 => "draft-eagle3",
            SpeculativeType.DraftMtp => "draft-mtp",
            SpeculativeType.DraftDflash => "draft-dflash",
            SpeculativeType.DraftDspark => "draft-dspark",
            SpeculativeType.NgramSimple => "ngram-simple",
            SpeculativeType.NgramMod => "ngram-mod",
            _ => "none",
        },
        _ => value.ToString().ToLowerInvariant(),
    };
}
