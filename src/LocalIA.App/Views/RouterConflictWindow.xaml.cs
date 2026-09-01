using System.Windows;
using LocalIA.Core.Abstractions;

namespace LocalIA.App.Views;

public partial class RouterConflictWindow : Window
{
    public RouterConflictResolution Resolution { get; private set; } = RouterConflictResolution.CancelIncoming;

    public RouterConflictWindow(RouterConflictContext context)
    {
        InitializeComponent();
        IncomingText.Text = $"Le modèle « {context.IncomingModelId} » ({context.IncomingTierLabel}) a besoin de ressources actuellement utilisées par :";
        BlockingText.Text = string.Join(", ", context.BlockingModelLabels);
    }

    private void DropExisting_Click(object sender, RoutedEventArgs e)
    {
        Resolution = RouterConflictResolution.DropExisting;
        Close();
    }

    private void CancelIncoming_Click(object sender, RoutedEventArgs e)
    {
        Resolution = RouterConflictResolution.CancelIncoming;
        Close();
    }
}
