using AgencyOS.Client.ViewModels;
using Xunit;

namespace AgencyOS.Tests.Unit.Client;

/// <summary>
/// The palette offers document and communication commands in words AgencyOS can
/// honour.
/// </summary>
/// <remarks>
/// A palette entry is a promise, and M10 is the first milestone where one of them
/// causes something irreversible outside the database. "Send" appears exactly
/// where a message actually leaves and nowhere else, and no document command
/// claims to delete anything, because archiving destroys nothing (ADR-0024,
/// ADR-0028).
/// </remarks>
public sealed class DocumentPaletteTests
{
    [Fact]
    public void TheDocumentAndCommunicationCommands_AreOffered()
    {
        IReadOnlyList<PaletteCommand> commands = CommandPaletteViewModel.DefaultCommands();

        foreach (string id in new[]
        {
            "go.documents",
            "go.communications",
            "document.record",
            "document.version.add",
            "document.download",
            "document.link",
            "document.archive",
            "document.restore",
            "mailbox.connect",
            "mailbox.visibility",
            "message.link",
            "message.participant.resolve",
            "attachment.ingest",
            "message.compose",
            "message.queue",
            "message.cancel",
            "go.outbound.unknown",
            "go.communications.command-center",
        })
        {
            Assert.Contains(commands, x => x.Id == id);
        }
    }

    /// <summary>
    /// No document command claims to delete anything, because none of them does.
    /// </summary>
    [Fact]
    public void NoDocumentCommand_ClaimsToDelete()
    {
        IEnumerable<PaletteCommand> documents = CommandPaletteViewModel.DefaultCommands()
            .Where(x => x.Category == "Documents");

        foreach (PaletteCommand command in documents)
        {
            Assert.DoesNotContain("Delete", command.Title, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Destroy", command.Title, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Purge", command.Title, StringComparison.OrdinalIgnoreCase);

            // And none of them claims to have checked the file.
            Assert.DoesNotContain("Scan", command.Title, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Verify", command.Title, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Adding bytes to an existing document is called adding a version.
    /// </summary>
    /// <remarks>
    /// "Update document" would describe the wrong operation. The bytes of a version
    /// are never replaced, and a command that implied otherwise would invite
    /// somebody to expect the old draft to be gone.
    /// </remarks>
    [Fact]
    public void AddingBytes_IsCalledAddingAVersion()
    {
        PaletteCommand add = CommandPaletteViewModel.DefaultCommands()
            .Single(x => x.Id == "document.version.add");

        Assert.Contains("version", add.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Replace", add.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Overwrite", add.Title, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Composing does not say "send", and queueing does.
    /// </summary>
    /// <remarks>
    /// The distinction the whole send protocol rests on. Composing writes a draft;
    /// queueing is the point after which AgencyOS cannot take the message back
    /// (ADR-0028).
    /// </remarks>
    [Fact]
    public void OnlyTheCommandThatSendsSaysSend()
    {
        IReadOnlyList<PaletteCommand> commands = CommandPaletteViewModel.DefaultCommands();

        PaletteCommand compose = commands.Single(x => x.Id == "message.compose");
        PaletteCommand queue = commands.Single(x => x.Id == "message.queue");

        Assert.DoesNotContain("Send", compose.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sending", queue.Title, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// No communication command offers to unsend, recall or delete a message.
    /// </summary>
    [Fact]
    public void NoCommunicationCommand_OffersToUnsendAnything()
    {
        IEnumerable<PaletteCommand> communications = CommandPaletteViewModel.DefaultCommands()
            .Where(x => x.Category == "Communications");

        foreach (PaletteCommand command in communications)
        {
            Assert.DoesNotContain("Unsend", command.Title, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Recall", command.Title, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Delete", command.Title, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Archive mail", command.Title, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Identifying an address is not called "match" or "resolve automatically".
    /// </summary>
    [Fact]
    public void IdentifyingAnAddress_IsNamedAsAJudgment()
    {
        PaletteCommand resolve = CommandPaletteViewModel.DefaultCommands()
            .Single(x => x.Id == "message.participant.resolve");

        Assert.DoesNotContain("Match", resolve.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Auto", resolve.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Detect", resolve.Title, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Every command has a unique identifier and a shortcut used once.</summary>
    /// <remarks>
    /// A duplicate identifier makes one of the two commands unreachable, and a
    /// duplicate shortcut makes the keyboard ambiguous. Both are silent.
    /// </remarks>
    [Fact]
    public void EveryCommandIsUniquelyAddressable()
    {
        IReadOnlyList<PaletteCommand> commands = CommandPaletteViewModel.DefaultCommands();

        Assert.Equal(commands.Count, commands.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count());

        string[] shortcuts =
        [
            .. commands.Where(x => x.Shortcut is not null).Select(x => x.Shortcut!),
        ];

        Assert.Equal(shortcuts.Length, shortcuts.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
