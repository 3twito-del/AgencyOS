using System.Xml.Linq;
using Xunit;

namespace AgencyOS.Tests.Windows.Presentation;

/// <summary>
/// That every surface listing tasks says the same things by the same means.
/// </summary>
/// <remarks>
/// <para>
/// Six surfaces list tasks and five decided independently what a row should show.
/// The result was that none showed an owner or a due date, the caption slot meant
/// "priority" on three surfaces and "a person's name" on another, and the person
/// it showed was the task's subject rather than whoever was accountable for it.
/// </para>
/// <para>
/// These assert the markup's <em>semantics</em>, not its layout: that each surface
/// asks the shared wording for who and when, and that no template puts a bare
/// identity on a task row. Layouts may differ where the context differs.
/// <c>TaskLineTests</c> covers what the shared wording decides.
/// </para>
/// </remarks>
public sealed class TaskSurfaceSemanticsTests
{
    /// <summary>Every markup file that lists tasks, and the element that does it.</summary>
    public static TheoryData<string, string> Surfaces => new()
    {
        { "App.xaml", "TaskTemplate" },                 // Command Center, three lists
        { "Pages/TalentPage.xaml", "TaskList" },
        { "Pages/DealsPage.xaml", "TaskList" },
        { "Pages/ContractsPage.xaml", "TaskList" },
        { "Pages/PipelinePage.xaml", "TaskList" },
        { "Pages/IntelligencePage.xaml", "ResearchTaskList" },
    };

    /// <summary>Each surface asks the shared wording who owns the work.</summary>
    [Theory]
    [MemberData(nameof(Surfaces))]
    public void EveryTaskSurfaceAsksWhoOwnsIt(string file, string element)
    {
        string markup = Markup(file);

        Assert.True(
            markup.Contains("TaskWho", StringComparison.Ordinal),
            $"{file} lists tasks in '{element}' and never asks who owns them. "
                + "A task list that cannot say who is accountable is an index, not a "
                + "working surface.");
    }

    /// <summary>Each surface asks the shared wording when the work is due.</summary>
    [Theory]
    [MemberData(nameof(Surfaces))]
    public void EveryTaskSurfaceAsksWhenItIsDue(string file, string element)
    {
        Assert.True(
            Markup(file).Contains("TaskWhen", StringComparison.Ordinal),
            $"{file} lists tasks in '{element}' and never says when they are due.");
    }

    /// <summary>
    /// No task row renders a bare identity.
    /// </summary>
    /// <remarks>
    /// The Command Center bound <c>Subject.Name</c> directly, unlabelled, in the
    /// position a byline occupies. A blind operator read it as the owner. The
    /// subject is useful and stays — through <c>TaskAbout</c>, which labels it.
    /// </remarks>
    [Fact]
    public void NoTaskRowShowsAnUnlabelledIdentity()
    {
        foreach ((string file, _) in Surfaces.Select(x => ((string)x[0], (string)x[1])))
        {
            Assert.DoesNotContain(
                "Binding Subject.Name",
                Markup(file),
                StringComparison.Ordinal);
        }
    }

    /// <summary>The shared wording is registered once, for every surface to use.</summary>
    [Fact]
    public void TheSharedWordingIsRegisteredOnce()
    {
        string app = Markup("App.xaml");

        foreach (string key in new[] { "TaskWho", "TaskWhen", "TaskAbout" })
        {
            Assert.Contains($"x:Key=\"{key}\"", app, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The Command Center row carries the subject and the owner, distinctly.
    /// </summary>
    /// <remarks>
    /// Two labelled identities cannot be confused for one another the way one
    /// unlabelled identity could. This is the row the blind operator misread.
    /// </remarks>
    [Fact]
    public void TheCommandCentreRowSeparatesSubjectFromOwner()
    {
        XElement template = Assert.Single(
            XElement.Load(Path(("App.xaml"))).Descendants(),
            x => x.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))
                ?.Value == "TaskTemplate");

        string markup = template.ToString();

        Assert.Contains("TaskAbout", markup, StringComparison.Ordinal);
        Assert.Contains("TaskWho", markup, StringComparison.Ordinal);
        Assert.Contains("TaskWhen", markup, StringComparison.Ordinal);
    }

    private static string Markup(string file) => File.ReadAllText(Path(file));

    private static string Path(string file) =>
        System.IO.Path.Combine(RepositoryRoot, "src", "AgencyOS.Windows", file);

    private static string RepositoryRoot { get; } = Find();

    private static string Find()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "AgencyOS.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root was not found.");
    }
}
