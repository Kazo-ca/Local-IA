using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LocalIA.App.Converters;

public sealed class BoolToFitBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Brushes.MediumSeaGreen : Brushes.IndianRed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
