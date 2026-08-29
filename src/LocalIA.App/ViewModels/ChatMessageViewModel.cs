using CommunityToolkit.Mvvm.ComponentModel;
using LocalIA.Core.Models;

namespace LocalIA.App.ViewModels;

public sealed partial class ChatMessageViewModel(ChatRole role, string content = "") : ObservableObject
{
    public ChatRole Role { get; } = role;

    [ObservableProperty]
    private string content = content;

    [ObservableProperty]
    private bool isStreaming;
}
