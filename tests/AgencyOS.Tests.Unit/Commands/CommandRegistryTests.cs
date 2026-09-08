using AgencyOS.Client.Commands;
using Xunit;

namespace AgencyOS.Tests.Unit.Commands;

/// <summary>
/// The guarantees that make a keyboard shortcut mean one thing.
/// </summary>
/// <remarks>
/// <para>
/// M12 shipped a bug where <c>Ctrl+9</c> silently changed meaning, because
/// accelerators addressed the navigation menu by position and inserting an item
/// shifted every index below it. The registry replaces the index with a
/// destination tag and refuses contradictions at construction, so the equivalent
/// mistake now fails here rather than under somebody's fingers (ADR-0032).
/// </para>
/// <para>
/// The tests over the real command set matter more than the tests over synthetic
/// ones. A registry that validates correctly and is never given the actual list
/// proves nothing.
/// </para>
/// </remarks>
public sealed class CommandRegistryTests
{
    private static readonly CommandRegistry Registry = CommandRegistry.Default;

    // ------------------------------------------------- the real command set

    /// <summary>
    /// The shipped set builds. Every guarantee below is checked at construction,
    /// so this one test failing is how a contradiction announces itself.
    /// </summary>
    [Fact]
    public void TheDefaultRegistryIsCoherent()
    {
        Assert.NotEmpty(Registry.Commands);
    }

    [Fact]
    public void EveryIdentifierIsUnique()
    {
        List<string> duplicates =
        [
            .. Registry.Commands
                .GroupBy(x => x.Id, StringComparer.Ordinal)
                .Where(x => x.Count() > 1)
                .Select(x => x.Key),
        ];

        Assert.Empty(duplicates);
    }

    /// <summary>One gesture, one command.</summary>
    [Fact]
    public void EveryGlobalGestureIsUnique()
    {
        List<string> duplicates =
        [
            .. Registry.Commands
                .Where(x => x.HasGesture && x.Scope == CommandScope.Global)
                .GroupBy(x => x.Gesture)
                .Where(x => x.Count() > 1)
                .Select(x => $"{x.Key.Display}: {string.Join(", ", x.Select(c => c.Id))}"),
        ];

        Assert.Empty(duplicates);
    }

    /// <summary>
    /// No gesture swallows typing.
    /// </summary>
    /// <remarks>
    /// A bare letter with no modifier would fire inside every text box on the
    /// page. Function keys are the only unmodified gestures AgencyOS binds.
    /// </remarks>
    [Fact]
    public void EveryGestureIsWellFormed()
    {
        Assert.All(
            Registry.Commands.Where(x => x.HasGesture),
            x => Assert.True(
                x.Gesture.IsWellFormed,
                $"'{x.Id}' claims the malformed gesture {x.Gesture.Key}."));
    }

    /// <summary>
    /// Every navigation command names a workspace that exists.
    /// </summary>
    /// <remarks>
    /// The destination tag is what replaced the menu index. A tag nothing answers
    /// is the same failure in a new costume, so the list of real workspaces is
    /// asserted here rather than trusted.
    /// </remarks>
    [Fact]
    public void EveryWorkspaceReferenceResolves()
    {
        Assert.All(
            Registry.Commands.Where(x => x.Workspace is not null),
            x => Assert.True(
                AgencyOsWorkspaces.Tags.Contains(x.Workspace!),
                $"'{x.Id}' names the workspace '{x.Workspace}', which does not exist."));
    }

    [Fact]
    public void EveryNavigationCommandHasADestination()
    {
        Assert.All(
            Registry.Commands.Where(x => x.Action == CommandActionKind.Navigate),
            x => Assert.False(string.IsNullOrWhiteSpace(x.Workspace)));
    }

    /// <summary>Every workspace is reachable by name.</summary>
    [Fact]
    public void EveryWorkspaceHasANavigationCommand()
    {
        HashSet<string> destinations = Registry.Commands
            .Where(x => x.Action == CommandActionKind.Navigate)
            .Select(x => x.Workspace!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(
            AgencyOsWorkspaces.Tags,
            tag => Assert.Contains(tag, destinations));
    }

    /// <summary>Nothing is advertised that cannot be shown.</summary>
    [Fact]
    public void EveryCommandHasALabelAndACategory()
    {
        Assert.All(Registry.Commands, x =>
        {
            Assert.False(string.IsNullOrWhiteSpace(x.Label));
            Assert.False(string.IsNullOrWhiteSpace(x.Category));
        });
    }

    /// <summary>Lookup by identifier and by gesture agree with the list.</summary>
    [Fact]
    public void LookupsAgreeWithTheList()
    {
        foreach (CommandDefinition command in Registry.Commands)
        {
            Assert.Same(command, Registry.Find(command.Id));

            if (command is { HasGesture: true, Scope: CommandScope.Global })
            {
                Assert.Same(command, Registry.ForGesture(command.Gesture));
            }
        }

        Assert.Null(Registry.Find("no.such.command"));
        Assert.Null(Registry.ForGesture(CommandGesture.ControlShift(CommandKey.Z)));
    }

    // ------------------------------------------------------ the refusals

    /// <summary>
    /// The Ctrl+9 case, as a test.
    /// </summary>
    /// <remarks>
    /// Two commands claiming one gesture is refused when the registry is built,
    /// which is what makes the M12 collision impossible to reintroduce quietly.
    /// </remarks>
    [Fact]
    public void ADuplicateGestureIsRefused()
    {
        CommandRegistryException error = Assert.Throws<CommandRegistryException>(() =>
            new CommandRegistry(
            [
                new("go.saved-views", "Saved Views", "Navigate", CommandActionKind.Navigate,
                    Workspace: "saved-views", Gesture: CommandGesture.Control(CommandKey.D9)),
                new("go.ai", "AI", "Navigate", CommandActionKind.Navigate,
                    Workspace: "ai", Gesture: CommandGesture.Control(CommandKey.D9)),
            ]));

        Assert.Contains("Ctrl+9", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADuplicateIdentifierIsRefused()
    {
        Assert.Throws<CommandRegistryException>(() =>
            new CommandRegistry(
            [
                new("go.people", "People", "Navigate", CommandActionKind.Navigate,
                    Workspace: "people"),
                new("go.people", "Also people", "Navigate", CommandActionKind.Navigate,
                    Workspace: "people"),
            ]));
    }

    /// <summary>
    /// A page gesture shadowed by a global one is refused.
    /// </summary>
    /// <remarks>
    /// The window's accelerator fires first and the page never sees the key, so a
    /// page binding under a global one is dead rather than merely lower priority.
    /// </remarks>
    [Fact]
    public void APageGestureShadowedByAGlobalOneIsRefused()
    {
        CommandRegistryException error = Assert.Throws<CommandRegistryException>(() =>
            new CommandRegistry(
            [
                new("deals.local", "Something local", "Deals", CommandActionKind.Invoke,
                    Workspace: "deals", Gesture: CommandGesture.Control(CommandKey.K),
                    Scope: CommandScope.Page),
                new("search.open", "Search", "Find", CommandActionKind.Invoke,
                    Gesture: CommandGesture.Control(CommandKey.K)),
            ]));

        Assert.Contains("globally", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Two page gestures on different pages do not collide.</summary>
    [Fact]
    public void PageGesturesOnDifferentPagesCoexist()
    {
        CommandRegistry registry = new(
        [
            new("deals.thing", "Deal thing", "Deals", CommandActionKind.Invoke,
                Workspace: "deals", Gesture: CommandGesture.ControlShift(CommandKey.Z),
                Scope: CommandScope.Page),
            new("finance.thing", "Finance thing", "Finance", CommandActionKind.Invoke,
                Workspace: "finance", Gesture: CommandGesture.ControlShift(CommandKey.Z),
                Scope: CommandScope.Page),
        ]);

        Assert.Equal(2, registry.Commands.Count);
    }

    [Fact]
    public void AnUnmodifiedLetterIsRefused()
    {
        Assert.Throws<CommandRegistryException>(() =>
            new CommandRegistry(
            [
                new("bad", "Bad", "Navigate", CommandActionKind.Invoke,
                    Gesture: new CommandGesture(CommandKey.P, CommandModifiers.None)),
            ]));
    }

    [Fact]
    public void ANavigationCommandWithNoDestinationIsRefused()
    {
        Assert.Throws<CommandRegistryException>(() =>
            new CommandRegistry(
            [new("go.nowhere", "Nowhere", "Navigate", CommandActionKind.Navigate)]));
    }

    // -------------------------------------------------------- availability

    /// <summary>A command needing a record is hidden until one is selected.</summary>
    [Fact]
    public void ContextRequirementsHideCommands()
    {
        CommandRegistry registry = new(
        [
            new("always", "Always", "View", CommandActionKind.Invoke),
            new("needs.deal", "Needs a deal", "Deals", CommandActionKind.Invoke,
                Workspace: "deals", RequiresContext: CommandContextKind.Deal),
        ]);

        Assert.Single(registry.Available(new CommandEnvironment()));
        Assert.Equal(
            2,
            registry.Available(new CommandEnvironment(CommandContextKind.Deal)).Count);
    }

    /// <summary>
    /// Unknown permissions hide nothing.
    /// </summary>
    /// <remarks>
    /// Before the client has learned the caller's grants, guessing would hide
    /// commands they do hold. The server refuses what it must; the client's job
    /// is not to pre-empt it wrongly.
    /// </remarks>
    [Fact]
    public void UnknownPermissionsHideNothing()
    {
        CommandRegistry registry = new(
        [
            new("needs.grant", "Needs a grant", "Finance", CommandActionKind.Invoke,
                Workspace: "finance", RequiredPermission: "finance.write"),
        ]);

        Assert.Single(registry.Available(new CommandEnvironment()));

        Assert.Empty(registry.Available(
            new CommandEnvironment(Permissions: new HashSet<string>(StringComparer.Ordinal))));

        Assert.Single(registry.Available(new CommandEnvironment(
            Permissions: new HashSet<string>(["finance.write"], StringComparer.Ordinal))));
    }

    // ------------------------------------------------------------ display

    [Theory]
    [InlineData(CommandKey.D9, CommandModifiers.Control, "Ctrl+9")]
    [InlineData(CommandKey.K, CommandModifiers.Control | CommandModifiers.Shift, "Ctrl+Shift+K")]
    [InlineData(CommandKey.F7, CommandModifiers.None, "F7")]
    public void GesturesRenderAsPeopleWriteThem(
        CommandKey key, CommandModifiers modifiers, string expected)
    {
        Assert.Equal(expected, new CommandGesture(key, modifiers).Display);
    }

    [Fact]
    public void AnAbsentGestureRendersAsNothing()
    {
        Assert.Equal(string.Empty, default(CommandGesture).Display);
        Assert.False(default(CommandGesture).IsWellFormed);
    }
}
