using System.Collections.ObjectModel;

namespace AgencyOS.Client.ViewModels;

/// <summary>One thing the user can do, addressable by name.</summary>
/// <param name="Id">Stable identifier, used by tests and by keyboard bindings.</param>
/// <param name="Title">What the command is called.</param>
/// <param name="Category">Grouping shown beside the title.</param>
/// <param name="Shortcut">Keyboard shortcut, when the command has one.</param>
public sealed record PaletteCommand(string Id, string Title, string Category, string? Shortcut = null);

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

    /// <summary>The commands this build actually implements.</summary>
    /// <remarks>
    /// Only implemented commands are listed. A palette that offers actions the
    /// build cannot perform teaches users to distrust it, so this list grows with
    /// the milestone rather than ahead of it.
    /// </remarks>
    public static IReadOnlyList<PaletteCommand> DefaultCommands() =>
    [
        new("go.command-center", "Go to Command Center", "Navigate", "Ctrl+1"),
        new("go.people", "Go to People", "Navigate", "Ctrl+2"),
        new("go.companies", "Go to Companies", "Navigate", "Ctrl+3"),
        new("go.saved-views", "Go to Saved Views", "Navigate", "Ctrl+4"),
        new("go.sync", "Go to Sync and Offline", "Navigate", "Ctrl+5"),
        new("search.open", "Search everything", "Find", "Ctrl+K"),
        new("person.create", "New person", "Create", "Ctrl+N"),
        new("company.create", "New company", "Create"),
        new("interaction.record", "Record interaction", "Capture", "Ctrl+I"),
        new("task.create", "New task", "Create", "Ctrl+T"),
        new("relationship.create", "Connect two parties", "Create"),
        new("view.save", "Save current view", "View"),
        new("sync.now", "Synchronize now", "Sync", "F9"),
        new("cache.reset", "Reset local cache", "Sync"),
        new("view.refresh", "Refresh", "View", "F5"),
    ];
}
