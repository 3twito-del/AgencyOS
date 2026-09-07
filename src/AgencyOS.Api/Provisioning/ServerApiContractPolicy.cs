using AgencyOS.Application.Abstractions;
using AgencyOS.Contracts;

namespace AgencyOS.Api.Provisioning;

/// <summary>
/// The contract range this build speaks, taken from the contracts assembly.
/// </summary>
/// <remarks>
/// A real range since M2. Version 2 added the people slice additively over
/// version 1, so a contract-1 client is still served: it simply does not call the
/// new endpoints. Narrowing the range is what locks a client out, and that should
/// happen only when an older client becomes genuinely unsafe.
/// </remarks>
internal sealed class ServerApiContractPolicy : IApiContractPolicy
{
    public int Minimum => ApiContract.MinimumSupported;

    public int Maximum => ApiContract.Current;
}
