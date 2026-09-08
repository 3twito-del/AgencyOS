namespace AgencyOS.Client.Commands;

/// <summary>
/// Raised when a command set contradicts itself.
/// </summary>
/// <remarks>
/// A type of its own so a test can assert the registry refuses a bad set rather
/// than asserting on a message. The registry validates in its constructor, so a
/// contradiction is a startup failure and not a surprise at the keyboard.
/// </remarks>
public sealed class CommandRegistryException : InvalidOperationException
{
    public CommandRegistryException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// The one list of things AgencyOS can be told to do.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Validated at construction.</strong> Duplicate identifiers, duplicate
/// gestures within a scope, malformed gestures and navigation commands with no
/// target are all refused before the shell exists. That is what makes the
/// <c>Ctrl+9</c> collision structurally hard to repeat: the second binding does
/// not lose silently, it fails to build a registry, and the test that constructs
/// the default set fails with it (ADR-0032).
/// </para>
/// <para>
/// A global gesture collides with everything, including page-local gestures,
/// because the window's accelerator sees the key first and the page never gets
/// it. Two page-local gestures collide only within the same page. Encoding that
/// asymmetry is the difference between collision detection that is correct and
/// collision detection that is merely noisy.
/// </para>
/// </remarks>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, CommandDefinition> _byId;
    private readonly Dictionary<CommandGesture, CommandDefinition> _globalGestures;

    public CommandRegistry(IReadOnlyList<CommandDefinition> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        _byId = new Dictionary<string, CommandDefinition>(StringComparer.Ordinal);
        _globalGestures = [];

        // Page-local gestures are keyed by the page that owns them, which is the
        // navigation target of the page they belong to.
        Dictionary<(string Page, CommandGesture Gesture), CommandDefinition> pageGestures = [];

        foreach (CommandDefinition command in commands)
        {
            Validate(command);

            if (!_byId.TryAdd(command.Id, command))
            {
                throw new CommandRegistryException(
                    $"Two commands share the identifier '{command.Id}'. Identifiers are "
                        + "stable and are what bindings and tests refer to.");
            }

            if (!command.HasGesture)
            {
                continue;
            }

            if (command.Scope == CommandScope.Global)
            {
                if (_globalGestures.TryGetValue(command.Gesture, out CommandDefinition? held))
                {
                    throw new CommandRegistryException(
                        $"'{command.Id}' and '{held.Id}' both claim {command.Gesture.Display}. "
                            + "One gesture means one command.");
                }

                _globalGestures[command.Gesture] = command;

                continue;
            }

            (string, CommandGesture) key = (command.Category, command.Gesture);

            if (pageGestures.TryGetValue(key, out CommandDefinition? owner))
            {
                throw new CommandRegistryException(
                    $"'{command.Id}' and '{owner.Id}' both claim {command.Gesture.Display} "
                        + $"within {command.Category}.");
            }

            pageGestures[key] = command;
        }

        // Checked after every global is known, because a page gesture added before
        // the global that shadows it would otherwise pass.
        foreach (((string page, CommandGesture gesture), CommandDefinition command) in pageGestures)
        {
            if (_globalGestures.TryGetValue(gesture, out CommandDefinition? global))
            {
                throw new CommandRegistryException(
                    $"'{command.Id}' claims {gesture.Display} within {page}, but "
                        + $"'{global.Id}' claims it globally and the window sees it first.");
            }
        }

        Commands = [.. commands];
    }

    /// <summary>The default set: every command this build implements.</summary>
    public static CommandRegistry Default { get; } = new(AgencyOsCommands.All);

    /// <summary>Every command, in declaration order.</summary>
    public IReadOnlyList<CommandDefinition> Commands { get; }

    /// <summary>Every command the window installs an accelerator for.</summary>
    public IReadOnlyList<CommandDefinition> GlobalGestures =>
        [.. Commands.Where(x => x.HasGesture && x.Scope == CommandScope.Global)];

    /// <summary>Looks a command up by identifier, or null when it does not exist.</summary>
    public CommandDefinition? Find(string id) =>
        _byId.TryGetValue(id, out CommandDefinition? command) ? command : null;

    /// <summary>The command a global gesture invokes, or null when unbound.</summary>
    public CommandDefinition? ForGesture(CommandGesture gesture) =>
        _globalGestures.TryGetValue(gesture, out CommandDefinition? command) ? command : null;

    /// <summary>What the user may invoke right now.</summary>
    public IReadOnlyList<CommandDefinition> Available(CommandEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return [.. Commands.Where(environment.Satisfies)];
    }

    private static void Validate(CommandDefinition command)
    {
        if (string.IsNullOrWhiteSpace(command.Id))
        {
            throw new CommandRegistryException("A command needs an identifier.");
        }

        if (string.IsNullOrWhiteSpace(command.Label))
        {
            throw new CommandRegistryException($"'{command.Id}' has no label to show.");
        }

        if (command.HasGesture && !command.Gesture.IsWellFormed)
        {
            throw new CommandRegistryException(
                $"'{command.Id}' claims {command.Gesture.Key} with no modifier. Only "
                    + "function keys are bound unmodified; anything else would swallow "
                    + "typing.");
        }

        // An Invoke command may name a workspace or not; a Navigate command must.
        // "Go to Deals" with no destination is the one shape that cannot mean
        // anything.
        if (command.Action == CommandActionKind.Navigate
            && string.IsNullOrWhiteSpace(command.Workspace))
        {
            throw new CommandRegistryException(
                $"'{command.Id}' navigates nowhere. A navigation command names its "
                    + "destination.");
        }
    }
}
