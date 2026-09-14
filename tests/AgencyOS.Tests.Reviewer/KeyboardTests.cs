using AgencyOS.Reviewer.Runtime;
using Xunit;

namespace AgencyOS.Tests.Reviewer;

/// <summary>
/// That typed text arrives as the text that was typed.
/// </summary>
/// <remarks>
/// <para>
/// Audit 002 asked the harness to type a command's label into the palette. The
/// harness sent virtual-key codes, Windows mapped them through whatever keyboard
/// layout was active, and the application received Hebrew. The palette matched
/// nothing, and seventeen dialogs reachable only from the palette were about to
/// be recorded as unreachable on the strength of it.
/// </para>
/// <para>
/// A character is not a key. This pins the distinction, because the failure is
/// invisible on any machine whose layout happens to be the one the codes assume.
/// </para>
/// </remarks>
public sealed class KeyboardTests
{
    /// <summary>A character is sent as a character, not as a key on a layout.</summary>
    [Theory]
    [InlineData('S')]
    [InlineData('a')]
    [InlineData(' ')]
    [InlineData('-')]
    [InlineData('7')]
    public void TypedTextIsSentLiterally(char character)
    {
        Native.Input press = Keyboard.Character(character, up: false);

        Assert.Equal(Native.InputKeyboard, press.Type);
        Assert.Equal(Native.KeyEventUnicode, press.Data.Keyboard.Flags);
        Assert.Equal(character, (char)press.Data.Keyboard.ScanCode);

        // A virtual key of zero is what tells Windows to read the scan code as a
        // character. Any other value sends it back through the layout.
        Assert.Equal(0, press.Data.Keyboard.VirtualKey);
    }

    /// <summary>The release carries the same character.</summary>
    [Fact]
    public void TheReleaseCarriesTheSameCharacter()
    {
        Native.Input release = Keyboard.Character('S', up: true);

        Assert.Equal(Native.KeyEventUnicode | Native.KeyEventKeyUp, release.Data.Keyboard.Flags);
        Assert.Equal('S', (char)release.Data.Keyboard.ScanCode);
    }

    /// <summary>
    /// Characters outside the ASCII letters survive.
    /// </summary>
    /// <remarks>
    /// The previous implementation silently dropped anything it could not map to a
    /// key, so a label with an apostrophe or an accent was typed incompletely and
    /// the search that followed matched nothing for a second reason.
    /// </remarks>
    [Theory]
    [InlineData('é')]
    [InlineData('\'')]
    [InlineData('/')]
    public void UnmappableCharactersAreStillSent(char character) =>
        Assert.Equal(character, (char)Keyboard.Character(character, up: false).Data.Keyboard.ScanCode);
}
