using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Input;
using LocalIA.App.ViewModels;

namespace LocalIA.App.Views;

public partial class ChatView : UserControl
{
    public ChatView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        // ChatViewModel est un singleton et le ContentControl de la coquille recrée une nouvelle
        // instance de cette vue à chaque navigation vers l'onglet Chat (jamais réutilisée) :
        // sans ce désabonnement explicite au déchargement, chaque ancienne instance resterait
        // référencée indéfiniment par Messages.CollectionChanged.
        Unloaded += (_, _) =>
        {
            if (DataContext is ChatViewModel viewModel)
            {
                viewModel.Messages.CollectionChanged -= OnMessagesChanged;
            }
        };
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ChatViewModel oldViewModel)
        {
            oldViewModel.Messages.CollectionChanged -= OnMessagesChanged;
        }

        if (e.NewValue is ChatViewModel newViewModel)
        {
            newViewModel.Messages.CollectionChanged += OnMessagesChanged;
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.BeginInvoke(() => MessagesScrollViewer.ScrollToEnd());

    private void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            return;
        }

        e.Handled = true;
        if (DataContext is ChatViewModel viewModel && viewModel.SendCommand.CanExecute(null))
        {
            viewModel.SendCommand.Execute(null);
        }
    }
}
