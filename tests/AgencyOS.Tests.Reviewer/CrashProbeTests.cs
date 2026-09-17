using AgencyOS.Reviewer.Runtime;
using Xunit;

namespace AgencyOS.Tests.Reviewer;

/// <summary>
/// That the crash probe says which step took the client down, and carries no
/// secrets into the dump it provokes.
/// </summary>
/// <remarks>
/// <para>
/// Repair Wave 003A.1. The dialog pass recorded sixteen "application closed"
/// observations against three dialogs; the process had in fact died on a detail
/// tab before any dialog was constructed (<c>AOS-R002-022</c>). The verdict is
/// the probe's answer to that, so it gets a positive and negative control like the
/// opener probe's.
/// </para>
/// <para>
/// The environment half exists because the one minidump Windows kept during Repair
/// Wave 003B held every credential in the shell that launched the harness.
/// </para>
/// </remarks>
public sealed class CrashProbeTests
{
    /// <summary>The process went away after the opener ran.</summary>
    [Fact]
    public void AnExitAfterTheOpenerIsACrash() =>
        Assert.Equal("CRASH_REPRODUCED", CrashProbe.Verdict(exited: true, invoked: true, opened: false));

    /// <summary>A dialog that appeared and was followed by an exit is still a crash.</summary>
    [Fact]
    public void AnExitOutranksADialogThatAppeared() =>
        Assert.Equal("CRASH_REPRODUCED", CrashProbe.Verdict(exited: true, invoked: true, opened: true));

    /// <summary>The process went away before the opener was reached.</summary>
    /// <remarks>
    /// The case the dialog pass reported as a dialog crash.
    /// </remarks>
    [Fact]
    public void AnExitBeforeTheOpenerIsNotBlamedOnTheDialog() =>
        Assert.Equal("EXITED_BEFORE_OPENER", CrashProbe.Verdict(exited: true, invoked: false, opened: false));

    /// <summary>The dialog appeared and the process outlived the watch.</summary>
    [Fact]
    public void ASurvivingDialogIsOpened() =>
        Assert.Equal("DIALOG_OPENED", CrashProbe.Verdict(exited: false, invoked: true, opened: true));

    /// <summary>The opener ran, the process lived, and nothing appeared.</summary>
    [Fact]
    public void ALivingProcessWithNoDialogDidNotAppear() =>
        Assert.Equal("DID_NOT_APPEAR", CrashProbe.Verdict(exited: false, invoked: true, opened: false));

    /// <summary>The path never reached an opener and the process lived.</summary>
    [Fact]
    public void NoOpenerAndNoExitIsNotInvoked() =>
        Assert.Equal("NOT_INVOKED", CrashProbe.Verdict(exited: false, invoked: false, opened: false));

    /// <summary>The minimal environment keeps nothing that looks like a credential.</summary>
    [Fact]
    public void TheMinimalEnvironmentCarriesNoCredentials()
    {
        string[] suspicious = ["TOKEN", "KEY", "SECRET", "PASSWORD", "PASS", "URL", "CONNECTION", "DATABASE", "AUTH"];

        Assert.All(ReviewApp.WindowsVariables, name =>
            Assert.DoesNotContain(
                suspicious,
                word => name.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>It still keeps what a desktop process needs to start.</summary>
    [Theory]
    [InlineData("SystemRoot")]
    [InlineData("LOCALAPPDATA")]
    [InlineData("TEMP")]
    [InlineData("Path")]
    public void TheMinimalEnvironmentKeepsWhatWindowsNeeds(string name) =>
        Assert.Contains(name, ReviewApp.WindowsVariables);
}
