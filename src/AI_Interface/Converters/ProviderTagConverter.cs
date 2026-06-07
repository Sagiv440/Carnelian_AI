using System;
using System.Globalization;
using Avalonia.Data.Converters;
using AI_Interface.Models;

namespace AI_Interface.Converters;

/// <summary>
/// Converts an <see cref="AiProvider"/> into its short picker tag ("Local", "OpenAI", "Gemini",
/// "Claude") for the model dropdown's provider chip.
/// </summary>
public sealed class ProviderTagConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is AiProvider provider ? provider.Tag() : value?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
