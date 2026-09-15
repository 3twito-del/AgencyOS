using System.IO;
using System.Globalization;
using AgencyOS.Reviewer.Runtime;
using Xunit;

namespace AgencyOS.Tests.Reviewer;

/// <summary>
/// Controls for the capture policy that exists because the audit filled a disk.
/// </summary>
/// <remarks>
/// The property that matters is not "the file is small". It is that the file is
/// small <em>and still contains the reason a run failed</em>. A bound that drops
/// failures would make every future audit cheaper and useless.
/// </remarks>
public sealed class BoundedCaptureTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), "agencyos-capture-" + Guid.NewGuid().ToString("N") + ".log");

    public void Dispose() => File.Delete(_path);

    private static string Routine(int n) =>
        "{\"LogLevel\":\"Debug\",\"Message\":\"Executed DbCommand "
        + n.ToString(CultureInfo.InvariantCulture) + "\"}";

    [Fact]
    public void ARunThatFitsIsKeptWhole()
    {
        using (BoundedCapture capture = new(_path, budgetBytes: 64 * 1024))
        {
            for (int i = 0; i < 50; i++)
            {
                capture.Write(Routine(i));
            }
        }

        string kept = File.ReadAllText(_path);

        Assert.Contains("Executed DbCommand 0", kept, StringComparison.Ordinal);
        Assert.Contains("Executed DbCommand 49", kept, StringComparison.Ordinal);
        Assert.DoesNotContain("reached its budget", kept, StringComparison.Ordinal);
    }

    [Fact]
    public void ARunThatOverflowsStopsGrowing()
    {
        using (BoundedCapture capture = new(_path, budgetBytes: 4 * 1024, tailLines: 10))
        {
            for (int i = 0; i < 20000; i++)
            {
                capture.Write(Routine(i));
            }
        }

        // Twenty thousand lines of roughly sixty bytes is well over a megabyte.
        Assert.True(new FileInfo(_path).Length < 64 * 1024,
            "the capture kept " + new FileInfo(_path).Length.ToString(CultureInfo.InvariantCulture)
                + " bytes, which is not bounded");
    }

    [Fact]
    public void TheBeginningAndTheEndSurvive()
    {
        using (BoundedCapture capture = new(_path, budgetBytes: 4 * 1024, tailLines: 10))
        {
            for (int i = 0; i < 20000; i++)
            {
                capture.Write(Routine(i));
            }
        }

        string kept = File.ReadAllText(_path);

        Assert.Contains("Executed DbCommand 0", kept, StringComparison.Ordinal);
        Assert.Contains("Executed DbCommand 19999", kept, StringComparison.Ordinal);
    }

    [Fact]
    public void ADropIsAnnouncedWhereItHappened()
    {
        using (BoundedCapture capture = new(_path, budgetBytes: 4 * 1024, tailLines: 10))
        {
            for (int i = 0; i < 20000; i++)
            {
                capture.Write(Routine(i));
            }
        }

        string kept = File.ReadAllText(_path);

        Assert.Contains("reached its budget", kept, StringComparison.Ordinal);
        Assert.Contains("was dropped", kept, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailureIsKeptNoMatterHowLateItArrives()
    {
        // The whole point. This line arrives long after the budget is spent and
        // is the only line anybody would want.
        const string Failure =
            "{\"LogLevel\":\"Error\",\"Message\":\"Failed to connect to 127.0.0.1:5432\"}";

        using (BoundedCapture capture = new(_path, budgetBytes: 1024, tailLines: 5))
        {
            for (int i = 0; i < 10000; i++)
            {
                capture.Write(Routine(i));
            }

            capture.Write(Failure);

            for (int i = 0; i < 10000; i++)
            {
                capture.Write(Routine(i));
            }
        }

        Assert.Contains("Failed to connect to 127.0.0.1:5432",
            File.ReadAllText(_path), StringComparison.Ordinal);
    }

    [Fact]
    public void WhatWasDroppedIsCounted()
    {
        using BoundedCapture capture = new(_path, budgetBytes: 1024, tailLines: 5);

        for (int i = 0; i < 10000; i++)
        {
            capture.Write(Routine(i));
        }

        Assert.True(capture.DroppedBytes > 0);
        Assert.True(capture.TotalBytes > capture.DroppedBytes);
    }

    [Theory]
    [InlineData("{\"LogLevel\":\"Error\",\"Message\":\"x\"}")]
    [InlineData("{\"LogLevel\":\"Warning\",\"Message\":\"x\"}")]
    [InlineData("{\"LogLevel\":\"Critical\",\"Message\":\"x\"}")]
    [InlineData("System.InvalidOperationException: nope")]
    [InlineData("Unhandled exception.")]
    [InlineData("fail: Microsoft.EntityFrameworkCore.Query[0]")]
    public void FailuresAreRecognised(string line) =>
        Assert.True(BoundedCapture.LooksLikeFailure(line));

    [Theory]
    [InlineData("{\"LogLevel\":\"Debug\",\"Message\":\"Executed DbCommand\"}")]
    [InlineData("{\"LogLevel\":\"Information\",\"Message\":\"Request finished\"}")]
    public void RoutineOutputIsNot(string line) =>
        Assert.False(BoundedCapture.LooksLikeFailure(line));
}
