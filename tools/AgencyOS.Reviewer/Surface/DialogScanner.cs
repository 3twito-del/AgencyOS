using System.IO;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AgencyOS.Reviewer.Surface;

/// <summary>One input a dialog asks a person to fill in.</summary>
/// <param name="Name">The control's <c>x:Name</c>.</param>
/// <param name="ControlType">The XAML element.</param>
/// <param name="Label">Header, content or placeholder — what the user reads.</param>
/// <param name="Optional">Whether the code-behind treats an empty value as acceptable.</param>
/// <param name="TakesRawIdentifier">Whether the value is parsed as a <see cref="Guid"/>.</param>
/// <param name="Choices">Fixed choices, for a selector that declares them in markup.</param>
public sealed record DialogField(
    string Name,
    string ControlType,
    string? Label,
    bool Optional,
    bool TakesRawIdentifier,
    IReadOnlyList<string> Choices);

/// <summary>How a dialog is opened from the running application.</summary>
/// <param name="Page">The page that constructs it.</param>
/// <param name="Workspace">The workspace tag that page is reached by.</param>
/// <param name="Method">The method that constructs and shows it.</param>
/// <param name="Handler">The event handler that calls that method.</param>
/// <param name="Control">The control's <c>x:Name</c>, when it has one.</param>
/// <param name="ControlLabel">What that control says — how a person finds it.</param>
/// <param name="HasControl">Whether a control wired to the handler exists at all.</param>
/// <param name="EnabledAtRest">Whether the control starts enabled, or needs a selection first.</param>
/// <param name="CommandId">The registry command that runs the same method, when one does.</param>
/// <param name="Preconditions">Guard clauses the method returns early on.</param>
public sealed record DialogOpening(
    string? Page,
    string? Workspace,
    string? Method,
    string? Handler,
    string? Control,
    string? ControlLabel,
    bool HasControl,
    bool EnabledAtRest,
    string? CommandId,
    IReadOnlyList<string> Preconditions);

/// <summary>Everything the source says about one dialog.</summary>
/// <remarks>
/// Read, never guessed. A field the scanner could not establish is null, because
/// the difference between "there is no opening control" and "nobody found the
/// opening control" is the difference this whole audit turns on.
/// </remarks>
public sealed record DialogRecord
{
    /// <summary>Stable identifier: the class name.</summary>
    public required string DialogId { get; init; }

    /// <summary>The markup file.</summary>
    public required string SourceFile { get; init; }

    /// <summary>The title the dialog shows.</summary>
    public string? Title { get; init; }

    /// <summary>What kind of thing the dialog does.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DialogPurpose Purpose { get; init; }

    /// <summary>The primary action's caption.</summary>
    public string? PrimaryButton { get; init; }

    /// <summary>The secondary action's caption, when there is one.</summary>
    public string? SecondaryButton { get; init; }

    /// <summary>The dismiss action's caption.</summary>
    public string? CloseButton { get; init; }

    /// <summary>Which button Enter activates, as the markup declares it.</summary>
    public string? DefaultButton { get; init; }

    /// <summary>Every input the dialog offers.</summary>
    public IReadOnlyList<DialogField> Fields { get; init; } = [];

    /// <summary>
    /// Every way the dialog is reached.
    /// </summary>
    /// <remarks>
    /// A list, because a general-purpose dialog is opened from several pages —
    /// the reason-for-this dialogs especially. Recording only the first makes the
    /// runtime pass go looking on a page where the button is not, and report a
    /// reachable dialog as unreachable.
    /// </remarks>
    public IReadOnlyList<DialogOpening> Openings { get; init; } = [];

    /// <summary>The client method called when the primary action is taken.</summary>
    public string? MutationCall { get; init; }

    /// <summary>Whether the call is sent with an idempotency key.</summary>
    public bool SendsIdempotencyKey { get; init; }

    /// <summary>Test files that name this dialog.</summary>
    public IReadOnlyList<string> TestReferences { get; init; } = [];
}

/// <summary>What a dialog is for.</summary>
public enum DialogPurpose
{
    /// <summary>Brings a new record into being.</summary>
    Create,

    /// <summary>Changes a record that exists.</summary>
    Edit,

    /// <summary>Performs a named action rather than editing fields.</summary>
    Action,

    /// <summary>Asks a question and returns an answer.</summary>
    Prompt,
}

/// <summary>
/// Reads every dialog in the client, and what opens it.
/// </summary>
/// <remarks>
/// <para>
/// A class existing in source does not prove a person can reach it. Audit 001
/// found four dialogs nothing constructs (<c>AOS-R001-017</c>), and this scanner
/// exists so that the list of what is reachable is derived rather than assumed —
/// and so that the runtime pass has a path to follow rather than a name to guess
/// at.
/// </para>
/// <para>
/// The chain is: a <c>Button</c> declares <c>Click="OnXClick"</c>; the handler
/// calls a method; the method constructs the dialog and awaits
/// <c>ShowAsync()</c>; on the primary result it calls one client API method. Each
/// link is read from text. Where a link is missing the record says so instead of
/// inventing one.
/// </para>
/// </remarks>
public static class DialogScanner
{
    private static readonly Regex Construction = new(
        @"\b(?<type>\w*Dialog)\s+\w+\s*=\s*new\s*\(|\bnew\s+(?<type2>\w*Dialog)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

    private static readonly Regex ApiCall = new(
        @"\bapi\.(?<call>\w+Async)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

    private static readonly string[] InputControls =
    [
        "TextBox", "ComboBox", "CheckBox", "DatePicker", "CalendarDatePicker", "TimePicker",
        "NumberBox", "ToggleSwitch", "AutoSuggestBox", "PasswordBox", "RadioButton", "Slider",
    ];

    /// <summary>Reads every dialog the client declares.</summary>
    /// <param name="source">The loaded repository.</param>
    /// <returns>One record per dialog, ordered by identifier.</returns>
    public static IReadOnlyList<DialogRecord> Scan(SourceIndex source)
    {
        ArgumentNullException.ThrowIfNull(source);

        List<DialogRecord> dialogs = [];

        // Not the dialogs' own code-behind. Every dialog names its own class, so
        // including them makes each dialog look like the thing that opens it.
        IReadOnlyList<string> pages =
        [
            .. source.Files.Keys.Where(x =>
                x.StartsWith("src/AgencyOS.Windows/", StringComparison.Ordinal)
                && x.EndsWith(".xaml.cs", StringComparison.Ordinal)
                && !x.StartsWith("src/AgencyOS.Windows/Dialogs/", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal),
        ];

        IReadOnlyList<string> tests =
        [
            .. source.Files.Keys.Where(x => x.StartsWith("tests/", StringComparison.Ordinal)),
        ];

        foreach (string markup in source.Files.Keys
            .Where(x => x.StartsWith("src/AgencyOS.Windows/Dialogs/", StringComparison.Ordinal)
                && x.EndsWith(".xaml", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal))
        {
            string id = Path.GetFileNameWithoutExtension(markup);
            string xaml = source.Files[markup];
            string behind = source.Files.TryGetValue(markup + ".cs", out string? code) ? code : string.Empty;

            dialogs.Add(new DialogRecord
            {
                DialogId = id,
                SourceFile = markup,
                Title = Attribute(xaml, "Title"),
                PrimaryButton = Attribute(xaml, "PrimaryButtonText"),
                SecondaryButton = Attribute(xaml, "SecondaryButtonText"),
                CloseButton = Attribute(xaml, "CloseButtonText"),
                DefaultButton = Attribute(xaml, "DefaultButton"),
                Purpose = Purpose(id, Attribute(xaml, "PrimaryButtonText")),
                Fields = Fields(xaml, behind),
                Openings = Openings(id, source, pages),
                MutationCall = Mutation(id, source, pages),
                SendsIdempotencyKey = Idempotent(id, source, pages),
                TestReferences =
                [
                    .. tests.Where(x => source.Files[x].Contains(id, StringComparison.Ordinal))
                        .Order(StringComparer.Ordinal),
                ],
            });
        }

        return dialogs;
    }

    private static DialogPurpose Purpose(string id, string? primary)
    {
        if (id.StartsWith("Add", StringComparison.Ordinal)
            || id.StartsWith("Create", StringComparison.Ordinal)
            || id.StartsWith("Record", StringComparison.Ordinal)
            || id.StartsWith("Raise", StringComparison.Ordinal))
        {
            return DialogPurpose.Create;
        }

        if (id.StartsWith("Edit", StringComparison.Ordinal)
            || id.StartsWith("Update", StringComparison.Ordinal)
            || id.StartsWith("Amend", StringComparison.Ordinal))
        {
            return DialogPurpose.Edit;
        }

        return primary is null ? DialogPurpose.Prompt : DialogPurpose.Action;
    }

    private static IReadOnlyList<DialogField> Fields(string xaml, string behind)
    {
        List<DialogField> fields = [];

        foreach (Match element in Regex.Matches(
            xaml, @"<(?<tag>[A-Za-z]+)\b(?<attrs>[^>]*?)/?>",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5)))
        {
            string tag = element.Groups["tag"].Value;

            if (!InputControls.Contains(tag, StringComparer.Ordinal))
            {
                continue;
            }

            string attrs = element.Groups["attrs"].Value;
            string? name = Value(attrs, "x:Name");

            if (name is null)
            {
                continue;
            }

            string? label = Value(attrs, "Header") ?? Value(attrs, "Content")
                ?? Value(attrs, "PlaceholderText");

            // A value the code-behind funnels through a null-or-whitespace guard
            // is one the dialog is willing to submit without.
            bool optional = Regex.IsMatch(
                behind,
                @"(Empty|Trimmed|Optional|NullIfBlank)\s*\(\s*" + Regex.Escape(name) + @"\b",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

            bool rawId = Regex.IsMatch(
                behind,
                @"Guid\.(Try)?Parse\s*\(\s*" + Regex.Escape(name) + @"\b",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

            fields.Add(new DialogField(name, tag, label, optional, rawId, Choices(xaml, name)));
        }

        return fields;
    }

    private static IReadOnlyList<string> Choices(string xaml, string name)
    {
        int start = xaml.IndexOf("x:Name=\"" + name + "\"", StringComparison.Ordinal);

        if (start < 0)
        {
            return [];
        }

        int close = xaml.IndexOf("</ComboBox>", start, StringComparison.Ordinal);

        if (close < 0)
        {
            return [];
        }

        return
        [
            .. Regex.Matches(
                    xaml[start..close], @"<ComboBoxItem[^>]*?Content=""(?<c>[^""]*)""",
                    RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5))
                .Select(x => x.Groups["c"].Value),
        ];
    }

    /// <summary>Walks back from every construction to the control that starts it.</summary>
    private static IReadOnlyList<DialogOpening> Openings(
        string id, SourceIndex source, IReadOnlyList<string> pages)
    {
        List<DialogOpening> openings = [];

        foreach (string page in pages)
        {
            string code = source.Files[page];

            // A mention is not a construction. Stopping at the first file that
            // merely names the class reports every dialog as unopenable.
            if (EnclosingMethod(code, id) is not { } method)
            {
                continue;
            }

            string? handler = HandlerCalling(code, method);
            string markupPath = page[..^3];
            string markup = source.Files.TryGetValue(markupPath, out string? x) ? x : string.Empty;

            (string? control, string? label, bool exists, bool enabled) = handler is null
                ? (null, null, false, false)
                : Control(markup, handler);

            openings.Add(new DialogOpening(
                Path.GetFileNameWithoutExtension(markupPath),
                Workspace(markupPath),
                method,
                handler,
                control,
                label,
                exists,
                enabled,
                CommandFor(code, method),
                Preconditions(code, method)));
        }

        return openings;
    }

    /// <summary>The method whose body constructs the dialog.</summary>
    private static string? EnclosingMethod(string code, string id)
    {
        Match construction = Regex.Match(
            code, @"\b" + Regex.Escape(id) + @"\s+\w+\s*=\s*new\s*\(|\bnew\s+"
                + Regex.Escape(id) + @"\s*\(",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

        if (!construction.Success)
        {
            return null;
        }

        // The nearest declaration above the construction, found by walking back a
        // line at a time. A single regex over the whole prefix is at the mercy of
        // every generic argument and lambda in the file between here and there.
        string[] lines = code[..construction.Index].Split('\n');

        for (int i = lines.Length - 1; i >= 0; i--)
        {
            Match declaration = Regex.Match(
                lines[i],
                @"^\s*(?:private|public|internal|protected)[\w\s<>,\[\]\?]*?\b(?<name>\w+)\s*\(",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

            if (declaration.Success)
            {
                return declaration.Groups["name"].Value;
            }
        }

        return null;
    }

    /// <summary>The event handler that calls a method, or the method itself when it is one.</summary>
    private static string? HandlerCalling(string code, string method)
    {
        if (method.StartsWith("On", StringComparison.Ordinal))
        {
            return method;
        }

        Match handler = Regex.Match(
            code,
            @"\b(?<name>On\w+)\s*\([^)]*\)\s*=>[^;\n]*\b" + Regex.Escape(method) + @"\s*\(",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

        return handler.Success ? handler.Groups["name"].Value : null;
    }

    /// <summary>
    /// The control whose click runs a handler.
    /// </summary>
    /// <remarks>
    /// Existence and naming are separate answers. Most of these buttons carry no
    /// <c>x:Name</c> because nothing in the code needs to reach them, and reading
    /// a missing name as a missing control reports a reachable dialog as
    /// unreachable — which the first version of this scanner did, for 24 of them.
    /// The label is what a person actually looks for, and what the runtime pass
    /// clicks on.
    /// </remarks>
    private static (string? Name, string? Label, bool Exists, bool Enabled) Control(
        string markup, string handler)
    {
        Match element = Regex.Match(
            markup, @"<(?<tag>\w+)\b(?<attrs>[^>]*?Click=""" + Regex.Escape(handler) + @"""[^>]*?)/?>",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

        if (!element.Success)
        {
            return (null, null, false, false);
        }

        string attrs = element.Groups["attrs"].Value;

        return (
            Value(attrs, "x:Name"),
            Value(attrs, "Content"),
            true,
            !string.Equals(Value(attrs, "IsEnabled"), "False", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The registry command that runs the same method.
    /// </summary>
    /// <remarks>
    /// A page's <c>Execute</c> switch is a second way in: the command palette and
    /// the declared gesture both arrive here. A dialog with no button but a
    /// command is still reachable, by a path most people would never find.
    /// </remarks>
    private static string? CommandFor(string code, string method)
    {
        Match dispatch = Regex.Match(
            code,
            @"case\s+""(?<id>[\w.]+)""\s*:\s*(?:_\s*=\s*)?" + Regex.Escape(method) + @"\s*\(",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

        return dispatch.Success ? dispatch.Groups["id"].Value : null;
    }

    /// <summary>The early returns that decide whether the dialog opens at all.</summary>
    private static IReadOnlyList<string> Preconditions(string code, string method)
    {
        Match body = Regex.Match(
            code,
            // One level of nesting was not enough for a handler that awaits
            // inside a using or a try. The guards are all at the top, so a
            // window from the signature is both sufficient and robust.
            @"\b" + Regex.Escape(method) + @"\s*\([^)]*\)\s*\{(?<body>(?:.|\n){0,2500})",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

        if (!body.Success)
        {
            return [];
        }

        return
        [
            .. Regex.Matches(
                    body.Groups["body"].Value,
                    // A guard may explain itself before it gives up, and most of
                    // these do: Error("Select a mailbox first."); return;.
                    // Requiring the return to come first recorded none of them.
                    @"if\s*\((?<cond>[^)]*(?:\([^)]*\)[^)]*)*)\)\s*\{[^{}]*?\breturn\b",
                    RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5))
                .Select(x => Regex.Replace(
                    x.Groups["cond"].Value, @"\s+", " ", RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(5)).Trim()),
        ];
    }

    /// <summary>The client call the dialog's primary action leads to.</summary>
    private static string? Mutation(string id, SourceIndex source, IReadOnlyList<string> pages)
    {
        foreach (string page in pages)
        {
            if (Construct(source.Files[page], id) is not { } at)
            {
                continue;
            }

            string code = source.Files[page];

            // The first client call after the dialog is shown. A window rather
            // than the rest of the file, because the next method's calls are not
            // this dialog's.
            int window = Math.Min(code.Length - at, 1600);
            Match call = ApiCall.Match(code.Substring(at, window));

            return call.Success ? call.Groups["call"].Value : null;
        }

        return null;
    }

    /// <summary>Where a page constructs a dialog, if it does.</summary>
    private static int? Construct(string code, string id)
    {
        Match construction = Regex.Match(
            code, @"\b" + Regex.Escape(id) + @"\s+\w+\s*=\s*new\s*\(|\bnew\s+"
                + Regex.Escape(id) + @"\s*\(",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

        return construction.Success ? construction.Index : null;
    }

    private static bool Idempotent(string id, SourceIndex source, IReadOnlyList<string> pages)
    {
        foreach (string page in pages)
        {
            if (Construct(source.Files[page], id) is not { } at)
            {
                continue;
            }

            string code = source.Files[page];
            int window = Math.Min(code.Length - at, 1600);

            return code.Substring(at, window).Contains("Guid.NewGuid()", StringComparison.Ordinal);
        }

        return false;
    }

    private static string? Workspace(string markupPath)
    {
        string name = Path.GetFileNameWithoutExtension(markupPath);

        return name.EndsWith("Page", StringComparison.Ordinal)
            ? name[..^4].ToLowerInvariant()
            : null;
    }

    private static string? Attribute(string xaml, string name) =>
        Value(xaml[..Math.Min(xaml.Length, xaml.IndexOf('>') + 1)], name);

    private static string? Value(string attrs, string name)
    {
        Match match = Regex.Match(
            attrs, @"\b" + Regex.Escape(name) + @"=""(?<v>[^""]*)""",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));

        return match.Success ? match.Groups["v"].Value : null;
    }
}
