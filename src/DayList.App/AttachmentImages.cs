using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

namespace DayList;

internal static class AttachmentImages
{
    public static async Task<ImageSource?> LoadAsync(string? fullPath, int decodePixelWidth)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
            return null;

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(fullPath);
            using var stream = await file.OpenReadAsync();
            var bitmap = new BitmapImage();
            if (decodePixelWidth > 0)
                bitmap.DecodePixelWidth = decodePixelWidth;
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }
        catch
        {
            return TryUri(fullPath, decodePixelWidth);
        }
    }

    private static ImageSource? TryUri(string fullPath, int decodePixelWidth)
    {
        try
        {
            var bitmap = new BitmapImage();
            if (decodePixelWidth > 0)
                bitmap.DecodePixelWidth = decodePixelWidth;
            bitmap.UriSource = new Uri(fullPath);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
