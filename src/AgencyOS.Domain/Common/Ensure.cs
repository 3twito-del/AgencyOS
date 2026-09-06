namespace AgencyOS.Domain.Common;

/// <summary>Guard helpers that fail as <see cref="DomainException"/> rather than as argument errors.</summary>
internal static class Ensure
{
    internal static string NotBlank(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{name} must not be blank.");
        }

        return value.Trim();
    }

    internal static string NotBlankMax(string? value, string name, int maxLength)
    {
        string trimmed = NotBlank(value, name);

        if (trimmed.Length > maxLength)
        {
            throw new DomainException($"{name} must be at most {maxLength} characters.");
        }

        return trimmed;
    }

    internal static string? OptionalMax(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return NotBlankMax(value, name, maxLength);
    }
}
