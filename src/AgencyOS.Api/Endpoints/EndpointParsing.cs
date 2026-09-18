using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Domain.Common;
using AgencyOS.Domain.Companies;
using AgencyOS.Domain.People;
using AgencyOS.Domain.Relationships;

namespace AgencyOS.Api.Endpoints;

/// <summary>
/// Turns wire values into domain values, failing with a message that says what
/// was expected.
/// </summary>
/// <remarks>
/// Enums cross the wire as names rather than numbers, so a stored or logged
/// request stays readable and a renumbering cannot silently change meaning.
/// </remarks>
internal static class EndpointParsing
{
    /// <summary>Parses an enum by name, case-insensitively.</summary>
    public static TEnum ParseEnum<TEnum>(string value, string field)
        where TEnum : struct, Enum
    {
        if (!Enum.TryParse(value, ignoreCase: true, out TEnum parsed) || !Enum.IsDefined(parsed))
        {
            throw new DomainException(
                $"{field} '{value}' is not valid. Expected one of: {string.Join(", ", Enum.GetNames<TEnum>())}.");
        }

        return parsed;
    }

    /// <summary>Parses an optional enum, falling back to a default.</summary>
    public static TEnum ParseEnumOrDefault<TEnum>(string? value, string field, TEnum fallback)
        where TEnum : struct, Enum
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : ParseEnum<TEnum>(value, field);
    }

    /// <summary>Parses an optional enum, returning null when absent.</summary>
    public static TEnum? ParseNullableEnum<TEnum>(string? value, string field)
        where TEnum : struct, Enum
    {
        return string.IsNullOrWhiteSpace(value) ? null : ParseEnum<TEnum>(value, field);
    }

    /// <summary>
    /// Turns a wire party reference into a relationship endpoint.
    /// </summary>
    /// <remarks>
    /// A caller who omits the object entirely is refused the same way a caller who
    /// sends an unknown enum name is: a <see cref="DomainException"/> naming the
    /// field, which the handler answers as a <c>400</c>. It used to be
    /// <c>ArgumentNullException.ThrowIfNull</c> — a programmer-error exception for
    /// something only a caller can cause — and that answered <c>500</c> with a
    /// trace id (<c>AOS-R002-025</c>).
    /// </remarks>
    public static RelationshipEndpoint ToEndpoint(PartyRefRequest party, string field)
    {
        if (party is null)
        {
            throw new DomainException($"{field} is required.");
        }

        PartyKind kind = ParseEnum<PartyKind>(party.Kind, $"{field}.Kind");

        return kind == PartyKind.Person
            ? RelationshipEndpoint.ForPerson(new PersonId(party.Id))
            : RelationshipEndpoint.ForCompany(new CompanyId(party.Id));
    }

    /// <summary>Turns an optional wire party reference into an endpoint.</summary>
    public static RelationshipEndpoint? ToEndpointOrNull(PartyRefRequest? party, string field) =>
        party is null ? null : ToEndpoint(party, field);
}
