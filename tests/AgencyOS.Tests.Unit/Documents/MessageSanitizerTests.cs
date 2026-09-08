using AgencyOS.Application.Communications;
using Xunit;

namespace AgencyOS.Tests.Unit.Documents;

/// <summary>
/// What survives being stored, and what does not.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these inputs is something a real message has contained. Provider
/// HTML is written by whoever sent the mail, and it is stored, so sanitizing on
/// the way in is the only point at which one pass protects every surface that will
/// ever render it (ADR-0026).
/// </para>
/// <para>
/// Two removals are worth stating. Script goes because it would run. External
/// images go because they do not need to run: fetching one tells the sender that
/// somebody at the agency opened their email, which is a fact about the agency
/// that the sender should not be given, least of all years later while somebody
/// browses a deal's history.
/// </para>
/// </remarks>
public sealed class MessageSanitizerTests
{
    /// <summary>Script never survives, in any of the shapes it arrives in.</summary>
    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<SCRIPT>alert(1)</SCRIPT>")]
    [InlineData("<script type=\"text/javascript\">alert(1)</script>")]
    [InlineData("<scr<script>ipt>alert(1)</script>")]
    [InlineData("<noscript><script>alert(1)</script></noscript>")]
    [InlineData("<div onclick=\"alert(1)\">text</div>")]
    [InlineData("<div ONMOUSEOVER=alert(1)>text</div>")]
    [InlineData("<svg onload=\"alert(1)\"></svg>")]
    [InlineData("<body onload=alert(1)>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<iframe src=\"javascript:alert(1)\"></iframe>")]
    [InlineData("<a href=\"javascript:alert(1)\">click</a>")]
    [InlineData("<form action=\"https://phish.test\"><input name=\"pw\"></form>")]
    [InlineData("<object data=\"evil.swf\"></object>")]
    [InlineData("<embed src=\"evil.swf\">")]
    [InlineData("<style>body{background:url('https://tracker.test')}</style>")]
    [InlineData("<link rel=\"stylesheet\" href=\"https://elsewhere.test/x.css\">")]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0;url=https://elsewhere.test\">")]
    [InlineData("<base href=\"https://elsewhere.test/\">")]
    public void NothingExecutableSurvives(string hostile)
    {
        string sanitized = MessageSanitizer.Sanitize(hostile) ?? string.Empty;

        foreach (string forbidden in new[]
        {
            "<script", "<iframe", "<object", "<embed", "<svg", "<form", "<input",
            "<style", "<link", "<meta", "<base", "<img", "<applet", "<noscript",
            "onclick", "onload", "onerror", "onmouseover", "javascript:",
            "href", "src=",
        })
        {
            Assert.DoesNotContain(forbidden, sanitized, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// An external image reference never survives, whatever it is hidden in.
    /// </summary>
    /// <remarks>
    /// The quiet one. Nothing about a tracking pixel looks dangerous, and the harm
    /// is not to the machine rendering it.
    /// </remarks>
    [Theory]
    [InlineData("<img src=\"https://tracker.test/beacon.gif\" width=\"1\" height=\"1\">")]
    [InlineData("<div style=\"background-image:url(https://tracker.test/b.png)\">x</div>")]
    [InlineData("<table background=\"https://tracker.test/b.png\"><tr><td>x</td></tr></table>")]
    public void NoExternalReferenceSurvives(string beacon)
    {
        string sanitized = MessageSanitizer.Sanitize(beacon) ?? string.Empty;

        Assert.DoesNotContain("tracker.test", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>What a person actually wrote survives, with its structure.</summary>
    /// <remarks>
    /// The check that stops the sanitizer becoming a shredder. A stored message
    /// with its paragraphs and lists gone is a message somebody has to open Outlook
    /// to read, which defeats storing it.
    /// </remarks>
    [Fact]
    public void ProseAndStructureSurvive()
    {
        string sanitized = MessageSanitizer.Sanitize(
            "<div><p>We can do <strong>750</strong> against the guarantee.</p>"
            + "<ul><li>Two episodes</li><li>Second position</li></ul>"
            + "<blockquote>As discussed on Tuesday.</blockquote></div>")!;

        Assert.Contains("We can do", sanitized, StringComparison.Ordinal);
        Assert.Contains("<strong>", sanitized, StringComparison.Ordinal);
        Assert.Contains("750", sanitized, StringComparison.Ordinal);
        Assert.Contains("<ul>", sanitized, StringComparison.Ordinal);
        Assert.Contains("Second position", sanitized, StringComparison.Ordinal);
        Assert.Contains("<blockquote>", sanitized, StringComparison.Ordinal);
    }

    /// <summary>An allowed element keeps none of its attributes.</summary>
    /// <remarks>
    /// An allow-list of elements with a free-for-all on attributes is not an
    /// allow-list. Every attribute goes, including the ones that look harmless:
    /// <c>style</c> can position an element over the interface, and <c>id</c> can
    /// collide with the host page.
    /// </remarks>
    [Fact]
    public void AllowedElementsKeepNoAttributes()
    {
        string sanitized = MessageSanitizer.Sanitize(
            "<p class=\"x\" id=\"y\" style=\"position:fixed;top:0\" data-x=\"1\">text</p>")!;

        Assert.Contains("text", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("class", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-x", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("position:fixed", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The text of a link is kept, and its destination is not made clickable.
    /// </summary>
    /// <remarks>
    /// A reader needs to see what the message said. What they do not need is a
    /// live link to a phishing site inside a window that already holds the agency's
    /// records (ADR-0026).
    /// </remarks>
    [Fact]
    public void ALinkKeepsItsTextAndLosesItsDestination()
    {
        string sanitized = MessageSanitizer.Sanitize(
            "<p>See <a href=\"https://phish.test/login\">the contract</a> attached.</p>")!;

        Assert.Contains("the contract", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("phish.test", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("href", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Malformed markup does not defeat the sanitizer or throw.</summary>
    /// <remarks>
    /// Real mail is full of unclosed tags, stray angle brackets and mismatched
    /// quotes. A sanitizer that threw on any of them would lose messages.
    /// </remarks>
    [Theory]
    [InlineData("<p>unclosed")]
    [InlineData("<<p>>text<</p>>")]
    [InlineData("<p onclick='alert(1)'>text")]
    [InlineData("a < b and c > d")]
    [InlineData("<script")]
    [InlineData("<script>alert(1)")]
    [InlineData("<p><b><i>nested")]
    [InlineData("<>")]
    [InlineData("<!-- <script>alert(1)</script> -->")]
    public void MalformedMarkupIsSurvivable(string malformed)
    {
        string sanitized = MessageSanitizer.Sanitize(malformed) ?? string.Empty;

        Assert.DoesNotContain("<script", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Nothing to sanitize is reported as nothing, not as an empty string.</summary>
    /// <remarks>
    /// A real answer. A message with no usable HTML shows its plain-text body, and
    /// an empty string would render as a blank message instead.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingToSanitize_IsNull(string? html) =>
        Assert.Null(MessageSanitizer.Sanitize(html));

    /// <summary>
    /// Plain text is produced from the sanitized markup, never from the original.
    /// </summary>
    [Fact]
    public void PlainTextComesFromTheSanitizedMarkup()
    {
        string text = MessageSanitizer.ToPlainText(
            "<p>Real content.</p><script>alert('secret')</script>")!;

        Assert.Contains("Real content.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("alert", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<", text, StringComparison.Ordinal);
    }
}
