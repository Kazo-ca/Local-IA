using System.Globalization;
using System.Windows.Data;
using LocalIA.Core.Models;

namespace LocalIA.App.Converters;

public sealed class RouterStatusToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value as RouterStatus?) switch
        {
            RouterStatus.Running => "Actif",
            RouterStatus.Starting => "Démarrage…",
            RouterStatus.Stopping => "Arrêt…",
            RouterStatus.Error => "Erreur",
            RouterStatus.Stopped => "Arrêté",
            _ => "Inconnu",
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
