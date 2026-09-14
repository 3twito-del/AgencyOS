using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;

namespace AgencyOS.Reviewer.Runtime;

/// <summary>
/// Captures one window, never the desktop.
/// </summary>
/// <remarks>
/// <para>
/// Scoped to a window handle on purpose. The review runs on a real desktop that
/// may have anything else open on it, and a harness that photographs the screen
/// would put whatever else is there into the evidence pack. Cropping to the
/// window's extended frame bounds is what keeps §1's "no screenshot containing
/// real private data" true by construction rather than by care.
/// </para>
/// <para>
/// PNG encoding uses <c>PngBitmapEncoder</c> from PresentationCore, which the
/// project already has for UI Automation, so the harness adds no package.
/// </para>
/// </remarks>
internal static class ScreenCapture
{
    /// <summary>Writes a PNG of one window, returning false when it cannot.</summary>
    internal static bool TryCapture(nint window, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (window == 0)
        {
            return false;
        }

        Native.Rect bounds;

        // The extended frame bounds exclude the invisible resize border, so the
        // capture matches what the window looks like rather than what it occupies.
        if (Native.DwmGetWindowAttribute(
                window,
                Native.DwmwaExtendedFrameBounds,
                out bounds,
                System.Runtime.InteropServices.Marshal.SizeOf<Native.Rect>()) != 0
            && !Native.GetWindowRect(window, out bounds))
        {
            return false;
        }

        int width = bounds.Width;
        int height = bounds.Height;

        if (width <= 0 || height <= 0 || width > 16384 || height > 16384)
        {
            return false;
        }

        nint screen = Native.GetDC(0);
        nint memory = Native.CreateCompatibleDC(screen);
        nint bitmap = Native.CreateCompatibleBitmap(screen, width, height);
        nint previous = Native.SelectObject(memory, bitmap);

        try
        {
            if (!Native.BitBlt(
                    memory, 0, 0, width, height,
                    screen, bounds.Left, bounds.Top,
                    Native.SrcCopy | Native.CaptureBlt))
            {
                return false;
            }

            Native.BitmapInfo info = new()
            {
                Header = new Native.BitmapInfoHeader
                {
                    Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.BitmapInfoHeader>(),
                    Width = width,

                    // Negative height asks GDI for a top-down image, which is the
                    // order every encoder expects and saves a flip.
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                },
            };

            byte[] pixels = new byte[width * height * 4];

            Native.SelectObject(memory, previous);

            if (Native.GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref info, 0) == 0)
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            BitmapSource source = BitmapSource.Create(
                width,
                height,
                96,
                96,
                PixelFormats.Bgr32,
                null,
                pixels,
                width * 4);

            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(source));

            using FileStream stream = File.Create(path);
            encoder.Save(stream);

            return true;
        }
        finally
        {
            Native.DeleteObject(bitmap);
            Native.DeleteDC(memory);
            Native.ReleaseDC(0, screen);
        }
    }
}
