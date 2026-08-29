using LocalIA.Core.Models;

namespace LocalIA.Core.Abstractions;

public interface IChatEngineClient
{
    IAsyncEnumerable<ChatStreamToken> StreamChatAsync(ChatRequest request, CancellationToken ct = default);
}
