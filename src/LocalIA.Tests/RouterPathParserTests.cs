using LocalIA.Infrastructure.Router;

namespace LocalIA.Tests;

public class RouterPathParserTests
{
    private static readonly string[] NoKnownIds = [];

    [Fact]
    public void Parse_ExtractsModelIdAndRemainder_FromPrefixedPath()
    {
        var (modelId, remainder) = RouterPathParser.Parse("/router/mon-modele/v1/chat/completions", NoKnownIds);

        Assert.Equal("mon-modele", modelId);
        Assert.Equal("/v1/chat/completions", remainder);
    }

    [Fact]
    public void Parse_ReturnsNullModelId_AndOriginalPath_WhenNoPrefix()
    {
        var (modelId, remainder) = RouterPathParser.Parse("/v1/chat/completions", NoKnownIds);

        Assert.Null(modelId);
        Assert.Equal("/v1/chat/completions", remainder);
    }

    [Fact]
    public void Parse_HandlesSoleModelIdWithNoTrailingPath()
    {
        var (modelId, remainder) = RouterPathParser.Parse("/router/mon-modele", NoKnownIds);

        Assert.Equal("mon-modele", modelId);
        Assert.Equal("/", remainder);
    }

    [Fact]
    public void Parse_UnescapesModelId()
    {
        var (modelId, _) = RouterPathParser.Parse("/router/mon%20modele/v1/chat/completions", NoKnownIds);

        Assert.Equal("mon modele", modelId);
    }

    [Fact]
    public void Parse_MatchesKnownModelIdContainingSlashes_InsteadOfTruncatingAtFirstSegment()
    {
        string[] knownIds = ["hf.co/TheBloke/Llama-2-7B-GGUF:Q4_K_M"];

        var (modelId, remainder) = RouterPathParser.Parse(
            "/router/hf.co/TheBloke/Llama-2-7B-GGUF:Q4_K_M/v1/chat/completions", knownIds);

        Assert.Equal("hf.co/TheBloke/Llama-2-7B-GGUF:Q4_K_M", modelId);
        Assert.Equal("/v1/chat/completions", remainder);
    }

    [Fact]
    public void Parse_PrefersLongestMatchingKnownModelId()
    {
        string[] knownIds = ["mon-modele", "mon-modele/variante"];

        var (modelId, remainder) = RouterPathParser.Parse("/router/mon-modele/variante/v1/chat/completions", knownIds);

        Assert.Equal("mon-modele/variante", modelId);
        Assert.Equal("/v1/chat/completions", remainder);
    }
}
