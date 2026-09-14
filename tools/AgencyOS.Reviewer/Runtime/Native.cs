using System.Runtime.InteropServices;

namespace AgencyOS.Reviewer.Runtime;

/// <summary>
/// The Win32 surface the harness needs, and nothing more.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately small. A review harness that can drive the whole desktop is a
/// remote-control tool that happens to write reports; everything here is scoped
/// to a window handle the caller already proved belongs to the AgencyOS process.
/// </para>
/// <para>
/// Screen capture is done by copying the desktop device context over the window's
/// own bounds rather than by <c>PrintWindow</c>, because WinUI 3 composes through
/// DirectComposition and <c>PrintWindow</c> returns a blank or stale surface for
/// parts of it. Copying what is on screen is also the honest thing to capture:
/// the evidence should be what a person would have seen.
/// </para>
/// </remarks>
internal static class Native
{
    internal const int SrcCopy = 0x00CC0020;
    internal const int CaptureBlt = 0x40000000;
    internal const int DwmwaExtendedFrameBounds = 9;

    /// <summary>The DPI a window is being laid out for.</summary>
    /// <remarks>
    /// UI Automation reports physical pixels; WinUI's own breakpoints are written
    /// in effective units. Without this the two are silently conflated, and a
    /// 1600x1000 window on a 150% display looks like it should fit things it
    /// cannot.
    /// </remarks>
    /// <param name="window">The window handle.</param>
    /// <returns>Dots per inch, where 96 is unscaled.</returns>
    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint window);

    /// <summary>Per-monitor DPI awareness, version 2.</summary>
    internal static readonly nint PerMonitorAwareV2 = -4;

    /// <summary>Tells Windows this process reads real pixels.</summary>
    /// <remarks>
    /// Without it <see cref="GetDpiForWindow"/> answers 96 whatever the display is
    /// doing, because Windows lies to processes that have not said they can cope
    /// with the truth. Audit 001R's first pass recorded a scale of 1.00 on a
    /// display running at 150%, which would have made every effective-unit
    /// argument in the report wrong. The coordinates UI Automation reports were
    /// physical either way; only the question "what is one effective unit worth"
    /// was being answered falsely.
    /// </remarks>
    /// <param name="context">The awareness context to adopt.</param>
    /// <returns>True when the process adopted it.</returns>
    [DllImport("user32.dll")]
    internal static extern bool SetProcessDpiAwarenessContext(nint context);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(nint context, int index);

    /// <summary>Pixels per logical inch, horizontally.</summary>
    private const int LogPixelsX = 88;

    /// <summary>The screen height this process is being shown.</summary>
    private const int VertRes = 10;

    /// <summary>The screen height the display actually has.</summary>
    private const int DesktopVertRes = 117;

    /// <summary>
    /// What one effective unit is worth in the pixels the tree reports.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked two ways, because each is only reliable in one state. A process that
    /// has claimed DPI awareness gets the truth from the device's logical pixel
    /// count; one that has not is told 96 whatever the display is doing, but is
    /// given away by the gap between the screen height it is shown and the screen
    /// height that exists. Taking the larger answer is right in both states.
    /// </para>
    /// <para>
    /// Audit 001R's first pass reported 1.00 on a display running at 150%. Nothing
    /// downstream measured differently — UI Automation reports physical pixels
    /// either way — but every sentence about what fits in a window would have been
    /// wrong by half, which is exactly the class of quiet error this rebaseline
    /// exists to stop repeating.
    /// </para>
    /// </remarks>
    /// <returns>The scale, where 1.0 is an unscaled display.</returns>
    internal static double DisplayScale()
    {
        nint screen = GetDC(0);

        if (screen == 0)
        {
            return 1;
        }

        try
        {
            double declared = GetDeviceCaps(screen, LogPixelsX) / 96.0;
            int shown = GetDeviceCaps(screen, VertRes);
            int real = GetDeviceCaps(screen, DesktopVertRes);

            double observed = shown > 0 ? real / (double)shown : 1;

            return Math.Round(Math.Max(Math.Max(declared, observed), 1), 2);
        }
        finally
        {
            ReleaseDC(0, screen);
        }
    }

    internal const uint InputKeyboard = 1;
    internal const uint KeyEventKeyUp = 0x0002;
    internal const uint KeyEventExtendedKey = 0x0001;

    /// <summary>
    /// Send the character itself rather than a key on the keyboard.
    /// </summary>
    /// <remarks>
    /// A virtual-key code is interpreted through whatever layout is active, so
    /// typing "State a thesis" on a machine set to Hebrew produces Hebrew. Audit
    /// 002 found that the hard way. With this flag the character in
    /// <c>ScanCode</c> is delivered literally and the layout is not consulted.
    /// </remarks>
    internal const uint KeyEventUnicode = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;

        [FieldOffset(0)]
        private readonly Padding _padding;
    }

    [StructLayout(LayoutKind.Sequential, Size = 28)]
    private readonly struct Padding;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MoveWindow(
        nint window, int x, int y, int width, int height, [MarshalAs(UnmanagedType.Bool)] bool repaint);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern nint GetDesktopWindow();

    [DllImport("user32.dll")]
    internal static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(nint window, nint context);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int key);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(
        nint window, int attribute, out Rect value, int size);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateCompatibleDC(nint context);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateCompatibleBitmap(nint context, int width, int height);

    [DllImport("gdi32.dll")]
    internal static extern nint SelectObject(nint context, nint handle);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(nint handle);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteDC(nint context);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BitBlt(
        nint destination, int x, int y, int width, int height,
        nint source, int sourceX, int sourceY, int operation);

    [DllImport("gdi32.dll")]
    internal static extern int GetDIBits(
        nint context, nint bitmap, uint start, uint lines, byte[]? bits, ref BitmapInfo info, uint usage);

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPixelsPerMeter;
        public int YPixelsPerMeter;
        public uint ColorUsed;
        public uint ColorImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfo
    {
        public BitmapInfoHeader Header;

        // Three colour masks, inline rather than as an array, so the struct stays
        // blittable and the interop needs no marshalling layer.
        public uint FirstMask;
        public uint SecondMask;
        public uint ThirdMask;
    }
}
