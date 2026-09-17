using System;
using System.Collections.Generic;
using System.Linq;
using AgencyOS.Contracts.Opportunities;
using AgencyOS.Contracts.Representation;

namespace AgencyOS.Client.ViewModels;

/// <summary>
/// Whose materials a pursuit can show: the people behind its talent subjects.
/// </summary>
/// <remarks>
/// <para>
/// A pursuit names talent by its <em>profile</em>. Materials are filed against a
/// <em>person</em>. The two identifiers are different values, and Repair Wave 003B
/// asked for materials with the profile's — which the server answered, correctly,
/// with nothing, for every pursuit there is (<c>AOS-R002-023</c>). The material
/// pickers in <c>RecordPitchDialog</c> and <c>RecordSubmissionDialog</c> therefore
/// only ever offered "nothing".
/// </para>
/// <para>
/// Talent is the only subject kind that is somebody. A project, a package and a
/// project role are not people whose reel one sends, and the server accepts no
/// other kind.
/// </para>
/// </remarks>
public static class SubjectMaterials
{
    /// <summary>The subject kind that names a person's talent profile.</summary>
    public const string TalentProfile = "TalentProfile";

    /// <summary>Whether any subject of the pursuit is somebody who has materials.</summary>
    /// <param name="subjects">The pursuit's subjects.</param>
    /// <returns>True when at least one subject is a talent profile.</returns>
    public static bool NamesTalent(IReadOnlyList<OpportunitySubjectResponse> subjects)
    {
        ArgumentNullException.ThrowIfNull(subjects);

        return subjects.Any(x => x.Kind == TalentProfile);
    }

    /// <summary>The people whose materials the pursuit can show, once each.</summary>
    /// <param name="subjects">The pursuit's subjects.</param>
    /// <param name="talent">The organization's talent roster.</param>
    /// <returns>
    /// Person identifiers, in subject order. A profile the roster does not list is
    /// left out rather than guessed at.
    /// </returns>
    public static IReadOnlyList<Guid> People(
        IReadOnlyList<OpportunitySubjectResponse> subjects,
        IReadOnlyList<TalentSummaryResponse> talent)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(talent);

        Dictionary<Guid, Guid> personByProfile = talent
            .GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => x.First().PersonId);

        return
        [
            .. subjects
                .Where(x => x.Kind == TalentProfile)
                .Select(x => personByProfile.TryGetValue(x.TargetId, out Guid person) ? person : Guid.Empty)
                .Where(x => x != Guid.Empty)
                .Distinct(),
        ];
    }
}
