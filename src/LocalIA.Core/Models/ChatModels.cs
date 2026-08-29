namespace LocalIA.Core.Models;

public enum ChatRole
{
    System,
    User,
    Assistant,
}

public static class ChatRoleExtensions
{
    public static string ToWireString(this ChatRole role) => role switch
    {
        ChatRole.System => "system",
        ChatRole.User => "user",
        ChatRole.Assistant => "assistant",
        _ => "user",
    };
}

public sealed record ChatMessage(ChatRole Role, string Content);

public sealed class ChatRequest
{
    public required string Model { get; init; }
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public double? Temperature { get; init; }
}

public sealed class ChatStreamToken
{
    public string? DeltaContent { get; init; }
    public bool IsDone { get; init; }
    public int? PromptTokens { get; init; }
    public int? CompletionTokens { get; init; }
}
