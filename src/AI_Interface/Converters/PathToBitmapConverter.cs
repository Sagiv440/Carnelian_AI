using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace AI_Interface.Converters;

/// <summary>
/// Loads an image file path into a downscaled <see cref="Bitmap"/> thumbnail for attachment previews.
/// Returns null on any failure (missing/unreadable/non-image file) so the UI simply shows nothing.
/// </summary>
public sealed class PathToBitmapConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path))
            return null;

        try
        {
            using var stream = File.OpenRead(path);
            // Decode to a small width — we only need a thumbnail, not the full-resolution image.
            return Bitmap.DecodeToWidth(stream, 240);
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
