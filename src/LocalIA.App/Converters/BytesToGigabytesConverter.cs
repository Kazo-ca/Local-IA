using System.Globalization;
using System.Windows.Data;

namespace LocalIA.App.Converters;

public sealed class BytesToGigabytesConverter : IValueConverter
{
    /// <summary>Partagé avec tout code C# qui a besoin du même format (ex. le résumé matériel
    /// envoyé à l'IA dans ConfigurationAdvisorViewModel) — une seule implémentation à faire évoluer.</summary>
    public static string Format(long bytes)
    {
        var gigabytes = bytes / 1024.0 / 1024.0 / 1024.0;
        return $"{gigabytes.ToString("0.0", CultureInfo.InvariantCulture)} Go";
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var bytes = value switch
        {
            long l => l,
            int i => i,
            _ => 0L,
        };

        return Format(bytes);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
