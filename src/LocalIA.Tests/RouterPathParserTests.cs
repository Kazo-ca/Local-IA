using LocalIA.Infrastructure.Router;

namespace LocalIA.Tests;

public class RouterPathParserTests
{
    [Fact]
    public void Parse_ExtractsModelIdAndRemainder_FromPrefixedPath()
    {
        var (modelId, remainder) = RouterPathParser.Parse("/router/mon-modele/v1/chat/completions");

        Assert.Equal("mon-modele", modelId);
        Assert.Equal("/v1/chat/completions", remainder);
    }

    [Fact]
    public void Parse_ReturnsNullModelId_AndOriginalPath_WhenNoPrefix()
    {
        var (modelId, remainder) = RouterPathParser.Parse("/v1/chat/completions");

        Assert.Null(modelId);
        Assert.Equal("/v1/chat/completions", remainder);
    }

    [Fact]
    public void Parse_HandlesSoleModelIdWithNoTrailingPath()
    {
        var (modelId, remainder) = RouterPathParser.Parse("/router/mon-modele");

        Assert.Equal("mon-modele", modelId);
        Assert.Equal("/", remainder);
    }

    [Fact]
    public void Parse_UnescapesModelId()
    {
        var (modelId, _) = RouterPathParser.Parse("/router/mon%20modele/v1/chat/completions");

        Assert.Equal("mon modele", modelId);
    }
}
