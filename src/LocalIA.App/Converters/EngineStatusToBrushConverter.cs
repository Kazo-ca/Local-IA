using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LocalIA.Core.Models;

namespace LocalIA.App.Converters;

public sealed class EngineStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value as EngineStatus?) switch
        {
            EngineStatus.Running => Brushes.MediumSeaGreen,
            EngineStatus.Starting or EngineStatus.Stopping => Brushes.Goldenrod,
            EngineStatus.Crashed => Brushes.IndianRed,
            EngineStatus.NotInstalled => Brushes.DarkOrange,
            _ => Brushes.Gray,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
