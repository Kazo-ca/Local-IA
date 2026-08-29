using LocalIA.Core.HuggingFace;

namespace LocalIA.Tests;

public class GgufFileRoleClassifierTests
{
    [Theory]
    [InlineData("mmproj-Qwen_Qwen3.6-35B-A3B-bf16.gguf", GgufFileRole.MultimodalProjector)]
    [InlineData("mtp-Qwen_Qwen3.6-35B-A3B-Q4_0.gguf", GgufFileRole.DraftModel)]
    [InlineData("Qwen2.5-Coder-32B-Instruct-Q4_K_M.gguf", GgufFileRole.MainWeights)]
    [InlineData("draft-model-q4.gguf", GgufFileRole.DraftModel)]
    [InlineData("Qwen3-0.6B-A3B-mtp.gguf", GgufFileRole.DraftModel)]
    [InlineData("modele-Q4_K_M-draft.gguf", GgufFileRole.DraftModel)]
    public void Classify_DetectsRoleFromFileName(string fileName, GgufFileRole expected)
    {
        Assert.Equal(expected, GgufFileRoleClassifier.Classify(fileName));
    }
}
