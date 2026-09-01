using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalIA.Core.Abstractions;
using LocalIA.Core.Models;
using Microsoft.Win32;

namespace LocalIA.App.ViewModels;

public sealed partial class RouterHistoryViewModel : ObservableObject
{
    private readonly IRouterRequestHistoryStore _historyStore;

    [ObservableProperty]
    private RouterRequestHistoryEntry? selectedEntry;

    public ObservableCollection<RouterRequestHistoryEntry> Entries { get; } = [];

    public RouterHistoryViewModel(IRouterRequestHistoryStore historyStore)
    {
        _historyStore = historyStore;
        _historyStore.EntryAdded += (_, entry) => RunOnUiThread(() =>
        {
            Entries.Add(entry);

            // Le magasin se tronque lui-même à MaxHistoryEntries (plusieurs requêtes peuvent se
            // terminer en même temps) — on aligne la collection affichée sur sa taille réelle
            // plutôt que de dupliquer cette logique de troncature ici.
            var actualCount = _historyStore.Snapshot().Count;
            while (Entries.Count > actualCount)
            {
                Entries.RemoveAt(0);
            }
        });

        foreach (var entry in _historyStore.Snapshot())
        {
            Entries.Add(entry);
        }
    }

    [RelayCommand]
    private void Clear()
    {
        _historyStore.Clear();
        Entries.Clear();
        SelectedEntry = null;
    }

    [RelayCommand]
    private void Copy()
    {
        if (Entries.Count == 0)
        {
            return;
        }

        var lines = Entries.Select(FormatLine);
        Clipboard.SetText(string.Join(Environment.NewLine, lines));
    }

    [RelayCommand]
    private void Save()
    {
        if (Entries.Count == 0)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Fichiers texte (*.log)|*.log|Tous les fichiers (*.*)|*.*",
            FileName = $"local-ia-router-history_{DateTime.Now:yyyyMMdd_HHmmss}.log",
        };

        if (dialog.ShowDialog() == true)
        {
            File.WriteAllLines(dialog.FileName, Entries.Select(FormatLine));
        }
    }

    private static string FormatLine(RouterRequestHistoryEntry entry)
    {
        var builder = new StringBuilder();
        builder.Append(entry.StartedAt.ToString("HH:mm:ss")).Append(" | ");
        builder.Append(entry.Method).Append(' ').Append(entry.Path).Append(" | ");
        builder.Append(entry.ResolvedModelId ?? "—").Append(" | ");
        builder.Append(entry.ResolvedEngine?.ToString() ?? "—").Append(" | ");
        builder.Append(entry.LoadOutcome?.ToString() ?? "—").Append(" | ");
        builder.Append(entry.StatusCode).Append(" | ");
        builder.Append(entry.Duration.TotalMilliseconds.ToString("0")).Append("ms");
        if (entry.ErrorMessage is { } error)
        {
            builder.Append(" | ").Append(error);
        }

        return builder.ToString();
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}
