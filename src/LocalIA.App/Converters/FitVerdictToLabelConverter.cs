using System.Globalization;
using System.Windows.Data;
using LocalIA.Core.Advisor;

namespace LocalIA.App.Converters;

public sealed class FitVerdictToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value as FitVerdict?) switch
        {
            FitVerdict.ComfortableFit => "Tient confortablement en VRAM",
            FitVerdict.TightFit => "Tient en VRAM, mais de justesse",
            FitVerdict.RamOnlyWillBeSlow => "Débordera sur la RAM (plus lent)",
            FitVerdict.DoesNotFit => "Ne tient pas sur ce matériel",
            _ => "Inconnu",
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
