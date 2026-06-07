using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AI_Interface.Converters;

/// <summary>
/// Converts a hex color string (e.g. "#804DEE" or "#33804DEE") into a <see cref="SolidColorBrush"/>
/// for swatches and live color previews. Invalid/partial input yields no value, so the previous
/// brush is kept rather than throwing while a user types a hex code.
/// </summary>
public sealed class StringToBrushConverter : IValueConverter
{
    public static readonly StringToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && Color.TryParse(hex, out var color))
            return new SolidColorBrush(color);
        return BindingOperations.DoNothing;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SolidColorBrush brush)
            return brush.Color.ToString();
        return BindingOperations.DoNothing;
    }
}
