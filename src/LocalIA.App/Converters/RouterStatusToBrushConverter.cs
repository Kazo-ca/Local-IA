using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LocalIA.Core.Models;

namespace LocalIA.App.Converters;

public sealed class RouterStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value as RouterStatus?) switch
        {
            RouterStatus.Running => Brushes.MediumSeaGreen,
            RouterStatus.Starting or RouterStatus.Stopping => Brushes.Goldenrod,
            RouterStatus.Error => Brushes.IndianRed,
            _ => Brushes.Gray,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
