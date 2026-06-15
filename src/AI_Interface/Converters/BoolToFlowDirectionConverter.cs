using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AI_Interface.Converters;

/// <summary>Converts a bool <c>IsRtl</c> flag to the appropriate <see cref="FlowDirection"/>.</summary>
public sealed class BoolToFlowDirectionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
