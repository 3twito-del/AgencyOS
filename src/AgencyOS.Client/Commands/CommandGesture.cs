namespace AgencyOS.Client.Commands;

/// <summary>
/// A key, named without reference to any windowing system.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not <c>Windows.System.VirtualKey</c>. The command registry is the
/// single source of truth for both the palette and the accelerators, and the
/// palette lives in this assembly, which cannot reference WinRT. A key enum of our
/// own is the price of having one list rather than two that drift (ADR-0032).
/// </para>
/// <para>
/// Only the keys AgencyOS actually binds are listed. An enum covering every key a
/// keyboard has would invite bindings nobody validated on a non-US layout.
/// </para>
/// </remarks>
public enum CommandKey
{
    /// <summary>No key. The command is palette-only.</summary>
    None = 0,

    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,

    A, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,

    /// <summary>Escape. Bound only for dismissal semantics.</summary>
    Escape,

    /// <summary>Enter. Bound only where a list has an unambiguous default action.</summary>
    Enter,
}

/// <summary>Modifier keys, combinable.</summary>
[Flags]
public enum CommandModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
}

/// <summary>
/// A keyboard gesture, comparable and renderable.
/// </summary>
/// <remarks>
/// A value type on purpose: gesture collision detection is equality over a set,
/// and the M12 <c>Ctrl+9</c> incident happened because two places each held a
/// gesture as loose constants that nothing compared.
/// </remarks>
public readonly record struct CommandGesture(CommandKey Key, CommandModifiers Modifiers)
{
    /// <summary>A function-key gesture with no modifier.</summary>
    public static CommandGesture Function(CommandKey key) => new(key, CommandModifiers.None);

    /// <summary>A Ctrl-modified gesture.</summary>
    public static CommandGesture Control(CommandKey key) => new(key, CommandModifiers.Control);

    /// <summary>A Ctrl+Shift-modified gesture.</summary>
    public static CommandGesture ControlShift(CommandKey key) =>
        new(key, CommandModifiers.Control | CommandModifiers.Shift);

    /// <summary>
    /// Whether this gesture is one a keyboard can actually produce.
    /// </summary>
    /// <remarks>
    /// A bare letter with no modifier is refused: it would swallow typing in every
    /// text box on the page. Function keys are the only unmodified gestures
    /// AgencyOS binds, which is why Intelligence took F7 and AI took F6.
    /// </remarks>
    public bool IsWellFormed
    {
        get
        {
            if (Key == CommandKey.None)
            {
                return false;
            }

            bool isFunctionKey = Key is >= CommandKey.F1 and <= CommandKey.F12;

            return Modifiers != CommandModifiers.None || isFunctionKey;
        }
    }

    /// <summary>How the gesture is written on screen.</summary>
    public string Display
    {
        get
        {
            if (Key == CommandKey.None)
            {
                return string.Empty;
            }

            List<string> parts = [];

            if (Modifiers.HasFlag(CommandModifiers.Control))
            {
                parts.Add("Ctrl");
            }

            if (Modifiers.HasFlag(CommandModifiers.Shift))
            {
                parts.Add("Shift");
            }

            if (Modifiers.HasFlag(CommandModifiers.Alt))
            {
                parts.Add("Alt");
            }

            parts.Add(KeyName(Key));

            return string.Join('+', parts);
        }
    }

    public override string ToString() => Display;

    private static string KeyName(CommandKey key) => key switch
    {
        >= CommandKey.D0 and <= CommandKey.D9 => ((int)(key - CommandKey.D0)).ToString(
            System.Globalization.CultureInfo.InvariantCulture),
        CommandKey.Escape => "Esc",
        CommandKey.Enter => "Enter",
        _ => key.ToString(),
    };
}
