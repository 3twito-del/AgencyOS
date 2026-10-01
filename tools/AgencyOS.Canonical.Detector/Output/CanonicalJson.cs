using System.Globalization;
using System.Text;

namespace AgencyOS.Canonical.Detector.Output;

/// <summary>
/// A JSON object whose keys serialise in RFC 8785 order: UTF-16 code-unit order, which
/// is ordinal string order in .NET.
/// </summary>
internal sealed class JsonObject : SortedDictionary<string, object>
{
    public JsonObject()
        : base(StringComparer.Ordinal)
    {
    }
}

/// <summary>
/// Writes the detector's output in the JSON Canonicalization Scheme (RFC 8785), the
/// same serialisation the publisher contract uses for its payload.
/// </summary>
/// <remarks>
/// Values are strings, booleans, arrays and objects only. There are no numbers, so the
/// scheme's number-formatting rules never arise; a count is written as a decimal
/// string. There is no insignificant whitespace, and identical input gives identical
/// bytes.
/// </remarks>
internal static class CanonicalJson
{
    public static string Serialize(object value)
    {
        StringBuilder builder = new();
        Write(builder, value);
        return builder.ToString();
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

            case JsonObject map:
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
                throw new InvalidOperationException(
                    $"A {value.GetType().Name} cannot be written: detector output holds strings, booleans, arrays and objects only.");
        }
    }

    /// <summary>ECMAScript string escaping, as RFC 8785 section 3.2.2.2 requires.</summary>
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
