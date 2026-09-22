using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using AgencyOS.Client.Presentation;
using AgencyOS.Contracts.Deals;
using AgencyOS.Contracts.Legal;
using AgencyOS.Contracts.Finance;
using AgencyOS.Contracts.Intelligence;
using AgencyOS.Contracts.Opportunities;
using AgencyOS.Contracts.Organizations;
using AgencyOS.Contracts.PeopleSlice;
using AgencyOS.Contracts.Projects;
using AgencyOS.Contracts.Representation;

namespace AgencyOS.Client.ViewModels;

/// <summary>
/// One record an operator can choose, carrying the identifier they never see.
/// </summary>
/// <remarks>
/// <para>
/// <c>AOS-R001-006</c>: twenty fields across thirteen dialogs asked a person to
/// type an identifier the product displays nowhere. This is what replaced them.
/// The operator reads <see cref="Label"/>; the command carries <see cref="Id"/>,
/// exactly as the server issued it.
/// </para>
/// <para>
/// <strong>Deliberately not a picker framework.</strong> There is no way to hand
/// this an endpoint, a display function or an untyped object. Every entity kind
/// gets its own factory below, each naming in one place the fields that
/// distinguish one record from another — so "which columns identify a person" is
/// a decision written down once and reviewable, rather than a lambda passed in at
/// each of eleven call sites (§6).
/// </para>
/// <para>
/// The labels answer §8: a name alone is not unique. Two people called Ada Sallow
/// are told apart by where they work, two materials by their type and version,
/// two deals by who is on the other side. Only data the product already shows is
/// used — nothing internal is exposed to disambiguate.
/// </para>
/// </remarks>
/// <param name="Id">The canonical identifier, exactly as the server issued it.</param>
/// <param name="Label">What the operator reads. Never an identifier.</param>
public sealed record EntityChoice(Guid Id, string Label)
{
    /// <summary>People, told apart by where they work or what they do.</summary>
    public static IReadOnlyList<EntityChoice> ForPeople(
        IReadOnlyList<PersonSummaryResponse> people)
    {
        ArgumentNullException.ThrowIfNull(people);

        return [.. people.Select(x => new EntityChoice(
            x.Id,
            Join(x.DisplayName, x.PrimaryCompanyName ?? x.Title)))];
    }

    /// <summary>
    /// People in this organization, told apart by what they do in it.
    /// </summary>
    /// <remarks>
    /// <c>AOS-R001-006</c>. The owner and lead fields asked an operator to type an
    /// internal user identifier. The directory that answers "who is in this
    /// organization" already exists and is what the representation team picker
    /// uses, so these choose from it rather than from nothing.
    /// </remarks>
    public static IReadOnlyList<EntityChoice> ForMembers(
        IReadOnlyList<OrganizationMemberResponse> members)
    {
        ArgumentNullException.ThrowIfNull(members);

        return [.. members.Select(x => new EntityChoice(x.UserId, Join(x.DisplayName, x.Role)))];
    }

    /// <summary>Submissions, told apart by who they went to and when.</summary>
    public static IReadOnlyList<EntityChoice> ForSubmissions(
        IReadOnlyList<SubmissionResponse> submissions)
    {
        ArgumentNullException.ThrowIfNull(submissions);

        return [.. submissions.Select(x => new EntityChoice(
            x.Id,
            Join(x.TargetDisplayName, x.SentAt.ToString("d", CultureInfo.CurrentCulture))))];
    }

    /// <summary>Offers, told apart by which way they went and where they stand.</summary>
    public static IReadOnlyList<EntityChoice> ForOffers(IReadOnlyList<OfferResponse> offers)
    {
        ArgumentNullException.ThrowIfNull(offers);

        return [.. offers.Select(x => new EntityChoice(
            x.Id,
            Join(
                "Offer " + x.Sequence.ToString(CultureInfo.CurrentCulture),
                x.Direction,
                x.Status)))];
    }

    /// <summary>Contracts, told apart by the deal they paper.</summary>
    public static IReadOnlyList<EntityChoice> ForContracts(
        IReadOnlyList<ContractSummaryResponse> contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        return [.. contracts.Select(x => new EntityChoice(x.Id, Join(x.Title, x.Kind, x.Status)))];
    }

    /// <summary>Invoices, told apart by who owes and against what.</summary>
    public static IReadOnlyList<EntityChoice> ForInvoices(IReadOnlyList<InvoiceResponse> invoices)
    {
        ArgumentNullException.ThrowIfNull(invoices);

        return [.. invoices.Select(x => new EntityChoice(
            x.Id, Join(x.Reference ?? x.ContractTitle, x.DebtorDisplayName, x.Status)))];
    }

    /// <summary>Payments, told apart by who paid whom.</summary>
    public static IReadOnlyList<EntityChoice> ForPayments(IReadOnlyList<PaymentResponse> payments)
    {
        ArgumentNullException.ThrowIfNull(payments);

        return [.. payments.Select(x => new EntityChoice(
            x.Id, Join(x.PayerDisplayName, x.PayeeDisplayName, x.Direction)))];
    }

    /// <summary>Companies, told apart by what kind of company they are.</summary>
    public static IReadOnlyList<EntityChoice> ForCompanies(
        IReadOnlyList<CompanySummaryResponse> companies)
    {
        ArgumentNullException.ThrowIfNull(companies);

        return [.. companies.Select(x => new EntityChoice(x.Id, Join(x.Name, x.Type)))];
    }

    /// <summary>Projects, told apart by type and year.</summary>
    public static IReadOnlyList<EntityChoice> ForProjects(
        IReadOnlyList<ProjectSummaryResponse> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);

        return [.. projects.Select(x => new EntityChoice(
            x.Id,
            Join(x.Title, x.Type, x.Year?.ToString(System.Globalization.CultureInfo.CurrentCulture))))];
    }

    /// <summary>Packages, told apart by the project they are built from.</summary>
    public static IReadOnlyList<EntityChoice> ForPackages(
        IReadOnlyList<PackageSummaryResponse> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);

        return [.. packages.Select(x => new EntityChoice(
            x.Id, Join(x.Name, x.ProjectTitle, x.Status)))];
    }

    /// <summary>
    /// The roles of one project, told apart by what the role is for.
    /// </summary>
    /// <remarks>
    /// Not a tenant-wide list: a role belongs to its project, and there is no
    /// endpoint that returns every role in the organization.
    /// </remarks>
    public static IReadOnlyList<EntityChoice> ForProjectRoles(
        IReadOnlyList<ProjectRoleResponse> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        return [.. roles.Select(x => new EntityChoice(x.Id, Join(x.Type, x.Label, x.Status)))];
    }

    /// <summary>Attachments on a project, told apart by the role they fill.</summary>
    public static IReadOnlyList<EntityChoice> ForAttachments(
        IReadOnlyList<AttachmentResponse> attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);

        return [.. attachments.Select(x => new EntityChoice(
            x.Id, Join(x.DisplayName, x.RoleLabel ?? x.RoleType, x.Status)))];
    }

    /// <summary>A project's materials, told apart by whose they are.</summary>
    public static IReadOnlyList<EntityChoice> ForProjectMaterials(
        IReadOnlyList<ProjectMaterialResponse> materials)
    {
        ArgumentNullException.ThrowIfNull(materials);

        return [.. materials.Select(x => new EntityChoice(
            x.MaterialId, Join(x.Title, x.Type, x.PersonName)))];
    }

    /// <summary>Source properties, told apart by type and who wrote them.</summary>
    public static IReadOnlyList<EntityChoice> ForSourceProperties(
        IReadOnlyList<SourcePropertyResponse> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        return [.. properties.Select(x => new EntityChoice(
            x.Id, Join(x.Title, x.Type, x.AttributedCreator)))];
    }

    /// <summary>Talent profiles, told apart by the person they belong to.</summary>
    public static IReadOnlyList<EntityChoice> ForTalent(
        IReadOnlyList<TalentSummaryResponse> talent)
    {
        ArgumentNullException.ThrowIfNull(talent);

        return [.. talent.Select(x => new EntityChoice(
            x.Id, Join(x.DisplayName, x.CareerStage, x.RepresentationStatus)))];
    }

    /// <summary>Opportunities, told apart by kind and where they stand.</summary>
    public static IReadOnlyList<EntityChoice> ForOpportunities(
        IReadOnlyList<OpportunitySummaryResponse> opportunities)
    {
        ArgumentNullException.ThrowIfNull(opportunities);

        return [.. opportunities.Select(x => new EntityChoice(
            x.Id, Join(x.Name, x.Kind, x.Status)))];
    }

    /// <summary>
    /// The targets of one opportunity, told apart by how far each has got.
    /// </summary>
    /// <remarks>
    /// Not a tenant-wide list: a target belongs to its opportunity, and the set is
    /// derived from whichever opportunity is currently chosen.
    /// </remarks>
    public static IReadOnlyList<EntityChoice> ForTargets(
        IReadOnlyList<OpportunityTargetResponse> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        // The contact carries its role here too: the label already holds the
        // company and the stage, so a third bare value left the operator to guess
        // whether the name was the counterparty's or ours.
        return [.. targets.Select(x => new EntityChoice(
            x.Id, Join(x.DisplayName, x.Stage, TargetLine.Contact(x))))];
    }

    /// <summary>Deals, told apart by who is on the other side.</summary>
    public static IReadOnlyList<EntityChoice> ForDeals(
        IReadOnlyList<DealSummaryResponse> deals)
    {
        ArgumentNullException.ThrowIfNull(deals);

        return [.. deals.Select(x => new EntityChoice(
            x.Id, Join(x.Name, x.CounterpartyDisplayName, x.Status)))];
    }

    /// <summary>Materials, told apart by type and version.</summary>
    /// <remarks>
    /// The version matters most here. Two drafts of one script are the same title
    /// and are not the same thing to send somebody.
    /// </remarks>
    public static IReadOnlyList<EntityChoice> ForMaterials(
        IReadOnlyList<MaterialResponse> materials)
    {
        ArgumentNullException.ThrowIfNull(materials);

        return [.. materials.Select(x => new EntityChoice(
            x.Id, Join(x.Title, x.Type, x.VersionLabel)))];
    }

    /// <summary>Intelligence sources, told apart by what kind of evidence they are.</summary>
    public static IReadOnlyList<EntityChoice> ForSources(
        IReadOnlyList<IntelligenceSourceResponse> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        return [.. sources.Select(x => new EntityChoice(x.Id, Join(x.Title, x.Kind)))];
    }

    /// <summary>Signals, told apart by kind and how well established they are.</summary>
    public static IReadOnlyList<EntityChoice> ForSignals(
        IReadOnlyList<SignalResponse> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);

        return [.. signals.Select(x => new EntityChoice(
            x.Id, Join(x.Title, x.Kind, x.Verification)))];
    }

    /// <summary>Theses, told apart by where they stand.</summary>
    public static IReadOnlyList<EntityChoice> ForTheses(
        IReadOnlyList<ThesisResponse> theses)
    {
        ArgumentNullException.ThrowIfNull(theses);

        return [.. theses.Select(x => new EntityChoice(x.Id, Join(x.Title, x.Status)))];
    }

    /// <summary>Predictions, told apart by their standing and the date they resolve.</summary>
    public static IReadOnlyList<EntityChoice> ForPredictions(
        IReadOnlyList<PredictionResponse> predictions)
    {
        ArgumentNullException.ThrowIfNull(predictions);

        return [.. predictions.Select(x => new EntityChoice(
            x.Id,
            Join(
                x.Statement,
                x.Status,
                "resolves " + x.ResolvesBy.ToString("d MMM yyyy", System.Globalization.CultureInfo.CurrentCulture))))];
    }

    /// <summary>Tasks, told apart by their state.</summary>
    public static IReadOnlyList<EntityChoice> ForTasks(
        IReadOnlyList<Contracts.PeopleSlice.TaskResponse> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        return [.. tasks.Select(x => new EntityChoice(x.Id, Join(x.Title, x.State)))];
    }

    /// <summary>What the operator reads.</summary>
    public override string ToString() => Label;

    /// <summary>
    /// The parts of a label, with the empty ones left out.
    /// </summary>
    /// <remarks>
    /// A middle dot rather than a comma, because several of these labels contain
    /// commas of their own — a company called "Sallow, Rowan and Partners" would
    /// otherwise read as two records.
    /// </remarks>
    private static string Join(params string?[] parts) =>
        string.Join(" · ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
}
