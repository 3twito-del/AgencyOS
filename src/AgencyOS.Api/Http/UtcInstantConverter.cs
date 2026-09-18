using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgencyOS.Api.Http;

/// <summary>
/// Reads a timestamp as the instant it denotes, whatever offset it was written
/// with, and keeps it in UTC from there on.
/// </summary>
/// <remarks>
/// <para>
/// <c>AOS-R002-001</c>. A client in Tel Aviv sends
/// <c>2026-09-18T03:13:39+03:00</c>; a client in Los Angeles sends
/// <c>2026-09-17T17:13:39-07:00</c>. Both are the same instant and both are legal
/// in the contract, which types these fields as <c>DateTimeOffset</c>. Neither
/// could be saved: the value travelled through binding, the command and the
/// domain untouched, and Npgsql refused it at the last step — "Cannot write
/// DateTimeOffset with Offset=03:00:00 to PostgreSQL type 'timestamp with time
/// zone', only offset 0 (UTC) is supported" — which arrived at the operator as an
/// unexplained <c>500</c>. The whole create path was unusable for anybody outside
/// UTC, which is most people.
/// </para>
/// <para>
/// The instant is what the domain means and what <c>timestamptz</c> stores, so
/// converting is lossless: <c>ToUniversalTime</c> keeps the moment and changes
/// only how it is written down. It happens here, once, where an outside
/// representation becomes an internal value — not in a dialog, not in each
/// endpoint, and not by rewriting the text of the timestamp.
/// </para>
/// <para>
/// Calendar dates are not affected: a business date is a <c>DateOnly</c> against
/// a <c>date</c> column all the way through, and converting one through a
/// timezone is exactly the error this converter exists to avoid making.
/// </para>
/// </remarks>
internal sealed class UtcInstantConverter : JsonConverter<DateTimeOffset>
{
    /// <inheritdoc />
    /// <remarks>
    /// A malformed timestamp still throws, so the framework answers <c>400</c>
    /// rather than letting a bad value travel.
    /// </remarks>
    public override DateTimeOffset Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset().ToUniversalTime();

    /// <inheritdoc />
    /// <remarks>
    /// Responses say the same instant the same way, so two servers cannot describe
    /// one moment differently. Stored instants are already UTC, so this changes no
    /// response the product sends today.
    /// </remarks>
    public override void Write(
        Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(value.ToUniversalTime());
    }
}
