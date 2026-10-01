using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AgencyOS.Canonical.Publisher.Json;

/// <summary>A JSON object whose keys serialise in RFC 8785 order (UTF-16 code units).</summary>
internal sealed class JsonMap : SortedDictionary<string, object>
{
    public JsonMap()
        : base(StringComparer.Ordinal)
    {
    }
}

/// <summary>
/// The JSON Canonicalization Scheme (RFC 8785), restricted to the values the contract
/// allows: strings, booleans, arrays and objects. No numbers and no <c>null</c>.
/// </summary>
internal static class CanonicalJson
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public static byte[] Serialize(object value)
    {
        StringBuilder builder = new();
        Write(builder, value);
        return Utf8.GetBytes(builder.ToString());
    }

    /// <summary>
    /// Parses JSON into the value tree, refusing anything the contract does not allow.
    /// Duplicate keys are refused, because canonicalisation would silently drop one.
    /// </summary>
    public static object Parse(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            throw new FormatException("The JSON starts with a byte-order mark.");
        }

        using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { AllowTrailingCommas = false });
        return Convert(document.RootElement);
    }

    private static object Convert(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString()!;

            case JsonValueKind.True:
                return true;

            case JsonValueKind.False:
                return false;

            case JsonValueKind.Array:
                return element.EnumerateArray().Select(Convert).ToList();

            case JsonValueKind.Object:
                JsonMap map = new();

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!map.TryAdd(property.Name, Convert(property.Value)))
                    {
                        throw new FormatException($"The key '{property.Name}' appears twice.");
                    }
                }

                return map;

            default:
                throw new FormatException($"A {element.ValueKind} value is not allowed: only strings, booleans, arrays and objects.");
        }
    }

    private static void Write(StringBuilder builder, object value)
    {
        switch (value)
        {
            case string text:
                WriteString(builder, text);
                break;

            case bool flag:
                builder.Append(flag ? "true" : "false");
                break;

            case JsonMap map:
                builder.Append('{');
                bool first = true;

                foreach (KeyValuePair<string, object> pair in map)
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    first = false;
                    WriteString(builder, pair.Key);
                    builder.Append(':');
                    Write(builder, pair.Value);
                }

                builder.Append('}');
                break;

            case IEnumerable<object> items:
                builder.Append('[');
                bool firstItem = true;

                foreach (object item in items)
                {
                    if (!firstItem)
                    {
                        builder.Append(',');
                    }

                    firstItem = false;
                    Write(builder, item);
                }

                builder.Append(']');
                break;

            default:
                throw new InvalidOperationException($"A {value.GetType().Name} cannot be written canonically.");
        }
    }

    private static void WriteString(StringBuilder builder, string text)
    {
        builder.Append('"');

        foreach (char c in text)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        builder.Append('"');
    }
}
