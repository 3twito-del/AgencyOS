namespace AgencyOS.Client.Commands;

/// <summary>
/// Where a gesture is listened for, and therefore what it can collide with.
/// </summary>
/// <remarks>
/// Scope is what makes collision detection meaningful rather than merely strict.
/// Two page-local gestures on different pages are not in conflict; a global
/// gesture conflicts with everything, including a page-local one, because the
/// window sees it first.
/// </remarks>
public enum CommandScope
{
    /// <summary>Reachable from anywhere. The window installs the accelerator.</summary>
    Global = 1,

    /// <summary>Reachable only while its page is showing.</summary>
    Page = 2,
}

/// <summary>What kind of thing a command needs before it can run.</summary>
/// <remarks>
/// Named rather than a free predicate so the requirement is data the registry can
/// validate and a test can enumerate. A <see cref="Func{T, TResult}"/> would be
/// more flexible and could not be checked for completeness.
/// </remarks>
public enum CommandContextKind
{
    /// <summary>Always available.</summary>
    None = 0,

    Person,
    Company,
    Deal,
    Contract,
    ResearchCase,
    AgentRun,
    Approval,
    Document,
    SavedView,
}

/// <summary>What invoking a command does.</summary>
public enum CommandActionKind
{
    /// <summary>Moves to a workspace named by <c>Workspace</c>.</summary>
    Navigate = 1,

    /// <summary>Runs something on a workspace, navigating there first if needed.</summary>
    Invoke = 2,
}

/// <summary>
/// One thing the user can do, defined once.
/// </summary>
/// <remarks>
/// <para>
/// Before M13 a command existed in up to three places: a palette entry, an
/// accelerator installed in <c>MainWindow</c>, and a string compared inside a
/// page's <c>Execute</c>. Nothing tied them together, so a palette could advertise
/// a shortcut the window did not install, and two accelerators could claim one
/// gesture — which is exactly how <c>Ctrl+9</c> came to mean two different things
/// (ADR-0032).
/// </para>
/// <para>
/// <paramref name="RequiredPermission"/> is advisory for the client only. It hides
/// a command the caller certainly cannot use; it decides nothing. Authorization is
/// the server's, and a command that reaches it without the grant is refused there.
/// </para>
/// </remarks>
/// <param name="Id">Stable identifier. Never renamed; tests and bindings use it.</param>
/// <param name="Workspace">
/// The navigation tag this command belongs to. For
/// <see cref="CommandActionKind.Navigate"/> it is the destination and is required.
/// For <see cref="CommandActionKind.Invoke"/> it names the workspace that answers
/// the command, so invoking it from anywhere navigates there first; null means any
/// workspace answers it, and it is dispatched to whichever one is showing.
/// </param>
public sealed record CommandDefinition(
    string Id,
    string Label,
    string Category,
    CommandActionKind Action,
    string? Workspace = null,
    CommandGesture Gesture = default,
    CommandScope Scope = CommandScope.Global,
    CommandContextKind RequiresContext = CommandContextKind.None,
    string? RequiredPermission = null)
{
    /// <summary>Whether the command carries a keyboard gesture.</summary>
    public bool HasGesture => Gesture.Key != CommandKey.None;
}

/// <summary>
/// What the user is currently looking at, and what they may do.
/// </summary>
/// <remarks>
/// Supplied by the shell rather than read from ambient state, so a test can put a
/// command in any situation without standing up a window.
/// </remarks>
/// <param name="Permissions">
/// The caller's grants, or null when the client has not yet learned them — in
/// which case nothing is hidden, because guessing would hide commands the user
/// does hold.
/// </param>
public sealed record CommandEnvironment(
    CommandContextKind ActiveContext = CommandContextKind.None,
    IReadOnlySet<string>? Permissions = null)
{
    /// <summary>Whether this environment satisfies a command's requirements.</summary>
    public bool Satisfies(CommandDefinition command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.RequiresContext != CommandContextKind.None
            && command.RequiresContext != ActiveContext)
        {
            return false;
        }

        return command.RequiredPermission is not { } permission
            || Permissions is null
            || Permissions.Contains(permission);
    }
}
