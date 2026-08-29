using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalIA.App.Chat;
using LocalIA.Core.Abstractions;
using LocalIA.Core.Models;

namespace LocalIA.App.ViewModels;

public sealed partial class ChatViewModel : ObservableObject
{
    private readonly ChatEngineClientResolver _resolver;
    private readonly IOllamaApiClient _ollamaApiClient;
    private readonly ILlamaCppApiClient _llamaCppApiClient;
    private CancellationTokenSource? _generationCts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool isGenerating;

    [ObservableProperty]
    private EngineKind selectedEngine = EngineKind.Ollama;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string? selectedModel;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string userInput = "";

    [ObservableProperty]
    private string? systemPrompt;

    [ObservableProperty]
    private string? perfStats;

    public IReadOnlyList<EngineKind> EngineOptions { get; } = [EngineKind.Ollama, EngineKind.LlamaCpp];

    public ObservableCollection<string> AvailableModels { get; } = [];

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public ChatViewModel(ChatEngineClientResolver resolver, IOllamaApiClient ollamaApiClient, ILlamaCppApiClient llamaCppApiClient)
    {
        _resolver = resolver;
        _ollamaApiClient = ollamaApiClient;
        _llamaCppApiClient = llamaCppApiClient;

        // Passe par la commande générée (pas un appel direct à la méthode) : AsyncRelayCommand
        // suit sa propre tâche et fait remonter une exception jusqu'au Dispatcher WPF (donc au
        // filet de sécurité global) au lieu de la laisser disparaître silencieusement — utile ici
        // puisqu'au tout premier lancement, avant que l'utilisateur ait démarré un moteur, cet
        // appel réseau échoue systématiquement.
        _ = RefreshAvailableModelsCommand.ExecuteAsync(null);
    }

    partial void OnSelectedEngineChanged(EngineKind value) => _ = RefreshAvailableModelsCommand.ExecuteAsync(null);

    [RelayCommand]
    private async Task RefreshAvailableModelsAsync()
    {
        var previousSelection = SelectedModel;
        AvailableModels.Clear();

        IEnumerable<string> modelNames = SelectedEngine == EngineKind.Ollama
            ? (await _ollamaApiClient.ListTagsAsync()).Select(t => t.Name)
            : (await _llamaCppApiClient.ListModelsAsync()).Select(m => m.Id);

        foreach (var name in modelNames)
        {
            AvailableModels.Add(name);
        }

        SelectedModel = AvailableModels.Contains(previousSelection ?? "") ? previousSelection : AvailableModels.FirstOrDefault();
    }

    private bool CanSend() => !IsGenerating && !string.IsNullOrWhiteSpace(UserInput) && SelectedModel is not null;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (SelectedModel is null)
        {
            return;
        }

        var userText = UserInput.Trim();
        UserInput = "";
        Messages.Add(new ChatMessageViewModel(ChatRole.User, userText));

        var history = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(SystemPrompt))
        {
            history.Add(new ChatMessage(ChatRole.System, SystemPrompt));
        }

        foreach (var message in Messages)
        {
            history.Add(new ChatMessage(message.Role, message.Content));
        }

        var assistantMessage = new ChatMessageViewModel(ChatRole.Assistant) { IsStreaming = true };
        Messages.Add(assistantMessage);

        _generationCts = new CancellationTokenSource();
        IsGenerating = true;

        var client = _resolver.Resolve(SelectedEngine);
        var request = new ChatRequest { Model = SelectedModel, Messages = history };
        var stopwatch = Stopwatch.StartNew();
        TimeSpan? firstTokenAt = null;
        var completionTokens = 0;

        try
        {
            await foreach (var token in client.StreamChatAsync(request, _generationCts.Token))
            {
                if (!string.IsNullOrEmpty(token.DeltaContent))
                {
                    firstTokenAt ??= stopwatch.Elapsed;
                    completionTokens++;
                    assistantMessage.Content += token.DeltaContent;
                }

                if (token.CompletionTokens is { } reported)
                {
                    completionTokens = reported;
                }

                if (token.IsDone)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            assistantMessage.Content += " [génération interrompue]";
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            assistantMessage.Content += $"\n[Erreur de communication avec le moteur : {ex.Message}]";
        }
        finally
        {
            stopwatch.Stop();
            assistantMessage.IsStreaming = false;
            IsGenerating = false;
            PerfStats = FormatPerfStats(stopwatch.Elapsed, firstTokenAt, completionTokens);
            _generationCts.Dispose();
            _generationCts = null;
        }
    }

    private static string? FormatPerfStats(TimeSpan total, TimeSpan? firstTokenAt, int tokenCount)
    {
        if (tokenCount == 0 || total.TotalSeconds <= 0)
        {
            return null;
        }

        var tokensPerSecond = tokenCount / total.TotalSeconds;
        var ttft = firstTokenAt?.TotalSeconds ?? 0;
        return $"{tokenCount} tokens en {total.TotalSeconds:0.0}s (~{tokensPerSecond:0.0} tok/s, premier token à {ttft:0.00}s)";
    }

    [RelayCommand]
    private void StopGeneration() => _generationCts?.Cancel();

    [RelayCommand]
    private void ClearConversation()
    {
        Messages.Clear();
        PerfStats = null;
    }
}
