using AgencyOS.Application.Documents;
using AgencyOS.Domain.Common;
using Xunit;

namespace AgencyOS.Tests.Unit.Documents;

/// <summary>
/// The rules about bytes arriving from outside.
/// </summary>
/// <remarks>
/// <c>docs/11_TESTING_AND_FORMAL_METHODS.md</c> makes hostile input a mandatory
/// target, and a filename is the most hostile field on an upload: it is chosen by
/// the sender, travels into a header, and is displayed. These are the cases that
/// would be silent if they were wrong (ADR-0024).
/// </remarks>
public sealed class UploadPolicyTests
{
    /// <summary>A supplied name never contributes a directory.</summary>
    [Theory]
    [InlineData("../../../etc/passwd", "passwd")]
    [InlineData("..\\..\\windows\\system32\\config\\sam", "sam")]
    [InlineData("/absolute/path.txt", "path.txt")]
    [InlineData("C:\\Windows\\System32\\drivers\\etc\\hosts", "hosts")]
    [InlineData("....//....//escape.txt", "escape.txt")]
    [InlineData("dir/subdir/file.pdf", "file.pdf")]
    public void APathIsReducedToItsLeaf(string supplied, string expected) =>
        Assert.Equal(expected, UploadPolicy.SafeFileName(supplied));

    /// <summary>
    /// A name that is only traversal, or only punctuation, becomes a name.
    /// </summary>
    /// <remarks>
    /// The empty result is the dangerous one. A filename that reduces to nothing
    /// and is then used as a path component is a path that means "this directory".
    /// </remarks>
    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("...")]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("/")]
    [InlineData("\\")]
    [InlineData("///")]
    public void ANameThatReducesToNothing_BecomesUntitled(string? supplied) =>
        Assert.Equal("untitled", UploadPolicy.SafeFileName(supplied));

    /// <summary>
    /// Control characters are removed, because a filename travels into a header.
    /// </summary>
    /// <remarks>
    /// A carriage return in a <c>Content-Disposition</c> value is a second header
    /// of the sender's choosing.
    /// </remarks>
    [Theory]
    [InlineData("report\r\nX-Injected: yes.txt")]
    [InlineData("nul\u0000byte.txt")]
    [InlineData("tab\there.txt")]
    [InlineData("vertical\u000Btab.txt")]
    [InlineData("bell\u0007.txt")]
    public void ControlCharacters_AreRemoved(string supplied)
    {
        string safe = UploadPolicy.SafeFileName(supplied);

        Assert.DoesNotContain(safe, c => char.IsControl(c));
    }

    /// <summary>Quotes are removed, for the same reason.</summary>
    [Fact]
    public void QuotesAreRemoved()
    {
        string safe = UploadPolicy.SafeFileName("invoice\"; DROP TABLE x; \".pdf");

        Assert.DoesNotContain('"', safe);

        // The rest survives as characters. It is a filename, not syntax, and
        // nothing anywhere interprets it.
        Assert.Contains("DROP TABLE", safe, StringComparison.Ordinal);
        Assert.EndsWith(".pdf", safe, StringComparison.Ordinal);
    }

    /// <summary>
    /// A name containing a separator keeps only its leaf, whatever surrounds it.
    /// </summary>
    /// <remarks>
    /// Worth stating separately because it surprises people: <c>file"; rm -rf /;
    /// "</c> reduces to what follows the last slash. That is the correct answer -
    /// the leaf of a path is the filename - and it is why nothing a caller supplies
    /// can contribute a directory.
    /// </remarks>
    [Fact]
    public void OnlyTheLeafOfAPathSurvives()
    {
        string safe = UploadPolicy.SafeFileName("file\"; rm -rf /; \"");

        Assert.DoesNotContain('/', safe);
        Assert.DoesNotContain(Path.DirectorySeparatorChar, safe);
        Assert.DoesNotContain('"', safe);
    }

    /// <summary>A very long name is bounded.</summary>
    [Fact]
    public void AVeryLongName_IsBounded()
    {
        string safe = UploadPolicy.SafeFileName(new string('a', 5_000) + ".pdf");

        Assert.True(safe.Length <= 300, $"A stored filename was {safe.Length} characters.");
    }

    /// <summary>An ordinary filename is left alone.</summary>
    /// <remarks>
    /// The check that stops the policy becoming over-eager. A name mangled for no
    /// reason is a file an operator no longer recognizes.
    /// </remarks>
    [Theory]
    [InlineData("Undertow - Executed Agreement (2027).pdf")]
    [InlineData("headshot_final_v2.JPG")]
    [InlineData("Sallow, Ada - resume.docx")]
    [InlineData("deal memo.txt")]
    public void AnOrdinaryName_IsUnchanged(string supplied) =>
        Assert.Equal(supplied, UploadPolicy.SafeFileName(supplied));

    /// <summary>
    /// A declared media type is accepted when it is well-formed, and otherwise the
    /// file becomes an opaque stream.
    /// </summary>
    [Theory]
    [InlineData("application/pdf", "application/pdf")]
    [InlineData("TEXT/PLAIN", "text/plain")]
    [InlineData("image/jpeg", "image/jpeg")]
    [InlineData("", "application/octet-stream")]
    [InlineData(null, "application/octet-stream")]
    [InlineData("not a media type", "application/octet-stream")]
    [InlineData("text/html; charset=utf-8\r\nX-Injected: yes", "application/octet-stream")]
    public void AMediaTypeIsAcceptedOrElseOpaque(string? declared, string expected) =>
        Assert.Equal(expected, UploadPolicy.ResolveMediaType(declared));

    /// <summary>
    /// A file is never displayed inline unless its type is one that cannot run.
    /// </summary>
    /// <remarks>
    /// The whole risk is a file claiming to be an image and holding HTML. Rendering
    /// it inline on the application's own origin would run it there (ADR-0025).
    /// </remarks>
    [Theory]
    [InlineData("text/html", false)]
    [InlineData("application/xhtml+xml", false)]
    [InlineData("image/svg+xml", false)]
    [InlineData("application/javascript", false)]
    [InlineData("application/octet-stream", false)]
    [InlineData("application/pdf", true)]
    [InlineData("image/png", true)]
    [InlineData("image/jpeg", true)]
    [InlineData("text/plain", true)]
    public void OnlyHarmlessTypesRenderInline(string mediaType, bool inline) =>
        Assert.Equal(inline, UploadPolicy.MayRenderInline(mediaType));

    /// <summary>A file over the limit is refused before anything is stored.</summary>
    [Fact]
    public void AnOversizeFile_IsRefused()
    {
        DomainException failure = Assert.Throws<DomainException>(
            () => UploadPolicy.RequireAcceptableLength(
                UploadPolicy.MaximumByteLength + 1, UploadPolicy.MaximumByteLength));

        // The message names both numbers, so an operator can see by how much.
        Assert.Contains("209,715,201", failure.Message, StringComparison.Ordinal);
        Assert.Contains("209,715,200", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>An empty file is refused: there is nothing to store.</summary>
    [Fact]
    public void AnEmptyFile_IsRefused() =>
        Assert.Throws<DomainException>(
            () => UploadPolicy.RequireAcceptableLength(0, UploadPolicy.MaximumByteLength));

    /// <summary>
    /// The attachment limit is lower than the upload limit, on purpose.
    /// </summary>
    /// <remarks>
    /// An upload is a person choosing a file. An attachment is whatever somebody
    /// else put in an email, and pulling fifty megabytes out of a mailbox on a
    /// person's say-so is already generous (ADR-0024).
    /// </remarks>
    [Fact]
    public void TheAttachmentLimit_IsLowerThanTheUploadLimit() =>
        Assert.True(
            UploadPolicy.MaximumAttachmentByteLength < UploadPolicy.MaximumByteLength);
}
