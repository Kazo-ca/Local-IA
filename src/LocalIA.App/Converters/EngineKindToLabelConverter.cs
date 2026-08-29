using System.Globalization;
using System.Windows.Data;
using LocalIA.Core.Models;

namespace LocalIA.App.Converters;

public sealed class EngineKindToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value as EngineKind?) switch
        {
            EngineKind.Ollama => "Ollama",
            EngineKind.LlamaCpp => "llama.cpp",
            _ => "?",
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
