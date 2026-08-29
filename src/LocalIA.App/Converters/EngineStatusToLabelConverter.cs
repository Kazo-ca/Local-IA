using System.Globalization;
using System.Windows.Data;
using LocalIA.Core.Models;

namespace LocalIA.App.Converters;

public sealed class EngineStatusToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value as EngineStatus?) switch
        {
            EngineStatus.Running => "Actif",
            EngineStatus.Starting => "Démarrage…",
            EngineStatus.Stopping => "Arrêt…",
            EngineStatus.Crashed => "Planté",
            EngineStatus.Stopped => "Arrêté",
            _ => "Inconnu",
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
