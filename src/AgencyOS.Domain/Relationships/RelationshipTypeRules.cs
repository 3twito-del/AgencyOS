namespace AgencyOS.Domain.Relationships;

/// <summary>Kind of professional relationship.</summary>
/// <remarks>
/// Deliberately about how people and companies relate professionally, not about
/// representation. Representation is a richer concept with its own lifecycle and
/// arrives in M4; modelling it as a relationship type now would make it look
/// simpler than it is.
/// </remarks>
public enum RelationshipType
{
    /// <summary>The person works for the company.</summary>
    Employment = 1,

    /// <summary>A looser association than employment: adviser, affiliate, alumnus.</summary>
    Affiliation = 2,

    /// <summary>Two people who work together or alongside each other.</summary>
    Colleague = 3,

    /// <summary>One person introduced the other.</summary>
    Introduction = 4,

    /// <summary>Two people who have worked on something together.</summary>
    Collaboration = 5,

    /// <summary>One party advises the other.</summary>
    Advisor = 6,

    /// <summary>Legal representation of a party by a firm or lawyer.</summary>
    Counsel = 7,

    Other = 99,
}

/// <summary>Whether a relationship reads the same in both directions.</summary>
public enum RelationshipDirection
{
    /// <summary>From means something different to To, as in an introduction.</summary>
    Directed = 1,

    /// <summary>The relationship reads identically either way, as with colleagues.</summary>
    Mutual = 2,
}

/// <summary>Lifecycle state of a relationship.</summary>
public enum RelationshipStatus
{
    Active = 1,

    /// <summary>Over, but retained. History is never deleted.</summary>
    Ended = 2,
}

/// <summary>
/// Which endpoint pairings each relationship type accepts, and how it reads.
/// </summary>
/// <remarks>
/// These are semantic rules, so they live in the domain rather than in a check
/// constraint: the database has no way to know that employing yourself is
/// meaningless while advising yourself might one day not be (ADR-0011).
/// </remarks>
public static class RelationshipTypeRules
{
    /// <summary>
    /// Determines whether a type permits both endpoints to be the same party.
    /// </summary>
    /// <remarks>
    /// No M2 type does. The rule exists as a rule rather than as a blanket
    /// prohibition because the answer is per-type, and a future type - a person
    /// succeeding themselves in a role, say - would answer differently.
    /// </remarks>
    public static bool PermitsSelfReference(RelationshipType type) => type switch
    {
        _ => false,
    };

    /// <summary>Determines whether a type accepts this pairing of endpoint kinds.</summary>
    public static bool AllowsPairing(RelationshipType type, PartyKind from, PartyKind to) => type switch
    {
        // A person is employed by, or affiliated with, a company.
        RelationshipType.Employment or RelationshipType.Affiliation =>
            from == PartyKind.Person && to == PartyKind.Company,

        // Person-to-person only: these describe how two humans relate.
        RelationshipType.Colleague or RelationshipType.Introduction or RelationshipType.Collaboration =>
            from == PartyKind.Person && to == PartyKind.Person,

        // Advice and counsel flow from a person or firm to either kind of party.
        RelationshipType.Advisor or RelationshipType.Counsel => true,

        RelationshipType.Other => true,

        _ => false,
    };

    /// <summary>Gets how a type reads when the caller does not say.</summary>
    public static RelationshipDirection DefaultDirection(RelationshipType type) => type switch
    {
        RelationshipType.Colleague or RelationshipType.Collaboration => RelationshipDirection.Mutual,
        _ => RelationshipDirection.Directed,
    };

    /// <summary>
    /// Gets a human-readable description of the pairings a type accepts, for error
    /// messages that tell the caller what to do instead.
    /// </summary>
    public static string DescribeAllowedPairings(RelationshipType type) => type switch
    {
        RelationshipType.Employment or RelationshipType.Affiliation => "person to company",
        RelationshipType.Colleague or RelationshipType.Introduction or RelationshipType.Collaboration =>
            "person to person",
        _ => "any pairing of person and company",
    };
}
