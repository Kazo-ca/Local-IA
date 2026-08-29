using System.Windows.Controls;
using LocalIA.App.ViewModels;

namespace LocalIA.App.Views;

public partial class ModelConfigurationView : UserControl
{
    public ModelConfigurationView()
    {
        InitializeComponent();
    }

    private void EngineCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ModelConfigurationViewModel viewModel && viewModel.RefreshForEngineChangeCommand.CanExecute(null))
        {
            viewModel.RefreshForEngineChangeCommand.Execute(null);
        }
    }
}
