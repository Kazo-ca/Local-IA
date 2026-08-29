using LocalIA.Core.HuggingFace;

namespace LocalIA.Tests;

public class GgufQuantizationParserTests
{
    [Theory]
    [InlineData("Qwen2.5-Coder-32B-Instruct-Q4_K_M.gguf", "Q4_K_M")]
    [InlineData("Qwen2.5-Coder-32B-Instruct-IQ2_M.gguf", "IQ2_M")]
    [InlineData("model-Q8_0.gguf", "Q8_0")]
    [InlineData("model-F16.gguf", "F16")]
    [InlineData("model-BF16.gguf", "BF16")]
    [InlineData("DeepSeek-Coder-V2-Lite-Instruct-Q4_K_M.gguf", "Q4_K_M")]
    public void TryParse_RecognizesCommonQuantizationSuffixes(string fileName, string expected)
    {
        Assert.Equal(expected, GgufQuantizationParser.TryParse(fileName));
    }

    [Fact]
    public void TryParse_ReturnsNull_ForNonGgufFile()
    {
        Assert.Null(GgufQuantizationParser.TryParse("README.md"));
    }

    [Fact]
    public void TryParse_ReturnsNull_WhenNoRecognizablePattern()
    {
        Assert.Null(GgufQuantizationParser.TryParse("mystery-model.gguf"));
    }

    [Theory]
    [InlineData("model-Q4_K_M-00001-of-00005.gguf", "model-Q4_K_M.gguf")]
    [InlineData("model-Q4_K_M.gguf", "model-Q4_K_M.gguf")]
    public void RemoveShardSuffix_GroupsMultiShardFiles(string fileName, string expected)
    {
        Assert.Equal(expected, GgufQuantizationParser.RemoveShardSuffix(fileName));
    }
}
