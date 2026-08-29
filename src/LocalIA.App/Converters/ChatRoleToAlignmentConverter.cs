using System.Globalization;
using System.Windows;
using System.Windows.Data;
using LocalIA.Core.Models;

namespace LocalIA.App.Converters;

public sealed class ChatRoleToAlignmentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Equals(value, ChatRole.User) ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
