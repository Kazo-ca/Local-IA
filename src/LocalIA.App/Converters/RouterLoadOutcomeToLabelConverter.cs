using System.Globalization;
using System.Windows.Data;
using LocalIA.Core.Models;

namespace LocalIA.App.Converters;

public sealed class RouterLoadOutcomeToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value as RouterLoadOutcome?) switch
        {
            RouterLoadOutcome.AlreadyLoaded => "Déjà chargé",
            RouterLoadOutcome.Loaded => "Chargé",
            RouterLoadOutcome.EvictedIdleThenLoaded => "Modèle inactif déchargé, puis chargé",
            RouterLoadOutcome.ConflictCancelled => "Conflit — requête annulée",
            RouterLoadOutcome.ConflictDropped => "Conflit — modèle précédent libéré",
            RouterLoadOutcome.Failed => "Échec",
            _ => "—",
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
