using System;
using System.Threading.Tasks;
using AgencyOS.Client;
using AgencyOS.Contracts.Representation;

namespace AgencyOS.Windows.Pages;

/// <summary>
/// Finds the representation a client's money hangs off.
/// </summary>
/// <remarks>
/// <para>
/// Commission - the rule and the entitlement alike - arises under a relationship,
/// not under a person. Asking an operator to paste a representation identifier
/// would be an invitation to attach a rate to the wrong one, so both surfaces that
/// need it look it up from the client instead (ADR-0023).
/// </para>
/// <para>
/// One copy, because Finance and Contracts both need the same answer and a second
/// edition of this would be free to drift. A person with no representation returns
/// empty rather than throwing: that is a fact the caller states to the operator,
/// not a failure.
/// </para>
/// </remarks>
internal static class RepresentationLookup
{
    internal static async Task<Guid> ForClientAsync(Guid personId)
    {
        if (AppServices.Api is not { } api || personId == Guid.Empty)
        {
            return Guid.Empty;
        }

        try
        {
            ClientOverviewResponse overview =
                await api.GetClientOverviewAsync(personId).ConfigureAwait(true);

            return overview.Representation?.Id ?? Guid.Empty;
        }
        catch (AgencyOsApiException)
        {
            return Guid.Empty;
        }
    }
}
