namespace LocalIA.Core.Configuration;

public enum GpuLayerMode
{
    Auto,
    All,
    Explicit,
}

public sealed class GpuLayerSpec
{
    public GpuLayerMode Mode { get; set; } = GpuLayerMode.Auto;
    public int? ExplicitCount { get; set; }
}

public enum MemoryLoadMode
{
    None,
    Mmap,
    Mlock,
    MmapAndMlock,
    DirectIo,
}

public enum NumaPolicy
{
    Distribute,
    Isolate,
    NumaCtl,
}

public enum FlashAttentionMode
{
    Auto,
    On,
    Off,
}

public enum RopeScalingType
{
    None,
    Linear,
    Yarn,
}

public enum SplitMode
{
    None,
    Layer,
    Row,
    Tensor,
}

public enum CacheQuantType
{
    F32,
    F16,
    Bf16,
    Q8_0,
    Q4_0,
    Q4_1,
    Iq4Nl,
    Q5_0,
    Q5_1,
}

public enum PoolingType
{
    None,
    Mean,
    Cls,
    Last,
    Rank,
}

public enum ReasoningMode
{
    Auto,
    On,
    Off,
}

public enum ReasoningFormat
{
    None,
    DeepSeek,
    DeepSeekLegacy,
}

public enum SpeculativeType
{
    None,
    DraftSimple,
    DraftEagle3,
    DraftMtp,
    DraftDflash,
    DraftDspark,
    NgramSimple,
    NgramMod,
}
