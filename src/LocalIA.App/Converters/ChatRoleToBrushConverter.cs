using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LocalIA.Core.Models;

namespace LocalIA.App.Converters;

public sealed class ChatRoleToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value as ChatRole?) switch
        {
            ChatRole.User => new SolidColorBrush(Color.FromRgb(0x1F, 0x2A, 0x44)),
            ChatRole.Assistant => new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
            _ => new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xD6)),
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
