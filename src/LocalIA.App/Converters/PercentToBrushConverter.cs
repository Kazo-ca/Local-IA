using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LocalIA.App.Converters;

public sealed class PercentToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = value switch
        {
            double d => d,
            int i => i,
            long l => l,
            _ => 0.0,
        };

        return percent switch
        {
            > 85 => Brushes.IndianRed,
            > 60 => Brushes.Goldenrod,
            _ => Brushes.MediumSeaGreen,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
