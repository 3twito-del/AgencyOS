using System.Reflection;
using System.Runtime.CompilerServices;
using AgencyOS.Application;
using AgencyOS.Application.Ai;
using AgencyOS.Application.Ai.Tools;
using AgencyOS.Domain.Ai;
using Xunit;

namespace AgencyOS.Tests.Unit.Ai;

/// <summary>
/// The shape of the AI write surface, asserted rather than intended.
/// </summary>
/// <remarks>
/// <para>
/// Every rule here is one a future change could break by writing perfectly
/// reasonable code. Adding a tool that bypasses the base class, registering a
/// second canonical write "while we are in here", or handing a tool a
/// <c>DbContext</c> because it needed one field — none of those would fail a
/// behavioural test, and all of them would quietly widen what a model can reach
/// (§31, §32).
/// </para>
/// <para>
/// Instances are created uninitialized on purpose. Every property read below is a
/// constant expression on the type, so no constructor is needed and none of the
/// tools' dependencies has to be stood up to ask what a tool <em>is</em>.
/// </para>
/// </remarks>
public sealed class AiArchitectureTests
{
    private static readonly IReadOnlyList<Type> ToolTypes =
    [
        .. ApplicationAssembly.Value
            .GetTypes()
            .Where(x => typeof(IAiTool).IsAssignableFrom(x) && x is { IsAbstract: false, IsInterface: false })
            .OrderBy(x => x.Name, StringComparer.Ordinal),
    ];

    /// <summary>
    /// Every tool goes through the base class, so the permission re-check cannot
    /// be skipped by forgetting.
    /// </summary>
    /// <remarks>
    /// A tool that implemented <see cref="IAiTool"/> directly would be a confused
    /// deputy, and the failure would look exactly like the tool working (§9).
    /// </remarks>
    [Fact]
    public void EveryToolDerivesFromTheBaseClass()
    {
        Assert.NotEmpty(ToolTypes);
        Assert.All(ToolTypes, x => Assert.True(
            typeof(AiToolBase).IsAssignableFrom(x),
            $"{x.Name} implements IAiTool without deriving from AiToolBase, so its "
                + "permission re-check is its own business."));
    }

    /// <summary>The re-check cannot be overridden away.</summary>
    [Fact]
    public void TheAuthorizationCheckIsSealed()
    {
        MethodInfo execute = typeof(AiToolBase).GetMethod(nameof(AiToolBase.ExecuteAsync))!;

        Assert.False(
            execute.IsVirtual && !execute.IsFinal,
            "AiToolBase.ExecuteAsync must not be overridable: the whole point of "
                + "the base class is that the check cannot be skipped.");
    }

    /// <summary>
    /// One canonical write, and it is <c>task.create</c>.
    /// </summary>
    /// <remarks>
    /// A task was chosen deliberately as the first and only one: reversible, low
    /// consequence, and enough to exercise the entire approval and execution
    /// protocol. Widening this is a decision with an ADR behind it, not a change
    /// somebody makes while adding a feature (§11, §12).
    /// </remarks>
    [Fact]
    public void ThereIsExactlyOneCanonicalWriteTool()
    {
        IReadOnlyList<IAiTool> writes =
            [.. Tools().Where(x => x.Effect == ToolEffect.CanonicalWrite)];

        IAiTool only = Assert.Single(writes);

        Assert.Equal("task.create", only.Name);
    }

    /// <summary>
    /// No tool causes an effect outside AgencyOS.
    /// </summary>
    /// <remarks>
    /// Sending is a canonical workflow a person carries out. There is no send tool
    /// in this build for any agent, the aggregate refuses one at construction, and
    /// a CHECK constraint refuses the row (§12, §15, §29).
    /// </remarks>
    [Fact]
    public void NoToolHasAnExternalEffect()
    {
        Assert.DoesNotContain(Tools(), x => x.Effect == ToolEffect.ExternalEffect);
    }

    /// <summary>
    /// No tool reaches persistence, the filesystem, the shell or configuration.
    /// </summary>
    /// <remarks>
    /// Checked on the constructor signature, which is where such a thing would
    /// arrive. A tool is a facade over an authorized application service; the
    /// moment one takes a <c>DbContext</c> it has become a way to ask a model
    /// what to read from the database (§9).
    /// </remarks>
    [Fact]
    public void NoToolTakesAForbiddenDependency()
    {
        string[] forbidden =
            ["DbContext", "IConfiguration", "HttpClient", "Process", "FileStream", "IWebHost"];

        foreach (Type type in ToolTypes)
        {
            foreach (ConstructorInfo constructor in type.GetConstructors())
            {
                foreach (ParameterInfo parameter in constructor.GetParameters())
                {
                    string name = parameter.ParameterType.Name;

                    Assert.DoesNotContain(
                        forbidden,
                        x => name.Contains(x, StringComparison.Ordinal));
                }
            }
        }
    }

    /// <summary>
    /// Every tool an agent is allowed to use exists.
    /// </summary>
    /// <remarks>
    /// The allow-list is written by hand, and a typo in it fails open in the
    /// unhelpful direction: the agent silently loses a tool and nobody notices
    /// until a brief is worse than it should be.
    /// </remarks>
    [Fact]
    public void EveryAllowedToolNameResolves()
    {
        HashSet<string> registered = [.. Tools().Select(x => x.Name)];

        foreach ((AgentKind kind, IReadOnlySet<string> allowed) in AgentToolAllowList.ByAgent)
        {
            Assert.All(allowed, name => Assert.True(
                registered.Contains(name),
                $"{kind} is allowed '{name}', which no tool provides."));
        }
    }

    /// <summary>Every agent this build offers has an allow-list and a definition.</summary>
    /// <remarks>
    /// A missing entry means an agent with no tools and no prompt, which fails at
    /// run time in front of somebody rather than here.
    /// </remarks>
    [Fact]
    public void EveryAgentHasAnAllowListAndADefinition()
    {
        foreach (AgentKind kind in Enum.GetValues<AgentKind>())
        {
            Assert.True(
                AgentToolAllowList.ByAgent.ContainsKey(kind), $"{kind} has no allow-list.");

            Assert.True(AgentCatalog.ByKind.ContainsKey(kind), $"{kind} has no definition.");
        }
    }

    /// <summary>
    /// The finance brief reads and does nothing else.
    /// </summary>
    /// <remarks>
    /// Called out on its own because it is the one somebody would widen without
    /// thinking. A finance brief that could create work items would be a finance
    /// workflow, and money stays strictly read-only in M12 (§28).
    /// </remarks>
    [Fact]
    public void TheFinanceBriefIsReadOnly()
    {
        IReadOnlyDictionary<string, IAiTool> byName =
            Tools().ToDictionary(x => x.Name, StringComparer.Ordinal);

        Assert.All(
            AgentToolAllowList.ByAgent[AgentKind.FinanceBrief],
            name => Assert.Equal(ToolEffect.ReadOnly, byName[name].Effect));
    }

    /// <summary>
    /// A prompt is versioned, and versions start at one.
    /// </summary>
    /// <remarks>
    /// The version is what makes a run reproducible: the wording lives in source
    /// control, and the run records which wording produced it (§19, §20).
    /// </remarks>
    [Fact]
    public void EveryAgentPromptIsVersioned()
    {
        Assert.All(AgentCatalog.ByKind.Values, definition =>
        {
            Assert.NotEmpty(definition.PromptTemplateId);
            Assert.NotEmpty(definition.SystemPrompt);
            Assert.True(definition.PromptTemplateVersion >= 1);
        });
    }

    /// <summary>
    /// Every tool declares a permission that exists.
    /// </summary>
    /// <remarks>
    /// A tool naming a permission nobody holds is refused for everybody, which
    /// looks like the tool being broken; a tool naming a misspelled one is refused
    /// the same way. Both are silent, so both are checked.
    /// </remarks>
    [Fact]
    public void EveryToolNamesARealPermission()
    {
        HashSet<string> permissions =
        [
            .. typeof(Domain.Authorization.Permission)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(x => x.IsLiteral && x.FieldType == typeof(string))
                .Select(x => (string)x.GetRawConstantValue()!),
        ];

        Assert.All(Tools(), tool => Assert.Contains(tool.RequiredPermission, permissions));
    }

    private static IReadOnlyList<IAiTool> Tools() =>
        [.. ToolTypes.Select(x => (IAiTool)RuntimeHelpers.GetUninitializedObject(x))];
}
