using System.Text;
using AgencyOS.Application.Abstractions;
using AgencyOS.Domain.Documents;
using AgencyOS.Infrastructure.Storage;
using Xunit;

namespace AgencyOS.Tests.Integration.Storage;

/// <summary>
/// What the text projection will and will not claim to have read.
/// </summary>
/// <remarks>
/// <para>
/// The extractor is deliberately strict: bytes that are not valid UTF-8 are a file
/// claiming a text media type while holding something else, and substituting
/// replacement characters would present a page of rubbish as the document's text.
/// </para>
/// <para>
/// M14 found a hole in that reasoning. <strong>A NUL byte is valid UTF-8</strong>,
/// so it passed the strict decode, and PostgreSQL cannot store NUL in a text
/// column — <c>22021: invalid byte sequence for encoding "UTF8": 0x00</c>. The
/// whole ingestion failed with a 500 and the document did not file at all, because
/// its search projection was unstorable (ADR-0037).
/// </para>
/// <para>
/// Found by an upload test that happened to send zero bytes. It would have been
/// found in production by the first UTF-16 file somebody saved as .txt.
/// </para>
/// </remarks>
public sealed class TextExtractionTests
{
    private static readonly IDocumentTextExtractor Extractor = new PlainTextDocumentExtractor();

    /// <summary>Ordinary text is read.</summary>
    [Fact]
    public async Task PlainTextIsExtracted()
    {
        DocumentTextExtraction result = await Extractor.ExtractAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("The agreed terms, in writing.")),
            "text/plain");

        Assert.Equal(TextExtractionState.Extracted, result.State);
        Assert.Equal("The agreed terms, in writing.", result.Text);
    }

    /// <summary>
    /// Content carrying NUL is refused, not stored and not stripped.
    /// </summary>
    /// <remarks>
    /// Refusing keeps faith with the rule the extractor already followed for
    /// invalid UTF-8. Stripping the NULs from a UTF-16 file would leave every
    /// second byte of a text nobody wrote, indexed as though somebody had.
    /// </remarks>
    [Theory]
    [InlineData(new byte[] { 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0x48, 0x00, 0x69, 0x00 })]
    public async Task ContentContainingNulIsRefused(byte[] content)
    {
        DocumentTextExtraction result = await Extractor.ExtractAsync(
            new MemoryStream(content), "text/plain");

        Assert.Equal(TextExtractionState.Failed, result.State);
        Assert.Null(result.Text);
        Assert.Contains("NUL", result.Detail!, StringComparison.Ordinal);
    }

    /// <summary>Bytes that are not UTF-8 at all are still refused.</summary>
    /// <remarks>
    /// The rule that already held. Asserted here so the NUL guard cannot be
    /// mistaken for the only thing standing between the database and a bad file.
    /// </remarks>
    [Fact]
    public async Task InvalidUtf8IsRefused()
    {
        DocumentTextExtraction result = await Extractor.ExtractAsync(
            new MemoryStream([0xC3, 0x28, 0xA0, 0xA1]), "text/plain");

        Assert.Equal(TextExtractionState.Failed, result.State);
        Assert.Null(result.Text);
    }

    /// <summary>An empty file extracts to nothing, successfully.</summary>
    [Fact]
    public async Task EmptyContentExtractsToEmptyText()
    {
        DocumentTextExtraction result = await Extractor.ExtractAsync(
            new MemoryStream([]), "text/plain");

        Assert.Equal(TextExtractionState.Extracted, result.State);
        Assert.Equal(string.Empty, result.Text);
    }

    /// <summary>A format this build cannot read says so rather than guessing.</summary>
    [Fact]
    public async Task AnUnsupportedFormatIsNamedUnsupported()
    {
        DocumentTextExtraction result = await Extractor.ExtractAsync(
            new MemoryStream([1, 2, 3]), "application/pdf");

        Assert.Equal(TextExtractionState.Unsupported, result.State);
    }
}
