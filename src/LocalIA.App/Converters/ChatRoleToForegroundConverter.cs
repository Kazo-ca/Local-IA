using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LocalIA.Core.Models;

namespace LocalIA.App.Converters;

public sealed class ChatRoleToForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Equals(value, ChatRole.User) ? Brushes.White : Brushes.Black;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
