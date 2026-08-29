using System.Collections.ObjectModel;

namespace LocalIA.Core.Models;

public sealed class ModelProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    // ObservableCollection (pas List) : Tiers est lié directement en ItemsSource dans
    // ProfilesView.xaml. Un List<T> muté par .Add()/.Remove() ne notifie personne, et
    // l'ItemContainerGenerator du ListBox finit par désynchroniser son compte de conteneurs
    // réalisés du compte réel — WPF lève alors "ItemsControl est incohérent par rapport à la
    // source de ses éléments" au layout suivant.
    public ObservableCollection<ModelTier> Tiers { get; set; } = [];

    public override string ToString() => Name;
}
