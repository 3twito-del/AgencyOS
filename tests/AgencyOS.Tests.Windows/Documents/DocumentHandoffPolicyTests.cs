using AgencyOS.Windows.Platform.Documents;
using Xunit;

namespace AgencyOS.Tests.Windows.Documents;

/// <summary>
/// What may be copied onto a workstation, and what it is not.
/// </summary>
/// <remarks>
/// M10 remains the only canonical document layer. Everything here concerns a
/// copy, which AgencyOS can neither version nor recall — so the tests are mostly
/// about what the copy is refused, where it is allowed to go, and the fact that a
/// path is never an identity (§U, ADR-0034).
/// </remarks>
public sealed class DocumentHandoffPolicyTests
{
    /// <summary>
    /// Restricted material is never copied to a machine.
    /// </summary>
    /// <remarks>
    /// The classification an organization uses to mean "this does not leave", and
    /// a file on a laptop is leaving. The same reasoning keeps Restricted
    /// unreachable for model inference at every residency.
    /// </remarks>
    [Fact]
    public void RestrictedMaterialIsNeverMaterialized()
    {
        Assert.Null(DocumentHandoffPolicy.Plan(
            HandoffSensitivity.Restricted, "Board minutes", ".pdf"));

        Assert.NotEmpty(DocumentHandoffPolicy.Refusal(HandoffSensitivity.Restricted));
    }

    /// <summary>The refusal explains the classification, not a permission.</summary>
    /// <remarks>
    /// The reader may well be allowed to read it inside AgencyOS. What they cannot
    /// do is take it with them, and the wording says so rather than implying they
    /// lack access.
    /// </remarks>
    [Fact]
    public void TheRefusalSaysTheReaderMayStillRead()
    {
        Assert.Contains(
            "read it here",
            DocumentHandoffPolicy.Refusal(HandoffSensitivity.Restricted),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sensitive material is not handed to another application.
    /// </summary>
    /// <remarks>
    /// Open-With means a second process holds the bytes and decides for itself
    /// where they go next, which is a disclosure AgencyOS cannot describe
    /// afterwards.
    /// </remarks>
    [Theory]
    [InlineData(HandoffSensitivity.Privileged, false)]
    [InlineData(HandoffSensitivity.Financial, false)]
    [InlineData(HandoffSensitivity.Confidential, true)]
    [InlineData(HandoffSensitivity.Internal, true)]
    public void OnlyOrdinaryMaterialMayBeOpenedElsewhere(
        HandoffSensitivity sensitivity, bool expected)
    {
        HandoffPlan plan = DocumentHandoffPolicy.Plan(sensitivity, "Draft", ".docx")!;

        Assert.Equal(expected, plan.MayOpenWith);
    }

    /// <summary>Sensitive copies live materially less long.</summary>
    [Theory]
    [InlineData(HandoffSensitivity.Privileged)]
    [InlineData(HandoffSensitivity.Financial)]
    public void SensitiveCopiesExpireSooner(HandoffSensitivity sensitivity)
    {
        HandoffPlan sensitive = DocumentHandoffPolicy.Plan(sensitivity, "Advice", ".pdf")!;
        HandoffPlan ordinary = DocumentHandoffPolicy.Plan(
            HandoffSensitivity.Internal, "Notes", ".pdf")!;

        Assert.True(sensitive.DeleteAfter < ordinary.DeleteAfter);
        Assert.Equal(DocumentHandoffPolicy.SensitiveLifetime, sensitive.DeleteAfter);
    }

    /// <summary>
    /// Every copy lands in a folder AgencyOS owns.
    /// </summary>
    /// <remarks>
    /// Not the shell's temp root and not Downloads. A folder AgencyOS owns can be
    /// enumerated and swept; a shared one contains everybody's files and cannot be
    /// cleaned safely.
    /// </remarks>
    [Theory]
    [InlineData(HandoffSensitivity.Internal)]
    [InlineData(HandoffSensitivity.Confidential)]
    [InlineData(HandoffSensitivity.Privileged)]
    [InlineData(HandoffSensitivity.Financial)]
    public void EveryCopyLandsInAgencyOsOwnFolder(HandoffSensitivity sensitivity)
    {
        HandoffPlan plan = DocumentHandoffPolicy.Plan(sensitivity, "Anything", ".pdf")!;

        Assert.Equal(DocumentHandoffPolicy.Root, plan.Directory);
        Assert.True(DocumentHandoffPolicy.IsMaterialized(plan.Path));
    }

    /// <summary>
    /// A path outside the materialization folder is not one of ours.
    /// </summary>
    /// <remarks>
    /// Checked before anything is deleted. A sweeper that trusted a stored path
    /// could be pointed at somebody's documents folder by a corrupted record.
    /// </remarks>
    [Theory]
    [InlineData(@"C:\Users\Someone\Documents\contract.pdf")]
    [InlineData(@"C:\Windows\System32\drivers\etc\hosts")]
    public void APathOutsideTheFolderIsNotOurs(string path)
    {
        Assert.False(DocumentHandoffPolicy.IsMaterialized(path));
    }

    /// <summary>
    /// A traversal in the title cannot escape the folder.
    /// </summary>
    /// <remarks>
    /// A document title is text somebody typed, and this is the same class of
    /// problem M10 solved for stored content arriving at a different layer.
    /// </remarks>
    [Theory]
    [InlineData(@"..\..\..\Windows\System32\evil")]
    [InlineData("../../etc/passwd")]
    [InlineData(@"C:\absolute\path")]
    [InlineData("sub/dir/name")]
    public void ATitleCannotEscapeTheFolder(string title)
    {
        HandoffPlan plan = DocumentHandoffPolicy.Plan(
            HandoffSensitivity.Internal, title, ".pdf")!;

        Assert.DoesNotContain('/', plan.FileName);
        Assert.DoesNotContain('\\', plan.FileName);
        Assert.True(DocumentHandoffPolicy.IsMaterialized(plan.Path));
    }

    /// <summary>A reserved device name stops being one.</summary>
    /// <remarks>
    /// Windows resolves CON.txt to the console, so the extension does not save it.
    /// </remarks>
    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    public void AReservedDeviceNameIsDefused(string title)
    {
        string name = DocumentHandoffPolicy.SafeName(title, ".txt");

        Assert.StartsWith("_", name, StringComparison.Ordinal);
    }

    /// <summary>A name of nothing but dots and spaces becomes a name.</summary>
    /// <remarks>
    /// On Windows such a name resolves to the directory itself, and a leading dot
    /// hides the file.
    /// </remarks>
    [Theory]
    [InlineData("...")]
    [InlineData("   ")]
    [InlineData(". . .")]
    [InlineData("")]
    public void ADegenerateTitleStillProducesAFile(string title)
    {
        string name = DocumentHandoffPolicy.SafeName(title, ".pdf");

        Assert.StartsWith("document", name, StringComparison.Ordinal);
        Assert.Equal("document.pdf", name);
    }

    /// <summary>Control characters never reach the filesystem.</summary>
    [Fact]
    public void ControlCharactersAreReplaced()
    {
        string name = DocumentHandoffPolicy.SafeName("Con\0tract\nDraft", ".pdf");

        Assert.DoesNotContain('\0', name);
        Assert.DoesNotContain('\n', name);
    }

    /// <summary>A long title is bounded, because a path is.</summary>
    [Fact]
    public void ALongTitleIsBounded()
    {
        string name = DocumentHandoffPolicy.SafeName(new string('x', 500), ".pdf");

        Assert.True(name.Length <= 101);
    }

    /// <summary>The extension is sanitized too.</summary>
    /// <remarks>
    /// An extension arriving from a stored record is no more trustworthy than the
    /// title, and ".pdf.exe" or a traversal inside it would be worse.
    /// </remarks>
    [Theory]
    [InlineData(".pdf", "Contract.pdf")]
    [InlineData("pdf", "Contract.pdf")]
    [InlineData(".p df", "Contract.pdf")]
    [InlineData("", "Contract")]
    [InlineData(@"..\exe", "Contract.exe")]
    public void TheExtensionIsSanitized(string extension, string expected)
    {
        Assert.Equal(expected, DocumentHandoffPolicy.SafeName("Contract", extension));
    }

    /// <summary>
    /// The plan carries no document identity.
    /// </summary>
    /// <remarks>
    /// The property M10 depends on: a canonical document is a version in the
    /// content store addressed by SHA-256, and a path on a workstation is a copy
    /// of it. A plan that carried an identifier would invite somebody to treat the
    /// file as the record.
    /// </remarks>
    [Fact]
    public void ThePlanIsAPathAndNotAnIdentity()
    {
        Assert.DoesNotContain(
            typeof(HandoffPlan).GetProperties(),
            x => x.Name.Contains("Id", StringComparison.Ordinal)
                || x.Name.Contains("Version", StringComparison.Ordinal)
                || x.Name.Contains("Hash", StringComparison.Ordinal));
    }
}
