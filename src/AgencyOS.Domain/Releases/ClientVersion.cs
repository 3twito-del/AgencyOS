using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace AgencyOS.Domain.Releases;

/// <summary>
/// A client build version, ordered by Semantic Versioning 2.0.0 precedence.
/// </summary>
/// <remarks>
/// <para>
/// Owned rather than taken from a package because ordering here is a
/// security-relevant decision: "is this client below the minimum supported
/// version?" is the question that decides whether a build may write canonical
/// data. A subtly wrong comparison silently admits a client that should have been
/// refused, so the rule is written out and tested directly.
/// </para>
/// <para>
/// Build metadata after '+' is ignored for precedence, per the specification.
/// </para>
/// </remarks>
public sealed class ClientVersion : IComparable<ClientVersion>, IEquatable<ClientVersion>
{
    private ClientVersion(int major, int minor, int patch, string[] prerelease, string text)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        _prerelease = prerelease;
        Text = text;
    }

    private readonly string[] _prerelease;

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    /// <summary>Gets a value indicating whether this is a prerelease version.</summary>
    public bool IsPrerelease => _prerelease.Length > 0;

    /// <summary>Gets the version as originally written, without build metadata.</summary>
    public string Text { get; }

    public static bool TryParse(string? value, [NotNullWhen(true)] out ClientVersion? version)
    {
        version = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string text = value.Trim();

        // Build metadata does not participate in precedence.
        int plus = text.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            text = text[..plus];
        }

        string core = text;
        string[] prerelease = [];

        int dash = text.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            core = text[..dash];
            string tail = text[(dash + 1)..];

            if (tail.Length == 0)
            {
                return false;
            }

            prerelease = tail.Split('.');

            foreach (string identifier in prerelease)
            {
                if (identifier.Length == 0)
                {
                    return false;
                }
            }
        }

        string[] parts = core.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!TryParseComponent(parts[0], out int major)
            || !TryParseComponent(parts[1], out int minor)
            || !TryParseComponent(parts[2], out int patch))
        {
            return false;
        }

        version = new ClientVersion(major, minor, patch, prerelease, text);
        return true;
    }

    /// <summary>Parses a version, throwing when it is not well formed.</summary>
    public static ClientVersion Parse(string value)
    {
        return TryParse(value, out ClientVersion? version)
            ? version
            : throw new FormatException($"'{value}' is not a valid client version.");
    }

    public int CompareTo(ClientVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        int result = Major.CompareTo(other.Major);
        if (result != 0)
        {
            return result;
        }

        result = Minor.CompareTo(other.Minor);
        if (result != 0)
        {
            return result;
        }

        result = Patch.CompareTo(other.Patch);
        if (result != 0)
        {
            return result;
        }

        // A prerelease version has lower precedence than the release it precedes.
        if (_prerelease.Length == 0 && other._prerelease.Length == 0)
        {
            return 0;
        }

        if (_prerelease.Length == 0)
        {
            return 1;
        }

        if (other._prerelease.Length == 0)
        {
            return -1;
        }

        return ComparePrerelease(_prerelease, other._prerelease);
    }

    private static int ComparePrerelease(string[] left, string[] right)
    {
        int shared = Math.Min(left.Length, right.Length);

        for (int i = 0; i < shared; i++)
        {
            bool leftNumeric = TryParseComponent(left[i], out int leftValue);
            bool rightNumeric = TryParseComponent(right[i], out int rightValue);

            if (leftNumeric && rightNumeric)
            {
                int numeric = leftValue.CompareTo(rightValue);
                if (numeric != 0)
                {
                    return numeric;
                }

                continue;
            }

            // Numeric identifiers always have lower precedence than alphanumeric ones.
            if (leftNumeric)
            {
                return -1;
            }

            if (rightNumeric)
            {
                return 1;
            }

            int lexical = string.CompareOrdinal(left[i], right[i]);
            if (lexical != 0)
            {
                return lexical < 0 ? -1 : 1;
            }
        }

        // All shared identifiers equal: the shorter set has lower precedence.
        return left.Length.CompareTo(right.Length);
    }

    private static bool TryParseComponent(string value, out int parsed)
    {
        // Leading zeroes are not valid numeric identifiers in SemVer.
        if (value.Length > 1 && value[0] == '0')
        {
            parsed = 0;
            return false;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed);
    }

    public bool Equals(ClientVersion? other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is ClientVersion other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(Major);
        hash.Add(Minor);
        hash.Add(Patch);

        foreach (string identifier in _prerelease)
        {
            hash.Add(identifier, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => Text;

    public static bool operator ==(ClientVersion? left, ClientVersion? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(ClientVersion? left, ClientVersion? right) => !(left == right);

    public static bool operator <(ClientVersion? left, ClientVersion? right) =>
        Compare(left, right) < 0;

    public static bool operator <=(ClientVersion? left, ClientVersion? right) =>
        Compare(left, right) <= 0;

    public static bool operator >(ClientVersion? left, ClientVersion? right) =>
        Compare(left, right) > 0;

    public static bool operator >=(ClientVersion? left, ClientVersion? right) =>
        Compare(left, right) >= 0;

    private static int Compare(ClientVersion? left, ClientVersion? right)
    {
        if (left is null)
        {
            return right is null ? 0 : -1;
        }

        return left.CompareTo(right);
    }
}
