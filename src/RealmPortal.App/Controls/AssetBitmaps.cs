using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace RealmPortal.App.Controls;

/// <summary>
/// Loads the designer's PNG art. Frames come as <c>name.png</c> (@1x) and <c>name@2x.png</c> (exactly twice the pixels);
/// the @2x file is used from a render scale of 1.5 up, so the art is never upscaled.
/// </summary>
internal static class AssetBitmaps
{
    /// <summary>Render scale from which the @2x art is used.</summary>
    public const double HiDpiThreshold = 1.5;

    private static readonly Dictionary<string, Bitmap> Cache = new();

    public static double ScaleOf(Avalonia.Visual visual) => TopLevel.GetTopLevel(visual)?.RenderScaling ?? 1.0;

    /// <summary>Returns the frame bitmap for the scale and its pixels-per-DIP factor (1 or 2).</summary>
    public static (Bitmap Bitmap, int Factor) Frame(string name, double scale)
    {
        var factor = scale >= HiDpiThreshold ? 2 : 1;
        var file = factor == 2 ? $"{name}@2x.png" : $"{name}.png";
        return (Load($"avares://RealmPortal.App/Assets/Frames/{file}"), factor);
    }

    public static Bitmap Seal(string name, bool hiRes) =>
        Load($"avares://RealmPortal.App/Assets/Seals/{name}-{(hiRes ? 128 : 64)}.png");

    private static Bitmap Load(string uri)
    {
        if (!Cache.TryGetValue(uri, out var bitmap))
        {
            using var stream = AssetLoader.Open(new Uri(uri));
            bitmap = new Bitmap(stream);
            Cache[uri] = bitmap;
        }

        return bitmap;
    }
}
