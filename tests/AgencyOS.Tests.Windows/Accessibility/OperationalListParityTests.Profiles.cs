using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using AgencyOS.Client.Presentation;
using AgencyOS.Contracts.Documents;
using AgencyOS.Contracts.Finance;
using AgencyOS.Contracts.Search;
using Xunit;

namespace AgencyOS.Tests.Windows.Accessibility;

/// <summary>
/// The row profiles the product repair introduced, held to the markup that names them.
/// </summary>
/// <remarks>
/// <para>
/// A profiled row announces exactly its profile's fields. These tests hold each
/// profile to its templates. Every id a template names exists and every profile is
/// named. Each field is a value its template shows as a scan fact, through the same
/// kind of rendering, and every scan fact is a field. The role words agree with the
/// accounting, and a value the template hides is never said.
/// </para>
/// <para>
/// <strong>Overflow (D4).</strong> A field a profile lets overflow is shortened in the
/// row's name and offered whole on the same row's help text. That wiring is proved here;
/// that an operator finds and reads it is <em>PRIMARY OVERFLOW — LIVE PROOF PENDING</em>
/// until the release-candidate client run.
/// </para>
/// </remarks>
public sealed partial class OperationalListParityTests
{
    private static IEnumerable<(Entry Entry, RowProfile Profile)> Profiled() =>
        Catalog.Select(x => (Entry: x, Profile: ProfileOf(x))).Where(x => x.Profile is not null)!;

    // ---------------------------------------------------------- the registry

    /// <summary>Every profile id a template names is a profile.</summary>
    [Fact]
    public void EveryProfileATemplateNamesExists()
    {
        List<string> unknown = [.. Catalog
            .Select(x => (Entry: x, Id: Parse(Template(x).Root.Attribute("AutomationProperties.Name")!.Value).Parameter))
            .Where(x => x.Id is not null && RowProfiles.Find(x.Id) is null)
            .Select(x => $"{Key(x.Entry.File, x.Entry.Template)} names '{x.Id}'")];

        Assert.True(unknown.Count == 0, "Unknown profiles: " + string.Join("; ", unknown));
    }

    /// <summary>Every profile is named by a template: none is stale.</summary>
    [Fact]
    public void EveryProfileIsUsed()
    {
        HashSet<string> used = [.. Profiled().Select(x => x.Profile.Id)];
        List<string> stale = [.. RowProfiles.All.Keys.Where(x => !used.Contains(x))];

        Assert.True(stale.Count == 0, "Profiles no template names: " + string.Join(", ", stale));
    }

    /// <summary>
    /// A template's converter parameter is a bare profile id; the words are in the profile.
    /// </summary>
    [Fact]
    public void ATemplateNamesItsProfileAndNothingElse()
    {
        foreach (Entry entry in Catalog)
        {
            Shown name = Parse(Template(entry).Root.Attribute("AutomationProperties.Name")!.Value);

            Assert.Null(name.Path);
            Assert.Equal("RowLabel", name.Converter);
            Assert.True(
                name.Parameter is null || Regex.IsMatch(name.Parameter, "^[A-Z][A-Za-z]+$"),
                $"{Key(entry.File, entry.Template)}: '{name.Parameter}' is not a profile id.");
        }
    }

    /// <summary>
    /// Every profile field is a scan fact its template shows, rendered the same way.
    /// </summary>
    /// <remarks>
    /// A token is shown through the display-label converter, money through the money
    /// converter, a person through the party converter with the same field, and plain
    /// text bare or as a date. A field whose role is another field's value needs that
    /// field shown too.
    /// </remarks>
    [Fact]
    public void EveryProfileFieldIsAScanFactItsTemplateShows()
    {
        List<string> wrong = [];

        foreach ((Entry entry, RowProfile profile) in Profiled())
        {
            List<Shown> primary = [.. Primaries(entry)];

            foreach (RowField field in profile.Fields)
            {
                Shown? shown = primary.FirstOrDefault(x => FieldFor(profile, x) == field);

                string? expected = field.Kind switch
                {
                    RowFieldKind.Token => "DisplayLabel",
                    RowFieldKind.Money => "Money",
                    RowFieldKind.Party => "Party",
                    _ => null,
                };

                if (shown is null)
                {
                    wrong.Add($"{Key(entry.File, entry.Template)}: {profile.Id}.{field.Path} is not a scan fact it shows");
                }
                else if (shown.Converter != expected && !(expected is null && shown.Converter == "IsoDate"))
                {
                    wrong.Add($"{Key(entry.File, entry.Template)}: {profile.Id}.{field.Path} is {field.Kind}, shown through {shown.Converter ?? "no converter"}");
                }

                if (field.RoleFrom is { } source
                    && !primary.Any(x => x.Path == source && x.Converter is null))
                {
                    wrong.Add($"{Key(entry.File, entry.Template)}: {profile.Id}.{field.Path} takes its role from {source}, which it does not show");
                }
            }
        }

        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }

    /// <summary>Every scan fact a profiled template shows is one of its profile's fields.</summary>
    [Fact]
    public void EveryScanFactOfAProfiledRowIsInItsProfile()
    {
        List<string> missing = [];

        foreach ((Entry entry, RowProfile profile) in Profiled())
        {
            foreach (Shown shown in Primaries(entry))
            {
                bool spoken = FieldFor(profile, shown) is not null
                    || profile.Fields.Any(x => x.RoleFrom is not null && x.RoleFrom == shown.Path);

                if (!spoken)
                {
                    missing.Add($"{Key(entry.File, entry.Template)}: {shown.Binding}");
                }
            }
        }

        Assert.True(missing.Count == 0, "Scan facts no profile field speaks: " + string.Join("; ", missing));
    }

    /// <summary>
    /// Where the accounting names a value's role, the profile says the same word.
    /// </summary>
    [Fact]
    public void EveryAccountedRoleIsTheWordItsProfileSays()
    {
        List<string> disagree = [];

        foreach ((Entry entry, RowProfile profile) in Profiled())
        {
            foreach (Shown shown in Primaries(entry))
            {
                RowField? field = FieldFor(profile, shown);

                if (shown.Role is { } role && !string.Equals(field?.Role, role, StringComparison.Ordinal))
                {
                    disagree.Add($"{Key(entry.File, entry.Template)}: {shown.Binding} is '{role}', its profile says '{field?.Role}'");
                }

                if (shown.RoleFrom is { } source && !string.Equals(field?.RoleFrom, source, StringComparison.Ordinal))
                {
                    disagree.Add($"{Key(entry.File, entry.Template)}: {shown.Binding} takes its role from {source}, its profile from '{field?.RoleFrom}'");
                }
            }
        }

        Assert.True(disagree.Count == 0, string.Join("; ", disagree));
    }

    /// <summary>At most one field yields to the budget, and only text overflows.</summary>
    [Fact]
    public void AProfileLetsOneFieldYieldAndOnlyTextOverflows()
    {
        foreach (RowProfile profile in RowProfiles.All.Values)
        {
            Assert.True(profile.Fields.Count(x => x.Yields) <= 1, $"{profile.Id} lets more than one field yield.");

            foreach (RowField field in profile.Fields.Where(x => x.Overflow))
            {
                Assert.True(field.Yields, $"{profile.Id}.{field.Path} overflows without yielding.");
                Assert.Equal(RowFieldKind.Text, field.Kind);
                Assert.NotNull(field.Role);
            }

            foreach (RowField field in profile.Fields.Where(x => x.Yields))
            {
                Assert.Equal(RowFieldKind.Text, field.Kind);
            }
        }
    }

    /// <summary>
    /// A field that overflows is offered whole on the same row's help text (D4).
    /// </summary>
    /// <remarks>
    /// Structural: the row binds the value itself to its help text, and that help text
    /// evaluates to the whole value. Operator proof is pending the live run.
    /// </remarks>
    [Fact]
    public void EveryOverflowIsOfferedWholeOnTheSameRow()
    {
        int checkedFields = 0;

        foreach ((Entry entry, RowProfile profile) in Profiled())
        {
            foreach (RowField field in profile.Fields.Where(x => x.Overflow))
            {
                RowTemplate markup = Template(entry);
                object row = Sentinels.Build(RowType(entry));

                Assert.Equal($"{{Binding {field.Path}}}", markup.Root.Attribute("AutomationProperties.HelpText")?.Value);
                Assert.Equal(Convert.ToString(Read(row, field.Path), CultureInfo.CurrentCulture), HelpText(markup, row));
                checkedFields++;
            }
        }

        // The Receivable and Commission contract titles.
        Assert.Equal(2, checkedFields);
    }

    /// <summary>A profiled row says nothing its template hides.</summary>
    /// <remarks>
    /// Every sentinel value on the row that no profile field reads — a hidden text, a
    /// hidden amount, a count, a date — must be absent from the announcement, unless
    /// the row shows it inside a value it does show. Identifiers and version counters
    /// are among them.
    /// </remarks>
    [Fact]
    public void AProfiledRowSaysNothingItsTemplateHides()
    {
        List<string> said = [];

        foreach ((Entry entry, RowProfile profile) in Profiled())
        {
            object row = Sentinels.Build(RowType(entry));
            string announced = Announce(entry, row);

            // A hidden field's value may still be on screen inside a shown one: the
            // currency every amount carries, the version numbers a conflict sentence
            // states. Saying it there, as part of that shown value, is not a leak.
            List<string> shown = [.. Primaries(entry).Select(x => Render(row, x))];

            HashSet<string> read = [.. profile.Fields
                .SelectMany(x => (string?[])[x.Path, x.RoleFrom])
                .OfType<string>()
                .Select(x => x.Split('.')[0])];

            foreach (PropertyInfo property in row.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(x => x.GetIndexParameters().Length == 0 && !read.Contains(x.Name)))
            {
                foreach (string hidden in Hidden(property.GetValue(row)))
                {
                    if (SaysHidden(announced, hidden, shown))
                    {
                        said.Add($"{Key(entry.File, entry.Template)}: hidden {property.Name} '{hidden}' in '{announced}'");
                    }
                }
            }
        }

        Assert.True(said.Count == 0, string.Join("; ", said));
    }

    /// <summary>
    /// Whether an announcement says a hidden value other than as part of a shown one.
    /// </summary>
    /// <remarks>
    /// Narrow on purpose. Each shown value whose rendering contains the hidden one
    /// accounts for one occurrence of that rendering, and only where the announcement
    /// says that rendering itself. Whatever remains is said on its own. A hidden value
    /// that merely equals some token on screen is not excused: said a second time, or
    /// outside the shown value that holds it, it is a leak.
    /// </remarks>
    internal static bool SaysHidden(string announced, string hidden, IEnumerable<string> shown)
    {
        string remaining = announced;

        foreach (string rendering in shown.Where(x => x.Length > 0 && Coverage.ContainsWords(x, hidden)))
        {
            int at = remaining.IndexOf(rendering, StringComparison.Ordinal);

            if (at >= 0)
            {
                remaining = string.Concat(remaining.AsSpan(0, at), " | ", remaining.AsSpan(at + rendering.Length));
            }
        }

        return Coverage.ContainsWords(remaining, hidden);
    }

    /// <summary>The exception for a hidden value inside a shown one stays narrow.</summary>
    [Theory]
    [InlineData("Cash, 70.00 USD debits", "USD", "70.00 USD", false)]
    [InlineData("Cash, 70.00 USD debits, USD", "USD", "70.00 USD", true)]
    [InlineData("Draft, You saw version 7; it is now version 8.", "7", "You saw version 7; it is now version 8.", false)]
    [InlineData("Draft, Version: 7", "7", "You saw version 7; it is now version 8.", true)]
    [InlineData("zqab, Note: zqab", "zqab", "zqab", true)]
    [InlineData("zqab", "zqab", "zqab", false)]
    [InlineData("Payer: zqab", "zqab", "zqac", true)]
    public void TheShownValueExceptionIsNarrow(string announced, string hidden, string shown, bool leak)
    {
        Assert.Equal(leak, SaysHidden(announced, hidden, [shown]));
    }

    /// <summary>The forms in which a hidden value could be said.</summary>
    private static IEnumerable<string> Hidden(object? value)
    {
        switch (value)
        {
            case string { Length: > 0 } text:
                yield return text;
                break;
            case MoneyResponse money:
                yield return money.Amount.ToString("N2", CultureInfo.CurrentCulture);
                yield return money.Amount.ToString("N2", CultureInfo.InvariantCulture);
                break;
            case int or long or decimal:
                yield return Convert.ToString(value, CultureInfo.InvariantCulture)!;
                break;
            case Guid id:
                yield return id.ToString();
                yield return id.ToString()[..8];
                break;
            case DateOnly or DateTimeOffset or DateTime:
                yield return IsoDate.Format(value);
                break;
        }
    }

    /// <summary>
    /// The ledger balance row is not profiled and still covered, as it was.
    /// </summary>
    [Fact]
    public void TheBalanceRowIsUnchanged()
    {
        Entry balance = Find("Pages/FinancePage.xaml", "BalanceList");

        Assert.Null(ProfileOf(balance));
        Assert.Equal(
            "{Binding Converter={StaticResource RowLabel}}",
            Template(balance).Root.Attribute("AutomationProperties.Name")?.Value);
    }

    // ------------------------------------------------ realistic Finance rows

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-GB");

    private static T InEnglish<T>(Func<T> act)
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = English;

        try
        {
            return act();
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    private const string SlateTitle = "Autumn slate feature agreement with Northgate Pictures (three pictures)";

    private static ReceivableResponse Receivable(
        string beneficiary = "Client",
        string payer = "Northgate Pictures",
        decimal original = 240_000m,
        decimal allocated = 90_000m,
        decimal outstanding = 150_000m) =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            SlateTitle,
            Guid.CreateVersion7(),
            payer,
            beneficiary,
            Guid.CreateVersion7(),
            "Ana Reyes",
            new MoneyResponse(original, "GBP"),
            new MoneyResponse(allocated, "GBP"),
            new MoneyResponse(12_345m, "GBP"),
            new MoneyResponse(outstanding, "GBP"),
            new DateOnly(2026, 11, 30),
            "PartiallyPaid",
            false,
            "INV-2026-0417",
            null,
            "Second tranche",
            new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
            7);

    private static string ReceivableSaid(ReceivableResponse row) =>
        InEnglish(() => RowLabel.For(row, "Receivable"));

    /// <summary>
    /// Whether one segment of the announcement is this amount, in this currency, in this role.
    /// </summary>
    private static bool SaysInRole(string announced, string money, string role) =>
        announced.Split(", ").Contains(money + " " + role, StringComparer.Ordinal);

    /// <summary>
    /// The realistic receivable row keeps every scan fact whole and shortens only its title.
    /// </summary>
    [Fact]
    public void TheRealisticReceivableRowKeepsEveryScanFact()
    {
        string said = ReceivableSaid(Receivable());

        Assert.True(said.Length <= 160, $"{said.Length}: {said}");

        string[] segments = said.Split(", ");

        Assert.StartsWith("Contract: Autumn slate", segments[0], StringComparison.Ordinal);
        Assert.EndsWith("…", segments[0], StringComparison.Ordinal);
        Assert.Contains("Payer: Northgate Pictures", segments);
        Assert.True(SaysInRole(said, "240,000.00 GBP", "original"), said);
        Assert.True(SaysInRole(said, "90,000.00 GBP", "allocated"), said);
        Assert.True(SaysInRole(said, "150,000.00 GBP", "outstanding"), said);
        Assert.Contains("Partially paid", segments);
        Assert.Contains("Client money", segments);

        // The shape the owner approved, produced by the mechanism rather than written here.
        Assert.Equal(
            "Contract: Autumn slate…, Payer: Northgate Pictures, 240,000.00 GBP original, "
                + "90,000.00 GBP allocated, 150,000.00 GBP outstanding, Partially paid, Client money",
            said);
        Assert.Equal(158, said.Length);
    }

    /// <summary>An agency receivable says whose money it is.</summary>
    [Fact]
    public void TheReceivableRowSaysWhoseMoneyItIs()
    {
        string agency = ReceivableSaid(Receivable(beneficiary: "Agency"));
        string client = ReceivableSaid(Receivable(beneficiary: "Client"));

        Assert.Contains("Agency money", agency.Split(", "));
        Assert.DoesNotContain("Client money", agency.Split(", "));
        Assert.Contains("Client money", client.Split(", "));
        Assert.True(agency.Length <= 160, agency);
    }

    /// <summary>The receivable row says none of what it hides.</summary>
    [Fact]
    public void TheReceivableRowSaysNothingItHides()
    {
        ReceivableResponse row = Receivable();
        string said = ReceivableSaid(row);

        Assert.DoesNotContain("INV-2026-0417", said, StringComparison.Ordinal);
        Assert.DoesNotContain("12,345.00", said, StringComparison.Ordinal);
        Assert.DoesNotContain("adjusted", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ana Reyes", said, StringComparison.Ordinal);
        Assert.DoesNotContain("Second tranche", said, StringComparison.Ordinal);
        Assert.DoesNotContain("2026-11-30", said, StringComparison.Ordinal);
        Assert.DoesNotContain(row.Id.ToString()[..8], said, StringComparison.OrdinalIgnoreCase);
        Assert.False(Regex.IsMatch(said, @"(?<![\d,.])7(?![\d,.])"), said);
    }

    /// <summary>
    /// Swapping any two of the three amounts fails the role check: each figure is its own.
    /// </summary>
    [Fact]
    public void SwappingAnyTwoReceivableAmountsFails()
    {
        (decimal Amount, string Money, string Role)[] facts =
        [
            (240_000m, "240,000.00 GBP", "original"),
            (90_000m, "90,000.00 GBP", "allocated"),
            (150_000m, "150,000.00 GBP", "outstanding"),
        ];

        bool Right(string said) => facts.All(x => SaysInRole(said, x.Money, x.Role));

        Assert.True(Right(ReceivableSaid(Receivable())));

        foreach ((int i, int j) in (ValueTuple<int, int>[])[(0, 1), (0, 2), (1, 2)])
        {
            decimal[] amounts = [.. facts.Select(x => x.Amount)];
            (amounts[i], amounts[j]) = (amounts[j], amounts[i]);

            string swapped = ReceivableSaid(Receivable(original: amounts[0], allocated: amounts[1], outstanding: amounts[2]));

            Assert.False(Right(swapped), $"Swapping {facts[i].Role} and {facts[j].Role} passed: {swapped}");
        }
    }

    /// <summary>The full receivable contract title is on the same row's help text.</summary>
    [Fact]
    public void TheReceivableTitleIsWholeOnTheSameRow()
    {
        RowTemplate markup = Template(Find("Pages/FinancePage.xaml", "ReceivableList"));

        Assert.Equal(SlateTitle, HelpText(markup, Receivable()));
        Assert.DoesNotContain(SlateTitle, ReceivableSaid(Receivable()), StringComparison.Ordinal);
    }

    private static CommissionEntitlementResponse Commission(string title = SlateTitle, string client = "Ana Reyes") =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            title,
            Guid.CreateVersion7(),
            client,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            null,
            "Gross",
            10m,
            new MoneyResponse(240_000m, "GBP"),
            new MoneyResponse(24_000m, "GBP"),
            new MoneyResponse(9_000m, "GBP"),
            new MoneyResponse(0m, "GBP"),
            new MoneyResponse(15_000m, "GBP"),
            new DateOnly(2026, 9, 1),
            "PartiallyCollected",
            [],
            new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
            "Ben Okafor",
            null,
            3);

    /// <summary>
    /// The realistic commission row keeps its client, figures and status, and shortens its title.
    /// </summary>
    [Fact]
    public void TheRealisticCommissionRowKeepsEveryScanFact()
    {
        string said = InEnglish(() => RowLabel.For(Commission(), "Commission"));
        string[] segments = said.Split(", ");

        Assert.True(said.Length <= 160, $"{said.Length}: {said}");
        Assert.Equal("Client: Ana Reyes", segments[0]);
        Assert.StartsWith("Contract: Autumn slate", segments[1], StringComparison.Ordinal);
        Assert.EndsWith("…", segments[1], StringComparison.Ordinal);
        Assert.True(SaysInRole(said, "24,000.00 GBP", "entitled"), said);
        Assert.True(SaysInRole(said, "9,000.00 GBP", "collected"), said);
        Assert.True(SaysInRole(said, "15,000.00 GBP", "outstanding"), said);
        Assert.Equal("Partially collected", segments[^1]);

        // The basis amount and the adjustment are not shown on the row.
        Assert.DoesNotContain("240,000.00", said, StringComparison.Ordinal);
        Assert.DoesNotContain("adjusted", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ben Okafor", said, StringComparison.Ordinal);

        RowTemplate markup = Template(Find("Pages/FinancePage.xaml", "CommissionList"));

        Assert.Equal(SlateTitle, HelpText(markup, Commission()));
    }

    // ------------------------------------------- D5: truth before the budget

    /// <summary>The least of a yielding value <see cref="RowLabel"/> keeps (its MinimumHeadline).</summary>
    private const int MinimumFragment = 12;

    private const string ContractRole = "Contract: ";

    /// <summary>
    /// The contract segment: present once, under its role, holding a recognisable
    /// fragment of the title — at least its first whole word, and a prefix of it.
    /// </summary>
    private static string ContractFragment(string said, string title = SlateTitle)
    {
        string segment = Assert.Single(said.Split(", "), x => x.StartsWith(ContractRole, StringComparison.Ordinal));
        string value = segment[ContractRole.Length..];
        string stem = value.EndsWith('…') ? value[..^1] : value;

        Assert.True(
            stem.Length >= title.Split(' ')[0].Length && title.StartsWith(stem, StringComparison.Ordinal),
            $"'{segment}' is not a recognisable fragment of '{title}'.");

        return value;
    }

    /// <summary>
    /// Every compact scan fact of the receivable row, each complete and in its role.
    /// </summary>
    private static void AssertEveryReceivableFact(string said, string payer, string beneficiary = "Client")
    {
        string[] segments = said.Split(", ");

        Assert.Contains("Payer: " + payer, segments);
        Assert.True(SaysInRole(said, "240,000.00 GBP", "original"), said);
        Assert.True(SaysInRole(said, "90,000.00 GBP", "allocated"), said);
        Assert.True(SaysInRole(said, "150,000.00 GBP", "outstanding"), said);
        Assert.Contains("Partially paid", segments);
        Assert.Contains(beneficiary + " money", segments);
    }

    /// <summary>
    /// Where a name runs past 160, the excess is only what the required fragment forced.
    /// </summary>
    /// <remarks>
    /// The facts other than the contract are complete, so their length is not a
    /// choice. Past 160, the fragment must be the minimum one — no more than the
    /// product's minimum headline — and the normal budget must genuinely have been
    /// too small for it: there is no larger cap, fixed or otherwise.
    /// </remarks>
    private static void AssertExcessIsOnlyWhatTruthForces(string said)
    {
        if (said.Length <= 160)
        {
            return;
        }

        string fragment = ContractFragment(said);
        string rest = string.Join(", ", said.Split(", ").Where(x => !x.StartsWith(ContractRole, StringComparison.Ordinal)));

        Assert.True(fragment.Length <= MinimumFragment, $"'{fragment}' is longer than the minimum fragment.");
        Assert.True(
            rest.Length + 2 + ContractRole.Length + MinimumFragment > 160,
            $"{said.Length}: the compact facts ({rest.Length}) left room for the fragment within 160.");
        Assert.Equal(rest.Length + 2 + ContractRole.Length + fragment.Length, said.Length);
    }

    /// <summary>
    /// A payer a few characters longer must not erase the contract (the D5 reproduction).
    /// </summary>
    /// <remarks>
    /// At <c>4d5b9b2</c> the facts other than the contract left 11 characters for its
    /// title, one under the minimum, and the row dropped the contract entirely.
    /// </remarks>
    [Fact]
    public void ALongerPayerDoesNotEraseTheContract()
    {
        const string payer = "Northgate Pictures LLC";
        string said = ReceivableSaid(Receivable(payer: payer));

        AssertEveryReceivableFact(said, payer);
        Assert.Equal("Autumn…", ContractFragment(said));
        AssertExcessIsOnlyWhatTruthForces(said);
        Assert.Equal(
            "Contract: Autumn…, Payer: Northgate Pictures LLC, 240,000.00 GBP original, "
                + "90,000.00 GBP allocated, 150,000.00 GBP outstanding, Partially paid, Client money",
            said);
        Assert.Equal(156, said.Length);
    }

    /// <summary>
    /// A long legal payer name keeps every fact whole and the contract recognisable,
    /// past 160 by exactly what that requires.
    /// </summary>
    [Fact]
    public void ALongLegalPayerKeepsEveryFactAndTheContract()
    {
        const string payer = "Northgate Pictures International Film Distribution and Production Holdings Limited";
        ReceivableResponse row = Receivable(payer: payer, beneficiary: "Agency");
        string said = ReceivableSaid(row);

        AssertEveryReceivableFact(said, payer, "Agency");
        Assert.Equal("Autumn…", ContractFragment(said));
        AssertExcessIsOnlyWhatTruthForces(said);
        Assert.Equal(said, ReceivableSaid(row));
        Assert.Equal(216, said.Length);

        // Nothing hidden joined it.
        Assert.DoesNotContain("INV-2026-0417", said, StringComparison.Ordinal);
        Assert.DoesNotContain("12,345.00", said, StringComparison.Ordinal);
        Assert.DoesNotContain("Ana Reyes", said, StringComparison.Ordinal);
        Assert.Equal(7, said.Split(", ").Length);

        RowTemplate markup = Template(Find("Pages/FinancePage.xaml", "ReceivableList"));

        Assert.Equal(SlateTitle, HelpText(markup, row));
    }

    /// <summary>A row whose whole phrase fits says its title whole and does not grow.</summary>
    [Fact]
    public void AReceivableThatFitsDoesNotUseTheSafetyValve()
    {
        ReceivableResponse row = Receivable() with { ContractTitle = "Autumn slate" };
        string said = ReceivableSaid(row);

        AssertEveryReceivableFact(said, "Northgate Pictures");
        Assert.Equal("Autumn slate", ContractFragment(said, "Autumn slate"));
        Assert.Equal(ContractRole.Length + "Autumn slate".Length + 2 + 133, said.Length);
        Assert.True(said.Length <= 160, said);
    }

    /// <summary>
    /// A long client name keeps the client, every amount and the status whole, and the
    /// contract recognisable.
    /// </summary>
    /// <remarks>
    /// At <c>4d5b9b2</c> this row dropped its contract: the other facts took 150 of
    /// the 160 characters.
    /// </remarks>
    [Fact]
    public void ALongClientDoesNotEraseTheCommissionContract()
    {
        const string client = "Anastasia Reyes-Okafor de la Fuente Montgomery";
        CommissionEntitlementResponse row = Commission(client: client);
        string said = InEnglish(() => RowLabel.For(row, "Commission"));
        string[] segments = said.Split(", ");

        Assert.Equal("Client: " + client, segments[0]);
        Assert.Equal("Autumn…", ContractFragment(said));
        Assert.True(SaysInRole(said, "24,000.00 GBP", "entitled"), said);
        Assert.True(SaysInRole(said, "9,000.00 GBP", "collected"), said);
        Assert.True(SaysInRole(said, "15,000.00 GBP", "outstanding"), said);
        Assert.Equal("Partially collected", segments[^1]);
        AssertExcessIsOnlyWhatTruthForces(said);
        Assert.Equal(
            "Client: Anastasia Reyes-Okafor de la Fuente Montgomery, Contract: Autumn…, 24,000.00 GBP entitled, "
                + "9,000.00 GBP collected, 15,000.00 GBP outstanding, Partially collected",
            said);
        Assert.Equal(169, said.Length);

        RowTemplate markup = Template(Find("Pages/FinancePage.xaml", "CommissionList"));

        Assert.Equal(SlateTitle, HelpText(markup, row));
    }

    /// <summary>A short contract title is said whole.</summary>
    [Fact]
    public void AShortContractTitleIsSaidWhole()
    {
        string said = InEnglish(() => RowLabel.For(Commission("Autumn slate"), "Commission"));

        Assert.Contains("Contract: Autumn slate", said.Split(", "));
    }

    // --------------------------------------------------- the link composition

    private sealed record Linked(string Target, string TargetLabel);

    private static readonly Shown[] LinkBindings =
    [
        Parse("{Binding TargetLabel}") with { RoleFrom = "Target" },
        Parse("{Binding Target}"),
    ];

    /// <summary>"Deal: Autumn slate" covers both the kind and the label.</summary>
    [Fact]
    public void ALinkIsCoveredByItsKindAsTheLabelsRole()
    {
        Linked row = new("Deal", "Autumn slate");

        Assert.Null(Coverage.Check(row, LinkBindings[0], LinkBindings, "Deal: Autumn slate"));
        Assert.Null(Coverage.Check(row, LinkBindings[1], LinkBindings, "Deal: Autumn slate"));
    }

    /// <summary>The same label under the wrong kind fails both.</summary>
    [Fact]
    public void ALinkUnderTheWrongKindFails()
    {
        Linked row = new("Deal", "Autumn slate");

        Assert.NotNull(Coverage.Check(row, LinkBindings[0], LinkBindings, "Contract: Autumn slate"));
        Assert.NotNull(Coverage.Check(row, LinkBindings[1], LinkBindings, "Contract: Autumn slate"));
    }

    /// <summary>The right kind with the wrong label fails both.</summary>
    [Fact]
    public void ALinkWithTheWrongLabelFails()
    {
        Linked row = new("Deal", "Autumn slate");

        Assert.NotNull(Coverage.Check(row, LinkBindings[0], LinkBindings, "Deal: Winter slate"));
        Assert.NotNull(Coverage.Check(row, LinkBindings[1], LinkBindings, "Deal: Winter slate"));
    }

    /// <summary>The kind and the label said apart, bare, fail both.</summary>
    [Fact]
    public void ALinkSaidBareFails()
    {
        Linked row = new("Deal", "Autumn slate");

        Assert.NotNull(Coverage.Check(row, LinkBindings[0], LinkBindings, "Deal, Autumn slate"));
        Assert.NotNull(Coverage.Check(row, LinkBindings[1], LinkBindings, "Deal, Autumn slate"));
    }

    /// <summary>An ordinary row of two texts still takes each role from its own field.</summary>
    [Fact]
    public void AnOrdinaryRowStillUsesFixedRoles()
    {
        Filing row = new("Case 12", "Ana Reyes", "Ben Okafor");
        Shown[] shown = [Parse("{Binding Name}"), Parse("{Binding Claimant}"), Parse("{Binding Respondent}")];

        Assert.Null(Coverage.Check(row, shown[1], shown, "Case 12, Claimant: Ana Reyes, Respondent: Ben Okafor"));

        // A value's role is its own field's name, not whatever the row says beside it.
        Assert.NotNull(Coverage.Check(row, shown[1], shown, "Case 12, Ben Okafor: Ana Reyes, Respondent: Ben Okafor"));
        Assert.NotNull(Coverage.Check(row, shown[1], shown, "Case 12, Respondent: Ana Reyes, Claimant: Ben Okafor"));
    }

    /// <summary>Both link lists use the one composition.</summary>
    [Fact]
    public void BothLinkListsUseOneComposition()
    {
        Entry documents = Find("Pages/DocumentsPage.xaml", "LinkList");
        Entry messages = Find("Pages/CommunicationsPage.xaml", "MessageLinkList");

        Assert.Equal("Link", ProfileOf(documents)?.Id);
        Assert.Equal("Link", ProfileOf(messages)?.Id);

        foreach (Entry entry in (Entry[])[documents, messages])
        {
            Assert.Equal("Target", Accounting[AccountingKey(entry, Parse("{Binding TargetLabel}"))].RoleFrom);
        }

        DocumentLinkResponse document = new(Guid.CreateVersion7(), "Deal", Guid.CreateVersion7(), "Autumn slate", "note", DateTimeOffset.UnixEpoch, "Ben Okafor");
        MessageLinkResponse message = new(Guid.CreateVersion7(), "Deal", Guid.CreateVersion7(), "Autumn slate", "note", DateTimeOffset.UnixEpoch, "Ben Okafor");

        Assert.Equal("Deal: Autumn slate", Announce(documents, document));
        Assert.Equal("Deal: Autumn slate", Announce(messages, message));
    }

    // ------------------------------------------------------------ search

    /// <summary>A search hit's context and its match reason cannot answer for each other.</summary>
    [Fact]
    public void ASearchHitsContextAndMatchCannotAnswerForEachOther()
    {
        Entry search = Find("MainWindow.xaml", "SearchResults");
        List<Shown> primary = [.. Primaries(search)];
        Shown context = primary.Single(x => x.Path == "Subtitle");
        Shown matched = primary.Single(x => x.Path == "MatchedOn");

        SearchHit hit = new("Person", Guid.CreateVersion7(), "Ana Reyes", "Producer at Northgate", "Active", 0.9, "Prefix");
        string said = Announce(search, hit);

        Assert.Equal("Ana Reyes, Context: Producer at Northgate, Person, Matched: Prefix", said);
        Assert.Null(Coverage.Check(hit, context, primary, said));
        Assert.Null(Coverage.Check(hit, matched, primary, said));

        const string swapped = "Ana Reyes, Context: Prefix, Person, Matched: Producer at Northgate";

        Assert.NotNull(Coverage.Check(hit, context, primary, swapped));
        Assert.NotNull(Coverage.Check(hit, matched, primary, swapped));

        const string bare = "Ana Reyes, Producer at Northgate, Person, Prefix";

        Assert.NotNull(Coverage.Check(hit, context, primary, bare));
        Assert.NotNull(Coverage.Check(hit, matched, primary, bare));
    }

    /// <summary>A hit with no context says none, rather than an empty role.</summary>
    [Fact]
    public void ASearchHitWithoutContextSaysNone()
    {
        SearchHit hit = new("Company", Guid.CreateVersion7(), "Northgate", null, "Active", 0.5, "Exact");

        Assert.Equal("Northgate, Company, Matched: Exact", Announce(Find("MainWindow.xaml", "SearchResults"), hit));
    }
}
