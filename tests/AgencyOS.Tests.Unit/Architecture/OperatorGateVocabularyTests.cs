using System.Text.RegularExpressions;
using AgencyOS.Domain.Finance;
using AgencyOS.Domain.Legal;
using Xunit;

namespace AgencyOS.Tests.Unit.Architecture;

/// <summary>
/// That the states the Windows client gates on are states the domain produces.
/// </summary>
/// <remarks>
/// <para>
/// Reality Closure wave 4 gave eight shipped operations the Windows routes they had
/// never had, and each route decides whether to offer itself by comparing a status
/// the server sent against a literal. That comparison is a seam: the status arrives
/// as a string, the literal is written by hand, and nothing in the compiler
/// connects either to the enumeration that produced it.
/// </para>
/// <para>
/// So a rename on the domain side - <c>Expected</c> to <c>Anticipated</c>, say -
/// would not fail a build. It would silently disable a control forever, and the
/// capability would go back to being unreachable with every test still green. That
/// is precisely the failure mode the census was convened to find, and this is the
/// cheapest possible guard against reintroducing it.
/// </para>
/// <para>
/// It asserts the states are <em>real</em>, not that the gate is <em>right</em>.
/// Whether an obligation should offer release is a domain question the server
/// answers on refusal; whether "Raised" is a thing an obligation can be is a fact,
/// and this checks the fact.
/// </para>
/// </remarks>
public sealed class OperatorGateVocabularyTests
{
    /// <summary>Each gate, and the enumeration whose names it is allowed to use.</summary>
    public static TheoryData<string, string, Type> Gates => new()
    {
        { "ContractsPage", "EffectiveDateButton.IsEnabled", typeof(ContractStatus) },
        { "ContractsPage", "ApproveButton.IsEnabled", typeof(ContractStatus) },
        { "ContractsPage", "QuantifyButton.IsEnabled", typeof(MonetaryObligationStatus) },
        { "ContractsPage", "ReleaseButton.IsEnabled", typeof(MonetaryObligationStatus) },
        { "FinancePage", "IssueInvoiceButton.IsEnabled", typeof(InvoiceStatus) },
        { "FinancePage", "VoidInvoiceButton.IsEnabled", typeof(InvoiceStatus) },
        { "FinancePage", "CancelReceivableButton.IsEnabled", typeof(ReceivableStatus) },
        { "FinancePage", "ReverseAllocationButton.IsEnabled", typeof(PaymentStatus) },
    };

    [Theory]
    [MemberData(nameof(Gates))]
    public void EveryGateNamesAStateTheDomainCanProduce(string page, string gate, Type states)
    {
        string statement = Statement(page, gate);

        string[] named =
        [
            .. Regex.Matches(statement, "\"([A-Za-z]+)\"").Select(x => x.Groups[1].Value),
        ];

        Assert.NotEmpty(named);

        foreach (string state in named)
        {
            Assert.Contains(state, Enum.GetNames(states));
        }
    }

    /// <summary>
    /// The eight restored acts gate on eight distinct records, not on one another.
    /// </summary>
    /// <remarks>
    /// An obligation, a receivable, an invoice and a payment are four aggregates
    /// with four lifecycles. A gate that read one aggregate's state to decide
    /// whether another may be acted on is the shape of finding F-01 - a surface
    /// asserting something the projection it read was not authoritative for.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Gates))]
    public void NoGateReadsAnotherAggregatesState(string page, string gate, Type states)
    {
        string statement = Statement(page, gate);

        foreach (Type other in (Type[])
                 [
                     typeof(ContractStatus), typeof(MonetaryObligationStatus),
                     typeof(InvoiceStatus), typeof(ReceivableStatus), typeof(PaymentStatus),
                 ])
        {
            if (other == states)
            {
                continue;
            }

            // Only names this enumeration alone holds can convict it. "Cancelled"
            // belongs to two of them, and a shared word proves nothing.
            foreach (string exclusive in Enum.GetNames(other)
                         .Except(Enum.GetNames(states))
                         .Where(x => !Shared(x, states, other)))
            {
                Assert.DoesNotContain($"\"{exclusive}\"", statement, StringComparison.Ordinal);
            }
        }
    }

    private static bool Shared(string name, Type states, Type other) =>
        Enum.GetNames(states).Contains(name) && Enum.GetNames(other).Contains(name);

    /// <summary>The one assignment, from the control's name to its semicolon.</summary>
    private static string Statement(string page, string gate)
    {
        string source = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "AgencyOS.Windows", "Pages", $"{page}.xaml.cs"));

        int start = source.IndexOf(gate, StringComparison.Ordinal);

        Assert.True(start > 0, $"{gate} is not assigned in {page}");

        int end = source.IndexOf(';', start);

        return source[start..end];
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
