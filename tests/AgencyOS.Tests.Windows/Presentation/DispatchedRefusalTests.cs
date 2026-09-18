using System.Text.RegularExpressions;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

/// <summary>
/// That a refusal is never lost because nobody awaited the command.
/// </summary>
/// <remarks>
/// <para>
/// The fire-and-forget observation, narrowed to the one class that actually
/// fails. A page command dispatched as <c>_ = SomethingAsync()</c> and calling the
/// server without handling a refusal loses it completely: the task faults, nobody
/// observes it, and the operator is told nothing at all.
/// </para>
/// <para>
/// Reproduced before it was repaired. As an observer — whose thesis create answers
/// <c>403</c> — <c>CreateThesisDialog</c> closed, nothing was created, no error bar
/// appeared anywhere, and focus landed on the navigation pane.
/// </para>
/// <para>
/// This is not a rule about the 230 dispatch sites. Most are loads whose view
/// model owns the error state, and nothing about them was changed.
/// </para>
/// </remarks>
public sealed class DispatchedRefusalTests
{
    /// <summary>A dispatched command that calls the server answers for it.</summary>
    [Fact]
    public void ADispatchedCommandThatCallsTheServerHandlesRefusal()
    {
        List<string> lost = [];

        foreach (string page in Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows", "Pages"), "*.xaml.cs"))
        {
            string text = File.ReadAllText(page);

            foreach (string name in Dispatched(text))
            {
                string body = Body(text, name);

                if (body.Length == 0 || !CallsTheServer(body) || Handles(body))
                {
                    continue;
                }

                lost.Add($"{Path.GetFileName(page)}: {name}");
            }
        }

        Assert.True(
            lost.Count == 0,
            "A refusal from these would be lost entirely — dispatched, unawaited and "
                + "unhandled: " + string.Join("; ", lost));
    }

    /// <summary>
    /// The rule tells a guarded dispatch from an unguarded one.
    /// </summary>
    /// <remarks>
    /// A positive and negative control, so a later edit that makes the rule vacuous
    /// fails here rather than passing quietly.
    /// </remarks>
    [Theory]
    [InlineData("_ = DoAsync();\nprivate async Task DoAsync() { await _api.CreateThesisAsync(); }", true)]
    [InlineData(
        "_ = Guarded(\"t\", DoAsync);\nprivate async Task DoAsync() { await _api.CreateThesisAsync(); }", false)]
    [InlineData(
        "_ = DoAsync();\nprivate async Task DoAsync() { try { await _api.CreateThesisAsync(); } "
            + "catch (AgencyOsApiException f) { Show(f); } }",
        false)]
    [InlineData("_ = DoAsync();\nprivate async Task DoAsync() { await _list.LoadAsync(); }", false)]
    public void TheRuleTellsTheTwoShapesApart(string source, bool flagged)
    {
        bool found = false;

        foreach (string name in Dispatched(source))
        {
            string body = Body(source, name);

            found |= body.Length > 0 && CallsTheServer(body) && !Handles(body);
        }

        Assert.Equal(flagged, found);
    }

    /// <summary>The two pages the reproduction came from are guarded.</summary>
    [Theory]
    [InlineData("IntelligencePage")]
    [InlineData("TalentPage")]
    public void TheRepairedPagesGuardTheirCommands(string page)
    {
        string text = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", page + ".xaml.cs"));

        // The dispatch still names the command directly. A helper taking the
        // method as an argument would have hidden it from every source scanner —
        // including the reviewer's own reachability analysis, which then reported
        // thirteen dialogs as unreachable when they were not.
        Assert.Contains("catch (AgencyOsApiException failure)", text.Replace("AgencyOS.Client.", string.Empty), StringComparison.Ordinal);
        Assert.Contains("Could not ", text, StringComparison.Ordinal);
    }

    /// <summary>Names dispatched without being awaited.</summary>
    private static IEnumerable<string> Dispatched(string text) =>
        Regex.Matches(text, @"_ = ([A-Za-z_][A-Za-z0-9_]*)\s*\(")
            .Select(x => x.Groups[1].Value)
            .Where(x => x != "Guarded")
            .Distinct(StringComparer.Ordinal);

    /// <summary>Whether a body reaches the server directly.</summary>
    private static bool CallsTheServer(string body) =>
        Regex.IsMatch(body, @"await\s+_?api\s*\.?\s*\n?\s*\.?[A-Za-z]+Async\(")
        || Regex.IsMatch(body, @"await\s+_api\s*\n\s*\.[A-Za-z]+Async\(");

    /// <summary>Whether a body answers a refusal itself.</summary>
    private static bool Handles(string body) =>
        body.Contains("catch (AgencyOsApiException", StringComparison.Ordinal)
        || body.Contains("catch (AgencyOS.Client.AgencyOsApiException", StringComparison.Ordinal)
        || body.Contains("Guarded(", StringComparison.Ordinal);

    /// <summary>The body of a method, by brace matching.</summary>
    private static string Body(string text, string name)
    {
        Match found = Regex.Match(
            text,
            @"(private|public|protected|internal)[^\n]*\b" + Regex.Escape(name) + @"\s*\([^)]*\)\s*\n?\s*\{");

        if (!found.Success)
        {
            return string.Empty;
        }

        int depth = 1;
        int i = found.Index + found.Length;

        while (i < text.Length && depth > 0)
        {
            if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}')
            {
                depth--;
            }

            i++;
        }

        return text[(found.Index + found.Length)..i];
    }

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
