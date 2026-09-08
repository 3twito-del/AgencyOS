using System.Collections.ObjectModel;
using AgencyOS.Client.Commands;

namespace AgencyOS.Client.ViewModels;

/// <summary>
/// One thing the user can do, as the palette shows it.
/// </summary>
/// <remarks>
/// A projection of <see cref="CommandDefinition"/> rather than a second list.
/// Before M13 the palette held its own entries, which is how it came to advertise
/// twenty-six commands nothing dispatched and thirteen shortcuts the window never
/// installed (ADR-0032).
/// </remarks>
/// <param name="Id">Stable identifier, used by tests and by keyboard bindings.</param>
/// <param name="Title">What the command is called.</param>
/// <param name="Category">Grouping shown beside the title.</param>
/// <param name="Shortcut">Keyboard shortcut, when the command has one.</param>
public sealed record PaletteCommand(string Id, string Title, string Category, string? Shortcut = null)
{
    /// <summary>Projects a registry command onto what the palette renders.</summary>
    public static PaletteCommand From(CommandDefinition command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return new PaletteCommand(
            command.Id,
            command.Label,
            command.Category,
            command.HasGesture ? command.Gesture.Display : null);
    }
}

/// <summary>
/// Keyboard-first access to every implemented command.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/12_WINDOWS_NATIVE.md</c> asks for a universal command palette and
/// keyboard-first operation. This holds the command set and the matching rule; the
/// WinUI layer supplies the surface and the invocation.
/// </para>
/// <para>
/// Only commands that actually exist are listed. A palette that offers actions the
/// build cannot perform teaches users to distrust it.
/// </para>
/// </remarks>
public sealed class CommandPaletteViewModel : ViewModelBase
{
    private readonly List<PaletteCommand> _all;
    private string _query = string.Empty;
    private PaletteCommand? _selected;

    public CommandPaletteViewModel()
        : this(DefaultCommands())
    {
    }

    public CommandPaletteViewModel(IReadOnlyList<PaletteCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        _all = [.. commands];
        Results = [.. _all];
        _selected = Results.Count > 0 ? Results[0] : null;
    }

    public ObservableCollection<PaletteCommand> Results { get; }

    public override bool IsEmpty => Results.Count == 0;

    /// <summary>Gets every command, filtered or not.</summary>
    public IReadOnlyList<PaletteCommand> AllCommands => _all;

    public string Query
    {
        get => _query;
        set
        {
            if (Set(ref _query, value))
            {
                Filter();
            }
        }
    }

    public PaletteCommand? Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    /// <summary>Moves the selection, wrapping at both ends.</summary>
    /// <remarks>
    /// Wrapping matters for a keyboard surface: reaching the end of a short list
    /// and having the arrow key stop responding reads as a bug.
    /// </remarks>
    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            Selected = null;
            return;
        }

        int current = _selected is null ? -1 : Results.IndexOf(_selected);
        int next = current < 0 ? 0 : (current + delta) % Results.Count;

        if (next < 0)
        {
            next += Results.Count;
        }

        Selected = Results[next];
    }

    private void Filter()
    {
        string query = _query.Trim();

        IEnumerable<PaletteCommand> matches = string.IsNullOrEmpty(query)
            ? _all
            : _all.Where(c =>
                c.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || c.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
                || c.Id.Contains(query, StringComparison.OrdinalIgnoreCase));

        Results.Clear();

        foreach (PaletteCommand command in matches)
        {
            Results.Add(command);
        }

        Selected = Results.Count > 0 ? Results[0] : null;
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>
    /// The commands this build actually implements.
    /// </summary>
    /// <remarks>
    /// Read from <see cref="CommandRegistry"/>, which is validated at construction
    /// and shared with the window's accelerators. The palette can no longer offer
    /// something the build does not dispatch, because there is only one list
    /// (ADR-0032).
    /// </remarks>
    public static IReadOnlyList<PaletteCommand> DefaultCommands() =>
        [.. CommandRegistry.Default.Commands.Select(PaletteCommand.From)];
}
