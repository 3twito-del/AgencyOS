using System.Diagnostics;

namespace AgencyOS.Reviewer.Runtime;

/// <summary>Keys the harness is allowed to press.</summary>
/// <remarks>
/// A closed list. Arbitrary synthetic input on a real desktop is a way to do
/// something nobody asked for to whatever happens to be focused, so the harness
/// can press navigation, activation and the gestures AgencyOS itself declares —
/// and nothing else.
/// </remarks>
public enum ReviewKey
{
    /// <summary>Tab.</summary>
    Tab = 0x09,

    /// <summary>Enter.</summary>
    Enter = 0x0D,

    /// <summary>Escape.</summary>
    Escape = 0x1B,

    /// <summary>Space.</summary>
    Space = 0x20,

    /// <summary>Left arrow.</summary>
    Left = 0x25,

    /// <summary>Up arrow.</summary>
    Up = 0x26,

    /// <summary>Right arrow.</summary>
    Right = 0x27,

    /// <summary>Down arrow.</summary>
    Down = 0x28,

    /// <summary>Home.</summary>
    Home = 0x24,

    /// <summary>End.</summary>
    End = 0x23,
}

/// <summary>Modifier keys the harness may hold.</summary>
[Flags]
public enum ReviewModifiers
{
    /// <summary>No modifier.</summary>
    None = 0,

    /// <summary>Control.</summary>
    Control = 1,

    /// <summary>Shift.</summary>
    Shift = 2,

    /// <summary>Alt.</summary>
    Alt = 4,
}

/// <summary>
/// Sends keystrokes, and only to the window under review.
/// </summary>
/// <remarks>
/// Every send checks that the foreground window still belongs to the process
/// under review and refuses otherwise. Without that check a stolen focus - a
/// notification toast, an installer, the reviewer's own terminal - would turn a
/// keyboard audit into typing into somebody else's application.
/// </remarks>
internal sealed class Keyboard
{
    private const int VkControl = 0x11;
    private const int VkShift = 0x10;
    private const int VkAlt = 0x12;

    private readonly Process _process;

    internal Keyboard(Process process) => _process = process;

    /// <summary>How many sends were refused because focus had left the application.</summary>
    internal int RefusedSends { get; private set; }

    /// <summary>Presses one of the allowed keys.</summary>
    internal bool Press(ReviewKey key, ReviewModifiers modifiers = ReviewModifiers.None) =>
        Send((ushort)key, modifiers);

    /// <summary>Presses a letter or digit with a modifier, as a command gesture.</summary>
    /// <remarks>
    /// A modifier is required. An unmodified letter would be typing, and the
    /// harness never types into a field it has not been told to fill.
    /// </remarks>
    internal bool PressGesture(char key, ReviewModifiers modifiers)
    {
        if (modifiers == ReviewModifiers.None)
        {
            throw new ArgumentException(
                "A bare letter is typing, not a gesture. The harness does not type unprompted.",
                nameof(modifiers));
        }

        char upper = char.ToUpperInvariant(key);

        if (upper is not ((>= 'A' and <= 'Z') or (>= '0' and <= '9')))
        {
            throw new ArgumentOutOfRangeException(nameof(key), key, "Only letters and digits.");
        }

        return Send(upper, modifiers);
    }

    /// <summary>Presses a function key.</summary>
    internal bool PressFunction(int number, ReviewModifiers modifiers = ReviewModifiers.None)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, 12);

        return Send((ushort)(0x70 + number - 1), modifiers);
    }

    /// <summary>Types text into whatever already has focus.</summary>
    /// <remarks>
    /// Used only after the harness has put focus in a named text box on purpose.
    /// Restricted to characters a review fixture actually needs.
    /// </remarks>
    internal bool Type(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (char character in text)
        {
            if (character is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9')
                or ' ' or '-' or '.'))
            {
                continue;
            }

            ReviewModifiers modifiers = char.IsUpper(character) ? ReviewModifiers.Shift : ReviewModifiers.None;
            ushort code = char.IsLetter(character)
                ? char.ToUpperInvariant(character)
                : character switch
                {
                    ' ' => (ushort)0x20,
                    '-' => (ushort)0xBD,
                    '.' => (ushort)0xBE,
                    _ => character,
                };

            if (!Send(code, modifiers))
            {
                return false;
            }

            Thread.Sleep(12);
        }

        return true;
    }

    private bool Send(ushort key, ReviewModifiers modifiers)
    {
        if (!OwnsForeground())
        {
            RefusedSends++;

            return false;
        }

        List<Native.Input> inputs = [];

        if (modifiers.HasFlag(ReviewModifiers.Control))
        {
            inputs.Add(Down(VkControl));
        }

        if (modifiers.HasFlag(ReviewModifiers.Shift))
        {
            inputs.Add(Down(VkShift));
        }

        if (modifiers.HasFlag(ReviewModifiers.Alt))
        {
            inputs.Add(Down(VkAlt));
        }

        inputs.Add(Down(key));
        inputs.Add(Up(key));

        if (modifiers.HasFlag(ReviewModifiers.Alt))
        {
            inputs.Add(Up(VkAlt));
        }

        if (modifiers.HasFlag(ReviewModifiers.Shift))
        {
            inputs.Add(Up(VkShift));
        }

        if (modifiers.HasFlag(ReviewModifiers.Control))
        {
            inputs.Add(Up(VkControl));
        }

        Native.Input[] array = [.. inputs];

        uint sent = Native.SendInput(
            (uint)array.Length,
            array,
            System.Runtime.InteropServices.Marshal.SizeOf<Native.Input>());

        Thread.Sleep(40);

        return sent == array.Length;
    }

    private static Native.Input Down(ushort key) => new()
    {
        Type = Native.InputKeyboard,
        Data = new Native.InputUnion { Keyboard = new Native.KeyboardInput { VirtualKey = key } },
    };

    private static Native.Input Up(ushort key) => new()
    {
        Type = Native.InputKeyboard,
        Data = new Native.InputUnion
        {
            Keyboard = new Native.KeyboardInput { VirtualKey = key, Flags = Native.KeyEventKeyUp },
        },
    };

    /// <summary>Whether the window accepting keystrokes belongs to the application under review.</summary>
    private bool OwnsForeground()
    {
        nint foreground = Native.GetForegroundWindow();

        if (foreground == 0)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(foreground, out uint owner);

        return owner == (uint)_process.Id;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
