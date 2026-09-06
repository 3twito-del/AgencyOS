using AgencyOS.Domain.Common;
using AgencyOS.Domain.Identity;

namespace AgencyOS.Domain.Organizations;

/// <summary>
/// A company or other body AgencyOS holds records about, including the agency itself.
/// </summary>
/// <remarks>
/// Shape follows the Organization entity in <c>docs/08_DATA_MODEL_FOUNDATION.md</c>.
/// Archival is a command rather than a status field edit, and it is not a delete:
/// historical truth is preserved.
/// </remarks>
public sealed class Organization
{
    private Organization()
    {
    }

    public OrganizationId Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? LegalName { get; private set; }

    public OrganizationType Type { get; private set; }

    public OrganizationStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public UserId CreatedBy { get; private set; }

    public static Organization Create(
        string name,
        string? legalName,
        OrganizationType type,
        UserId createdBy,
        DateTimeOffset now)
    {
        return new Organization
        {
            Id = OrganizationId.New(),
            Name = Ensure.NotBlankMax(name, nameof(name), 256),
            LegalName = Ensure.OptionalMax(legalName, nameof(legalName), 256),
            Type = type,
            Status = OrganizationStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = createdBy,
        };
    }

    public void Rename(string name, DateTimeOffset now)
    {
        Name = Ensure.NotBlankMax(name, nameof(name), 256);
        UpdatedAt = now;
    }

    /// <summary>Archives the organization. Archival preserves history; it is not a delete.</summary>
    public void Archive(DateTimeOffset now)
    {
        if (Status == OrganizationStatus.Archived)
        {
            throw new DomainException("Organization is already archived.");
        }

        Status = OrganizationStatus.Archived;
        UpdatedAt = now;
    }
}
