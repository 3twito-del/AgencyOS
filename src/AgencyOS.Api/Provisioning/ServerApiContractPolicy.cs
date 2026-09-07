using AgencyOS.Application.Abstractions;
using AgencyOS.Contracts;

namespace AgencyOS.Api.Provisioning;

/// <summary>
/// The contract range this build speaks, taken from the contracts assembly.
/// </summary>
/// <remarks>
/// Currently a single version. When the server begins supporting a range - the
/// normal case during a contract migration - it widens here, in one place, rather
/// than at each site that needs to know.
/// </remarks>
internal sealed class ServerApiContractPolicy : IApiContractPolicy
{
    public int Minimum => ApiContract.Current;

    public int Maximum => ApiContract.Current;
}
