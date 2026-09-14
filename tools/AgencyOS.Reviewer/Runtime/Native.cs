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

    internal const uint InputKeyboard = 1;
    internal const uint KeyEventKeyUp = 0x0002;
    internal const uint KeyEventExtendedKey = 0x0001;

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
