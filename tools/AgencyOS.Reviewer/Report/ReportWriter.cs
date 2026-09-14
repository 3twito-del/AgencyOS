using System.Globalization;
using System.Text;
using System.Text.Json;
using AgencyOS.Reviewer.Ledger;
using System.IO;

namespace AgencyOS.Reviewer.Report;

/// <summary>
/// Turns gathered evidence and an authored ledger into the audit's outputs.
/// </summary>
/// <remarks>
/// <para>
/// The counts in the summary are computed from the ledger rather than typed into
/// it. A report whose totals are written by hand is a report whose totals drift,
/// and the one number a reader trusts without checking is the one most worth
/// getting from the data.
/// </para>
/// <para>
/// The writer refuses to render a finding that carries no evidence. §20 of the
/// review brief makes that a rule; enforcing it here makes it a property of the
/// artefact rather than of the reviewer's discipline.
/// </para>
/// </remarks>
public static class ReportWriter
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Renders the summary, coverage and HTML report for a run.</summary>
    public static void Render(string runDirectory, string staticDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);

        string ledgerPath = Path.Combine(runDirectory, "REVIEW-001-FINDINGS.json");

        if (!File.Exists(ledgerPath))
        {
            throw new FileNotFoundException(
                "No findings ledger. An audit that produced no ledger produced no audit.", ledgerPath);
        }

        FindingLedger ledger = JsonSerializer.Deserialize<FindingLedger>(
            File.ReadAllText(ledgerPath), Json)
            ?? throw new InvalidOperationException("The findings ledger could not be read.");

        IReadOnlyList<Finding> malformed = [.. ledger.Findings.Where(x => !x.IsWellFormed)];

        if (malformed.Count > 0)
        {
            throw new InvalidOperationException(
                "These findings carry no evidence, expectation or correction, which §20 forbids: "
                    + string.Join(", ", malformed.Select(x => x.Id)));
        }

        File.WriteAllText(
            Path.Combine(runDirectory, "REVIEW-001-SUMMARY-STATISTICS.md"),
            Statistics(ledger),
            Encoding.UTF8);

        File.WriteAllText(
            Path.Combine(runDirectory, "report.html"),
            Html(ledger, runDirectory),
            Encoding.UTF8);

        _ = staticDirectory;
    }

    /// <summary>The computed half of the summary: every count, derived.</summary>
    private static string Statistics(FindingLedger ledger)
    {
        StringBuilder text = new();

        text.AppendLine("# REVIEW-001 — computed statistics");
        text.AppendLine();
        text.AppendLine("Generated from `REVIEW-001-FINDINGS.json`. Every number here is counted, not typed.");
        text.AppendLine();
        text.AppendLine(Invariant($"Total findings: **{ledger.Findings.Count}**"));
        text.AppendLine();

        text.AppendLine("## By severity");
        text.AppendLine();
        text.AppendLine("| Severity | Count |");
        text.AppendLine("| --- | ---: |");

        foreach (Severity severity in Enum.GetValues<Severity>())
        {
            text.AppendLine(Invariant(
                $"| {severity} | {ledger.Findings.Count(x => x.Severity == severity)} |"));
        }

        text.AppendLine();
        text.AppendLine("## By category");
        text.AppendLine();
        text.AppendLine("| Category | Count |");
        text.AppendLine("| --- | ---: |");

        foreach (IGrouping<FindingCategory, Finding> group in ledger.Findings
            .GroupBy(x => x.Category)
            .OrderByDescending(x => x.Count()))
        {
            text.AppendLine(Invariant($"| {group.Key} | {group.Count()} |"));
        }

        text.AppendLine();
        text.AppendLine("## By confidence");
        text.AppendLine();
        text.AppendLine("| Confidence | Count |");
        text.AppendLine("| --- | ---: |");

        foreach (Confidence confidence in Enum.GetValues<Confidence>())
        {
            text.AppendLine(Invariant(
                $"| {confidence} | {ledger.Findings.Count(x => x.Confidence == confidence)} |"));
        }

        text.AppendLine();
        text.AppendLine("## Repair queues");
        text.AppendLine();

        foreach ((string name, Func<Finding, bool> predicate) in Queues())
        {
            IReadOnlyList<Finding> queue = [.. ledger.Findings.Where(predicate)];

            text.AppendLine(Invariant($"### {name} ({queue.Count})"));
            text.AppendLine();

            foreach (Finding finding in queue.OrderBy(x => x.Severity).ThenBy(x => x.Id, StringComparer.Ordinal))
            {
                text.AppendLine(Invariant($"- `{finding.Id}` {finding.Severity} {finding.Category} — {finding.Title}"));
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    private static IEnumerable<(string Name, Func<Finding, bool> Predicate)> Queues()
    {
        yield return ("QUEUE A — MUST FIX", x =>
            x.Severity <= Severity.S1
            || (x.Severity == Severity.S2 && x.Confidence == Confidence.ConfirmedDefect));

        yield return ("QUEUE B — SHOULD FIX", x =>
            x.Severity is Severity.S2 or Severity.S3
            && x.Confidence is Confidence.ConfirmedDefect or Confidence.LikelyDefect
            && !(x.Severity == Severity.S2 && x.Confidence == Confidence.ConfirmedDefect));

        yield return ("QUEUE C — POLISH / CONSIDER", x =>
            x.Severity is Severity.S3 or Severity.S4
            && x.Confidence is Confidence.DesignRecommendation or Confidence.Inconclusive);
    }

    private static string Html(FindingLedger ledger, string runDirectory)
    {
        StringBuilder html = new();

        html.AppendLine("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        html.AppendLine("<title>AgencyOS Review 001</title>");
        html.AppendLine("<style>");
        html.AppendLine("body{font:14px/1.55 Segoe UI,system-ui,sans-serif;margin:0;background:#14161a;color:#e6e8ec}");
        html.AppendLine("main{max-width:1100px;margin:0 auto;padding:32px 24px 80px}");
        html.AppendLine("h1{font-size:26px;margin:0 0 4px}h2{font-size:18px;margin:36px 0 12px;border-bottom:1px solid #2a2f38;padding-bottom:6px}");
        html.AppendLine(".sub{color:#98a1b0;margin:0 0 28px}");
        html.AppendLine("table{border-collapse:collapse;width:100%;margin:12px 0}");
        html.AppendLine("th,td{text-align:left;padding:7px 10px;border-bottom:1px solid #262b33;vertical-align:top}");
        html.AppendLine("th{color:#98a1b0;font-weight:600;font-size:12px;text-transform:uppercase;letter-spacing:.04em}");
        html.AppendLine(".f{border:1px solid #262b33;border-radius:8px;padding:16px 18px;margin:14px 0;background:#191c22}");
        html.AppendLine(".id{font-family:Cascadia Code,Consolas,monospace;color:#7fb3ff}");
        html.AppendLine(".s0,.s1{color:#ff6b6b}.s2{color:#ffb454}.s3{color:#8fd18f}.s4{color:#98a1b0}");
        html.AppendLine("dt{color:#98a1b0;font-size:12px;text-transform:uppercase;letter-spacing:.04em;margin-top:10px}");
        html.AppendLine("dd{margin:2px 0 0}ul{margin:4px 0 0 18px;padding:0}");
        html.AppendLine("img{max-width:100%;border:1px solid #2a2f38;border-radius:6px;margin-top:8px}");
        html.AppendLine("</style></head><body><main>");
        html.AppendLine("<h1>AgencyOS — Audit Run 001</h1>");
        html.AppendLine(Invariant($"<p class=\"sub\">{Escape(ledger.Baseline)} · {ledger.Findings.Count} findings · no product repairs applied</p>"));

        html.AppendLine("<h2>Severity</h2><table><tr><th>Severity</th><th>Count</th></tr>");

        foreach (Severity severity in Enum.GetValues<Severity>())
        {
            html.AppendLine(Invariant(
                $"<tr><td class=\"{severity.ToString().ToLowerInvariant()}\">{severity}</td>"
                    + $"<td>{ledger.Findings.Count(x => x.Severity == severity)}</td></tr>"));
        }

        html.AppendLine("</table>");

        html.AppendLine("<h2>Findings</h2>");

        foreach (Finding finding in ledger.Findings
            .OrderBy(x => x.Severity)
            .ThenBy(x => x.Id, StringComparer.Ordinal))
        {
            html.AppendLine("<div class=\"f\">");
            html.AppendLine(Invariant(
                $"<div><span class=\"id\">{Escape(finding.Id)}</span> "
                    + $"<span class=\"{finding.Severity.ToString().ToLowerInvariant()}\">{finding.Severity}</span> "
                    + $"· {finding.Category} · {finding.Confidence} · {finding.Reproducibility}</div>"));
            html.AppendLine(Invariant($"<h3>{Escape(finding.Title)}</h3>"));
            html.AppendLine("<dl>");
            html.AppendLine(Invariant($"<dt>Surface</dt><dd>{Escape(string.Join(", ", finding.Surface))}</dd>"));
            html.AppendLine(Invariant($"<dt>Expected</dt><dd>{Escape(finding.Expected)}</dd>"));
            html.AppendLine(Invariant($"<dt>Actual</dt><dd>{Escape(finding.Actual)}</dd>"));

            if (finding.Steps.Count > 0)
            {
                html.AppendLine("<dt>Steps</dt><dd><ul>");

                foreach (string step in finding.Steps)
                {
                    html.AppendLine(Invariant($"<li>{Escape(step)}</li>"));
                }

                html.AppendLine("</ul></dd>");
            }

            html.AppendLine("<dt>Evidence</dt><dd><ul>");

            foreach (string evidence in finding.Evidence)
            {
                html.AppendLine(Invariant($"<li>{Escape(evidence)}</li>"));
            }

            html.AppendLine("</ul></dd>");
            html.AppendLine(Invariant($"<dt>Why it matters</dt><dd>{Escape(finding.WhyItMatters)}</dd>"));
            html.AppendLine(Invariant(
                $"<dt>Suggested correction (not applied)</dt><dd>{Escape(finding.SuggestedCorrection)}</dd>"));
            html.AppendLine("</dl>");

            foreach (string shot in finding.Screenshots)
            {
                if (File.Exists(Path.Combine(runDirectory, shot)))
                {
                    html.AppendLine(Invariant($"<img src=\"{Escape(shot)}\" alt=\"evidence\">"));
                }
            }

            html.AppendLine("</div>");
        }

        html.AppendLine("</main></body></html>");

        return html.ToString();
    }

    private static string Escape(string? value) =>
        (value ?? string.Empty)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    private static string Invariant(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture);

    /// <summary>Passes a literal through, so call sites need not know which they wrote.</summary>
    private static string Invariant(string text) => text;
}

/// <summary>The authored ledger, as it is stored.</summary>
/// <param name="Run">Which audit run.</param>
/// <param name="Baseline">The product commit and evidence the audit ran against.</param>
/// <param name="GeneratedUtc">When the ledger was last written.</param>
/// <param name="Findings">Every finding.</param>
public sealed record FindingLedger(
    string Run,
    string Baseline,
    DateTimeOffset GeneratedUtc,
    IReadOnlyList<Finding> Findings);
