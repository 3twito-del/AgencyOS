using System.Text.RegularExpressions;
using Xunit;

namespace AgencyOS.Tests.Windows.Dialogs;

/// <summary>
/// That the four openers <c>AOS-R002-019</c> named still say something when they
/// decline to open.
/// </summary>
/// <remarks>
/// <para>
/// The repaired defect was an opener that produced nothing an operator could see.
/// Making the dialog appear fixes the case where it should appear; it does not fix
/// the case where it should not. An opener that turns a real precondition into a
/// silent <c>return</c> leaves the operator exactly where they were — the command
/// ran, and the application said nothing — so both halves are asserted here, per
/// §8.
/// </para>
/// <para>
/// Two kinds of early return are distinguished, and that distinction is the whole
/// content of the test. A guard on a <em>record</em> — nothing selected, no
/// provider configured — is a state an operator reaches by ordinary use, and it
/// has to be answered in words. A guard on the composition root
/// (<c>AppServices.Api</c>, a view model that is null because the client is not
/// configured) is not: the shell already reports that failure across the whole
/// window, and repeating it per command would be noise.
/// </para>
/// <para>
/// Structural, over the shipped code-behind, because a WinUI page is not
/// constructible in this suite. It is evidence about the shape of the handler; the
/// runtime evidence for these four openers is under
/// <c>artifacts/reviewer/run-repair-003a/</c>.
/// </para>
/// </remarks>
public sealed class OpenerRefusalTests
{
    /// <summary>The openers this wave repaired, and where each one lives.</summary>
    private static readonly (string Page, string Method, string Dialog)[] Openers =
    [
        ("CommunicationsPage.xaml.cs", "ConnectMailboxAsync", "ConnectMailboxDialog"),
        ("IntelligencePage.xaml.cs", "CreatePredictionAsync", "CreatePredictionDialog"),
        ("IntelligencePage.xaml.cs", "RecordSourceAsync", "RecordSourceDialog"),
        ("IntelligencePage.xaml.cs", "ResolvePredictionAsync", "ResolvePredictionDialog"),
    ];

    /// <summary>How a page tells an operator something, in any of its spellings.</summary>
    private static readonly string[] Visible = ["Notice(", "Error(", "NoteAsync(", "ShowError("];

    /// <summary>
    /// What a guard may be about without answering in words: the client's own
    /// configuration, which the shell reports once for everything.
    /// </summary>
    private static readonly string[] Composition =
    [
        "AppServices.Api",
        "_api is null",
        "_mailboxes is null",
        "_predictions is null",
        "_sources is null",
    ];

    /// <summary>
    /// What a guard may be about because the operator has already been asked: the
    /// dialog's own result.
    /// </summary>
    private const string Dismissal = "ContentDialogResult";

    public static TheoryData<string, string, string> Cases
    {
        get
        {
            TheoryData<string, string, string> data = [];

            foreach ((string page, string method, string dialog) in Openers)
            {
                data.Add(page, method, dialog);
            }

            return data;
        }
    }

    /// <summary>The valid path still reaches the dialog it is named for.</summary>
    /// <remarks>
    /// Cheap, and worth having. If a future edit rewires one of these openers to a
    /// different dialog or drops the construction, the runtime pass would report
    /// the dialog as unreachable and the reason would be here.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Cases))]
    public void TheOpenerStillConstructsItsDialog(string page, string method, string dialog)
    {
        string body = Body(page, method);

        Assert.True(
            Construction(body, dialog) >= 0,
            $"{method} no longer constructs a {dialog}.");

        Assert.Contains("ShowAsync()", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every precondition an operator can hit is answered where they can see it.
    /// </summary>
    /// <remarks>
    /// Fails against the product baseline <c>6e9b66f</c> on
    /// <c>ResolvePredictionAsync</c>, which returned in silence when no prediction
    /// was selected — a state the command palette makes reachable from every tab
    /// on the page.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryOperatorReachablePreconditionIsAnswered(
        string page,
        string method,
        string dialog)
    {
        string body = Body(page, method);

        _ = dialog;

        List<string> silent = [];

        foreach (Match guard in Regex.Matches(
            body,
            @"if\s*\((?<condition>[^{]*?)\)\s*\{(?<block>[^{}]*)\}",
            RegexOptions.Singleline | RegexOptions.CultureInvariant))
        {
            string condition = guard.Groups["condition"].Value;
            string block = guard.Groups["block"].Value;

            if (!block.Contains("return", StringComparison.Ordinal))
            {
                continue;
            }

            // Cancel is not a refusal. The operator saw the dialog and closed it,
            // which is an answer they already have.
            if (condition.Contains(Dismissal, StringComparison.Ordinal))
            {
                continue;
            }

            // Every clause, not any clause. The baseline folded "no prediction is
            // selected" into the same guard as "the client is not configured", and
            // a rule that exempted the whole condition because one disjunct was
            // about configuration would have exempted the defect with it.
            if (Clauses(condition)
                .All(x => Composition.Any(y => x.Contains(y, StringComparison.Ordinal))))
            {
                continue;
            }

            if (Visible.Any(x => block.Contains(x, StringComparison.Ordinal)))
            {
                continue;
            }

            silent.Add(Collapse(condition));
        }

        Assert.True(
            silent.Count == 0,
            $"{method} returns without telling the operator why, on: "
                + string.Join("; ", silent)
                + ". An opener that says nothing is the defect AOS-R002-019 was.");
    }

    /// <summary>One guard's condition, split into the things it is about.</summary>
    private static IReadOnlyList<string> Clauses(string condition) =>
    [
        .. condition
            .Split("||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Collapse)
            .Where(x => x.Length > 0),
    ];

    /// <summary>Where the opener declares its dialog, or -1 when it no longer does.</summary>
    /// <remarks>
    /// Target-typed, as the client writes it: <c>RecordSourceDialog dialog = new()</c>.
    /// </remarks>
    private static int Construction(string body, string dialog)
    {
        Match match = Regex.Match(
            body,
            Regex.Escape(dialog) + @"\s+\w+\s*=\s*new",
            RegexOptions.CultureInvariant);

        return match.Success ? match.Index : -1;
    }

    private static string Body(string page, string method)
    {
        string code = File.ReadAllText(
            Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows", "Pages", page));

        Match declaration = Regex.Match(
            code,
            @"private\s+async\s+Task\s+" + Regex.Escape(method) + @"\(\)\s*\{",
            RegexOptions.CultureInvariant);

        Assert.True(declaration.Success, $"{page} no longer declares {method}.");

        int start = declaration.Index + declaration.Length;
        int depth = 1;
        int index = start;

        while (depth > 0 && index < code.Length)
        {
            depth += code[index] switch { '{' => 1, '}' => -1, _ => 0 };
            index++;
        }

        return code[start..(index - 1)];
    }

    private static string Collapse(string text) =>
        Regex.Replace(text, @"\s+", " ", RegexOptions.CultureInvariant).Trim();

    /// <summary>Walks up from the test binary to the repository root.</summary>
    private static string RepositoryRoot { get; } = Find();

    private static string Find()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AgencyOS.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root was not found.");
    }
}
