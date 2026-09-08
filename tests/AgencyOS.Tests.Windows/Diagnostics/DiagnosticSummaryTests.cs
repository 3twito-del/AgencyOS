using AgencyOS.Windows.Platform.Capabilities;
using AgencyOS.Windows.Platform.Diagnostics;
using Xunit;

namespace AgencyOS.Tests.Windows.Diagnostics;

/// <summary>
/// What AgencyOS will say about itself, and what it will not.
/// </summary>
/// <remarks>
/// A diagnostic summary is pasted into support channels AgencyOS does not
/// control. These tests are about the allow-list holding: it is wrong only about
/// things somebody deliberately added, where a redactor is wrong the first time
/// it meets a shape of secret it does not recognize (ADR-0034).
/// </remarks>
public sealed class DiagnosticSummaryTests
{
    /// <summary>An unlisted field is refused rather than silently dropped.</summary>
    /// <remarks>
    /// Dropping it would make the allow-list advisory, and the failure would be a
    /// missing line nobody noticed.
    /// </remarks>
    [Theory]
    [InlineData("Provider API key")]
    [InlineData("Connection string")]
    [InlineData("Current user")]
    [InlineData("Open deal")]
    public void AnUnlistedFieldIsRefused(string name)
    {
        DiagnosticSummary summary = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => summary.Add(name, "anything"));
    }

    /// <summary>
    /// The allow-list is exactly what was reviewed.
    /// </summary>
    /// <remarks>
    /// A change-detector on purpose. The whole value of an allow-list is that
    /// adding to it is deliberate and visible in a diff; a test that merely
    /// pattern-matched the names would pass for a field somebody added carelessly
    /// with an innocuous name.
    /// </remarks>
    [Fact]
    public void ThePermittedFieldsAreExactlyTheReviewedSet()
    {
        string[] expected =
        [
            "API contract",
            "AgencyOS version",
            "Architecture",
            "Cache schema",
            "Connectivity",
            "Execution providers",
            "Last sync",
            "Local AI readiness",
            "Release channel",
            "Server compatibility",
            "Windows App SDK",
            "Windows version",
        ];

        Assert.Equal(expected, DiagnosticSummary.Permitted.OrderBy(x => x, StringComparer.Ordinal));
    }

    /// <summary>
    /// No permitted field names business content.
    /// </summary>
    /// <remarks>
    /// "API contract" is the wire contract version, not an agency contract, and is
    /// the one place the word appears legitimately. Everything else describes the
    /// installation.
    /// </remarks>
    [Fact]
    public void NothingPermittedNamesBusinessContent()
    {
        string[] forbidden =
        [
            "person", "client", "company", "deal", "document",
            "message", "signal", "amount", "invoice", "payment", "prompt",
        ];

        Assert.All(
            DiagnosticSummary.Permitted,
            field => Assert.DoesNotContain(
                forbidden,
                word => field.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>An obviously unsafe value is withheld even from a permitted field.</summary>
    /// <remarks>
    /// A crude second net under the allow-list. It cannot catch a leak using none
    /// of these words and is not relied on to; it exists so an obviously wrong
    /// value fails here rather than reaching a support channel.
    /// </remarks>
    [Theory]
    [InlineData("Bearer eyJhbGciOi")]
    [InlineData("Host=db;Password=hunter2")]
    [InlineData("api_key=sk-live-000")]
    public void AnObviouslyUnsafeValueIsWithheld(string value)
    {
        DiagnosticSummary summary = new();
        summary.Add("Server compatibility", value);

        Assert.Equal("[withheld]", summary.Fields[0].Value);
        Assert.DoesNotContain("hunter2", summary.Render(), StringComparison.Ordinal);
    }

    /// <summary>One field cannot forge others.</summary>
    [Fact]
    public void NewlinesAreFlattened()
    {
        DiagnosticSummary summary = new();
        summary.Add("Windows version", "11\nProvider API key  sk-live-000");

        Assert.DoesNotContain('\n', summary.Fields[0].Value);
    }

    /// <summary>An absent value reads as unknown rather than as blank.</summary>
    [Fact]
    public void AnAbsentValueReadsAsUnknown()
    {
        DiagnosticSummary summary = new();
        summary.Add("Last sync", null);
        summary.Add("Connectivity", "   ");

        Assert.All(summary.Fields, x => Assert.Equal("unknown", x.Value));
    }

    /// <summary>A realistic summary carries versions and capability, and nothing else.</summary>
    [Fact]
    public void ARealisticSummaryIsSafeToPaste()
    {
        WindowsCapabilityReport capability = new(
            LocalModelReadiness.NotSupported,
            [new ExecutionProviderInfo("VendorCpu", ExecutionDevice.Cpu, IsReady: true)],
            "This device has no supported neural processor.");

        string rendered = new DiagnosticSummary()
            .Add("AgencyOS version", "0.3.0")
            .Add("Release channel", "alpha")
            .Add("API contract", "13")
            .Add("Windows version", "11 build 26200")
            .Add("Windows App SDK", "2.4.0")
            .Add("Architecture", "x64")
            .Add("Local AI readiness", capability.LocalModel.ToString())
            .Add("Execution providers", string.Join(", ", capability.ReadyDevices))
            .Add("Cache schema", "2")
            .Add("Connectivity", "online")
            .Render();

        Assert.Contains("0.3.0", rendered, StringComparison.Ordinal);
        Assert.Contains("NotSupported", rendered, StringComparison.Ordinal);
        Assert.Contains("Cpu", rendered, StringComparison.Ordinal);

        Assert.All(
            DiagnosticSummary.Forbidden,
            word => Assert.DoesNotContain(word, rendered, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A long value is bounded rather than pasting a wall of text.</summary>
    [Fact]
    public void ALongValueIsBounded()
    {
        DiagnosticSummary summary = new();
        summary.Add("Execution providers", new string('x', 900));

        Assert.True(summary.Fields[0].Value.Length <= 201);
    }
}

/// <summary>
/// What the capability report means when nothing is available.
/// </summary>
/// <remarks>
/// The interesting case on every machine AgencyOS is currently developed on. The
/// workstation has no NPU, so the honest report is "not supported" — and the
/// client has to behave correctly on exactly that answer (§F, ADR-0035).
/// </remarks>
public sealed class WindowsCapabilityReportTests
{
    [Fact]
    public void UnknownIsDistinctFromNotSupported()
    {
        WindowsCapabilityReport unknown = WindowsCapabilityReport.Unknown("The probe threw.");

        Assert.Equal(LocalModelReadiness.Unknown, unknown.LocalModel);
        Assert.NotEqual(LocalModelReadiness.NotSupported, unknown.LocalModel);
        Assert.False(unknown.CanRunLocally);
        Assert.Empty(unknown.Providers);
    }

    /// <summary>Only a ready model permits a local attempt.</summary>
    [Theory]
    [InlineData(LocalModelReadiness.Unknown, false)]
    [InlineData(LocalModelReadiness.NotSupported, false)]
    [InlineData(LocalModelReadiness.NotReady, false)]
    [InlineData(LocalModelReadiness.Ready, true)]
    public void OnlyReadyPermitsALocalAttempt(LocalModelReadiness readiness, bool expected)
    {
        WindowsCapabilityReport report = new(readiness, [], "detail");

        Assert.Equal(expected, report.CanRunLocally);
    }

    /// <summary>Devices that are not ready are not reported as available.</summary>
    [Fact]
    public void OnlyReadyProvidersAreReported()
    {
        WindowsCapabilityReport report = new(
            LocalModelReadiness.Ready,
            [
                new ExecutionProviderInfo("Cpu", ExecutionDevice.Cpu, IsReady: true),
                new ExecutionProviderInfo("Npu", ExecutionDevice.Npu, IsReady: false),
            ],
            "detail");

        Assert.Equal([ExecutionDevice.Cpu], report.ReadyDevices);
        Assert.DoesNotContain(ExecutionDevice.Npu, report.ReadyDevices);
    }

    /// <summary>The strongest ready device is reported first.</summary>
    [Fact]
    public void ReadyDevicesArePreferenceOrdered()
    {
        WindowsCapabilityReport report = new(
            LocalModelReadiness.Ready,
            [
                new ExecutionProviderInfo("Cpu", ExecutionDevice.Cpu, IsReady: true),
                new ExecutionProviderInfo("Npu", ExecutionDevice.Npu, IsReady: true),
                new ExecutionProviderInfo("Gpu", ExecutionDevice.Gpu, IsReady: true),
            ],
            "detail");

        Assert.Equal(
            [ExecutionDevice.Npu, ExecutionDevice.Gpu, ExecutionDevice.Cpu],
            report.ReadyDevices);
    }
}
