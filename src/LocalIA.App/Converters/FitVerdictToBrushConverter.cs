using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LocalIA.Core.Advisor;

namespace LocalIA.App.Converters;

public sealed class FitVerdictToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value as FitVerdict?) switch
        {
            FitVerdict.ComfortableFit => Brushes.MediumSeaGreen,
            FitVerdict.TightFit => Brushes.Goldenrod,
            FitVerdict.RamOnlyWillBeSlow => Brushes.DarkOrange,
            FitVerdict.DoesNotFit => Brushes.IndianRed,
            _ => Brushes.Gray,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
