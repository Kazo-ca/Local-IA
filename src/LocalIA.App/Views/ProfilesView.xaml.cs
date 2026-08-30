using System.Windows.Controls;
using LocalIA.App.ViewModels;

namespace LocalIA.App.Views;

public partial class ProfilesView : UserControl
{
    public ProfilesView()
    {
        InitializeComponent();
    }

    private void EngineCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ProfilesViewModel viewModel)
        {
            viewModel.RefreshForEngineChange();
        }
    }
}
