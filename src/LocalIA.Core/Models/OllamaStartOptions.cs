namespace LocalIA.Core.Models;

public sealed class OllamaStartOptions
{
    public required string ModelsPath { get; init; }
    public string Host { get; init; } = "127.0.0.1:11434";
    public string KeepAlive { get; init; } = "60m";
    public int NumParallel { get; init; } = 2;
}
