using System.Windows;
using LocalIA.App.ViewModels;

namespace LocalIA.App.Views;

public partial class NewModelWizardWindow : Window
{
    public NewModelWizardWindow(NewModelWizardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private async void OnCreateClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not NewModelWizardViewModel viewModel)
        {
            return;
        }

        await viewModel.CreateCommand.ExecuteAsync(null);
        if (viewModel.CreatedSuccessfully)
        {
            DialogResult = true;
            Close();
        }
    }
}
