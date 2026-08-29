using LocalIA.Core.Abstractions;
using LocalIA.Core.Models;

namespace LocalIA.App.Chat;

public sealed class ChatEngineClientResolver(IOllamaApiClient ollamaApiClient, ILlamaCppApiClient llamaCppApiClient)
{
    public IChatEngineClient Resolve(EngineKind kind) => kind switch
    {
        EngineKind.Ollama => ollamaApiClient,
        EngineKind.LlamaCpp => llamaCppApiClient,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
