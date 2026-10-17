using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace RealmPortal.App.Platform;

/// <summary>One icon size for <c>_NET_WM_ICON</c>.</summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="Argb">Non-premultiplied pixels, <c>0xAARRGGBB</c>, row by row.</param>
internal sealed record WindowIconImage(int Width, int Height, uint[] Argb);

/// <summary>
/// Linux/X11 only: sets the <c>_NET_WM_ICON</c> property of the main window with every Tésera size (16 to 256 px). Avalonia sets that
/// property itself, but from the single <c>Window.Icon</c> bitmap; here the window manager gets the natively drawn micro (up to 24 px),
/// simplified (32 px) and full (48 px and up) variants and picks the best one for the panel, the task switcher and Alt-Tab.
/// Everything is best effort: on any other platform, without libX11, or when the window is not an X11 window, nothing happens.
/// </summary>
internal static class X11WindowIcon
{
    /// <summary>The sizes bundled as <c>Assets/Icons/realm-portal-N.png</c> (the art is drawn per size, never resized).</summary>
    public static readonly int[] Sizes = [16, 22, 24, 32, 48, 64, 128, 256];

    private const int XaCardinal = 6;
    private const int PropModeReplace = 0;

    /// <summary>
    /// The value of the property: for each image <c>width, height, pixels…</c> as 32-bit values
    /// (the layout the EWMH spec defines for <c>_NET_WM_ICON</c>).
    /// </summary>
    public static uint[] BuildProperty(IEnumerable<WindowIconImage> images)
    {
        var data = new List<uint>();
        foreach (var image in images)
        {
            if (image.Width <= 0 || image.Height <= 0 || image.Argb.Length != image.Width * image.Height)
                throw new ArgumentException($"The {image.Width}x{image.Height} icon has {image.Argb.Length} pixels.", nameof(images));
            data.Add((uint)image.Width);
            data.Add((uint)image.Height);
            data.AddRange(image.Argb);
        }

        return [.. data];
    }

    /// <summary>Converts 32-bit RGBA/BGRA rows (premultiplied or not) to non-premultiplied <c>0xAARRGGBB</c>.</summary>
    public static uint[] PackArgb(ReadOnlySpan<byte> pixels, int width, int height, int rowBytes, bool bgra, bool premultiplied)
    {
        var result = new uint[width * height];
        for (var y = 0; y < height; y++)
        {
            var row = pixels.Slice(y * rowBytes, width * 4);
            for (var x = 0; x < width; x++)
            {
                var p = row.Slice(x * 4, 4);
                uint r = bgra ? p[2] : p[0];
                uint g = p[1];
                uint b = bgra ? p[0] : p[2];
                uint a = p[3];
                if (premultiplied && a is > 0 and < 255)
                {
                    r = Math.Min(255, (r * 255 + a / 2) / a);
                    g = Math.Min(255, (g * 255 + a / 2) / a);
                    b = Math.Min(255, (b * 255 + a / 2) / a);
                }

                result[y * width + x] = (a << 24) | (r << 16) | (g << 8) | b;
            }
        }

        return result;
    }

    /// <summary>Decodes a PNG into a <see cref="WindowIconImage"/>.</summary>
    public static WindowIconImage Decode(Stream png)
    {
        using var bitmap = WriteableBitmap.Decode(png);
        using var frame = bitmap.Lock();
        var size = frame.Size;
        var bytes = new byte[frame.RowBytes * size.Height];
        Marshal.Copy(frame.Address, bytes, 0, bytes.Length);
        var bgra = frame.Format == PixelFormat.Bgra8888;
        if (!bgra && frame.Format != PixelFormat.Rgba8888)
            throw new NotSupportedException($"Unsupported pixel format {frame.Format}.");
        var premultiplied = bitmap.AlphaFormat == AlphaFormat.Premul;
        return new WindowIconImage(size.Width, size.Height, PackArgb(bytes, size.Width, size.Height, frame.RowBytes, bgra, premultiplied));
    }

    /// <summary>Loads every bundled size.</summary>
    public static List<WindowIconImage> LoadBundled() =>
        Sizes.Select(size =>
        {
            using var stream = AssetLoader.Open(new Uri($"avares://RealmPortal.App/Assets/Icons/realm-portal-{size}.png"));
            return Decode(stream);
        }).ToList();

    /// <summary>Sets <c>_NET_WM_ICON</c> on the window when it is an X11 window. Returns whether the property was written.</summary>
    public static bool TrySet(Window window)
    {
        if (!OperatingSystem.IsLinux())
            return false;

        try
        {
            var handle = window.TryGetPlatformHandle();
            if (handle is null || handle.HandleDescriptor != "XID")
                return false;

            var property = BuildProperty(LoadBundled());
            var values = property.Select(v => unchecked((nint)v)).ToArray(); // X sends format-32 data as C longs
            return X11Native.SetCardinalProperty((nuint)handle.Handle, "_NET_WM_ICON", values);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or NotSupportedException or InvalidOperationException or IOException)
        {
            Trace.TraceWarning($"Could not set _NET_WM_ICON: {ex.Message}");
            return false;
        }
    }

    private static class X11Native
    {
        public static bool SetCardinalProperty(nuint window, string name, nint[] values)
        {
            var display = XOpenDisplay(IntPtr.Zero); // our own connection; the property stays on the window after it is closed
            if (display == IntPtr.Zero)
                return false;

            var pin = GCHandle.Alloc(values, GCHandleType.Pinned);
            try
            {
                var atom = XInternAtom(display, name, false);
                var status = XChangeProperty(display, window, atom, XaCardinal, 32, PropModeReplace, pin.AddrOfPinnedObject(), values.Length);
                XFlush(display);
                return status != 0;
            }
            finally
            {
                pin.Free();
                XCloseDisplay(display);
            }
        }

        [DllImport("libX11.so.6")]
        private static extern IntPtr XOpenDisplay(IntPtr displayName);

        [DllImport("libX11.so.6")]
        private static extern int XCloseDisplay(IntPtr display);

        [DllImport("libX11.so.6", CharSet = CharSet.Ansi)]
        private static extern nuint XInternAtom(IntPtr display, string atomName, [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);

        [DllImport("libX11.so.6")]
        private static extern int XChangeProperty(IntPtr display, nuint window, nuint property, nuint type, int format, int mode, IntPtr data, int nelements);

        [DllImport("libX11.so.6")]
        private static extern int XFlush(IntPtr display);
    }
}
