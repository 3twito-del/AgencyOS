using System.Diagnostics;
using AgencyOS.Windows.Platform.Diagnostics;
using Xunit;

namespace AgencyOS.Tests.Windows.Diagnostics;

/// <summary>
/// That the harness measures and attributes correctly.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Nothing here asserts that anything is fast.</strong> A shared CI
/// runner's wall clock is not product truth, and a threshold here would fail on
/// somebody else's noisy afternoon. What CI can verify is the part that can be
/// wrong in a way nobody would notice: whether the harness attributes time to the
/// right bucket and adds it up correctly (§X, §61).
/// </para>
/// <para>
/// The clock is injected, so these run instantly and deterministically.
/// </para>
/// </remarks>
public sealed class OperationTimingTests
{
    /// <summary>Time lands in the bucket it was measured under.</summary>
    [Fact]
    public void TimeIsAttributedToItsBucket()
    {
        FakeClock clock = new();
        OperationTimer timer = new(clock.Read);

        using (timer.Measure(TimingBucket.Client))
        {
            clock.Advance(TimeSpan.FromMilliseconds(40));
        }

        using (timer.Measure(TimingBucket.Server))
        {
            clock.Advance(TimeSpan.FromMilliseconds(120));
        }

        TimingBreakdown breakdown = timer.Breakdown;

        Assert.Equal(40, breakdown.In(TimingBucket.Client).TotalMilliseconds, 0);
        Assert.Equal(120, breakdown.In(TimingBucket.Server).TotalMilliseconds, 0);
        Assert.Equal(0, breakdown.In(TimingBucket.Network).TotalMilliseconds, 0);
        Assert.Equal(160, breakdown.Total.TotalMilliseconds, 0);
    }

    /// <summary>Repeated spans in one bucket add up.</summary>
    [Fact]
    public void RepeatedSpansAccumulate()
    {
        FakeClock clock = new();
        OperationTimer timer = new(clock.Read);

        for (int i = 0; i < 3; i++)
        {
            using (timer.Measure(TimingBucket.Network))
            {
                clock.Advance(TimeSpan.FromMilliseconds(10));
            }
        }

        Assert.Equal(30, timer.Breakdown.In(TimingBucket.Network).TotalMilliseconds, 0);
    }

    /// <summary>
    /// The dominant bucket is the one worth reading first.
    /// </summary>
    /// <remarks>
    /// It answers "whose problem is this" before anybody argues about the total,
    /// which is the whole reason the buckets are kept apart.
    /// </remarks>
    [Fact]
    public void TheDominantBucketIsIdentified()
    {
        FakeClock clock = new();
        OperationTimer timer = new(clock.Read);

        using (timer.Measure(TimingBucket.Client))
        {
            clock.Advance(TimeSpan.FromMilliseconds(20));
        }

        using (timer.Measure(TimingBucket.Model))
        {
            clock.Advance(TimeSpan.FromSeconds(11));
        }

        Assert.Equal(TimingBucket.Model, timer.Breakdown.Dominant);
    }

    /// <summary>
    /// A slow model is not a slow server.
    /// </summary>
    /// <remarks>
    /// The distinction the four buckets exist for. A run that waited on a provider
    /// must not read as a server problem, because the fix is somewhere else
    /// entirely — and a budget that conflated them would fail on latency AgencyOS
    /// does not own.
    /// </remarks>
    [Fact]
    public void ModelTimeIsNotServerTime()
    {
        FakeClock clock = new();
        OperationTimer timer = new(clock.Read);

        using (timer.Measure(TimingBucket.Server))
        {
            clock.Advance(TimeSpan.FromMilliseconds(30));
        }

        using (timer.Measure(TimingBucket.Model))
        {
            clock.Advance(TimeSpan.FromSeconds(9));
        }

        TimingBreakdown breakdown = timer.Breakdown;

        Assert.Equal(30, breakdown.In(TimingBucket.Server).TotalMilliseconds, 0);
        Assert.True(breakdown.In(TimingBucket.Model) > TimeSpan.FromSeconds(8));
    }

    /// <summary>Disposing twice records one span.</summary>
    /// <remarks>
    /// A using block inside a retry loop should not double-count, and a
    /// double-counted span is the kind of measurement error that quietly justifies
    /// optimizing the wrong thing.
    /// </remarks>
    [Fact]
    public void DisposingTwiceRecordsOneSpan()
    {
        FakeClock clock = new();
        OperationTimer timer = new(clock.Read);

        IDisposable span = timer.Measure(TimingBucket.Client);
        clock.Advance(TimeSpan.FromMilliseconds(15));
        span.Dispose();
        span.Dispose();

        Assert.Single(timer.Breakdown.Spans);
        Assert.Equal(15, timer.Breakdown.Total.TotalMilliseconds, 0);
    }

    /// <summary>An unmeasured operation says so rather than reporting zero.</summary>
    [Fact]
    public void NothingMeasuredSaysSo()
    {
        TimingBreakdown empty = new OperationTimer().Breakdown;

        Assert.Null(empty.Dominant);
        Assert.Equal("no measurements", empty.Render());
    }

    /// <summary>The rendering names every bucket that took time, and no others.</summary>
    [Fact]
    public void RenderingNamesTheBucketsThatTookTime()
    {
        FakeClock clock = new();
        OperationTimer timer = new(clock.Read);

        using (timer.Measure(TimingBucket.Client))
        {
            clock.Advance(TimeSpan.FromMilliseconds(5));
        }

        using (timer.Measure(TimingBucket.Network))
        {
            clock.Advance(TimeSpan.FromMilliseconds(25));
        }

        string rendered = timer.Breakdown.Render();

        Assert.Contains("Client", rendered, StringComparison.Ordinal);
        Assert.Contains("Network", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Server", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Model", rendered, StringComparison.Ordinal);
        Assert.Contains("total 30ms", rendered, StringComparison.Ordinal);
    }

    /// <summary>
    /// A breakdown carries no content.
    /// </summary>
    /// <remarks>
    /// Timing reports get pasted into support channels, so the same allow-list
    /// reasoning as the diagnostic summary applies: the caller names the
    /// operation, and this says only how long its parts took.
    /// </remarks>
    [Fact]
    public void ABreakdownCarriesNoContent()
    {
        Assert.DoesNotContain(
            typeof(TimingBreakdown).GetProperties(),
            x => x.PropertyType == typeof(string));

        Assert.DoesNotContain(
            typeof(TimingSpan).GetProperties(),
            x => x.PropertyType == typeof(string));
    }

    /// <summary>The real clock is monotonic and produces a positive span.</summary>
    /// <remarks>
    /// The one test that touches the system clock. It asserts the harness is wired
    /// to a real tick source, not how fast anything is.
    /// </remarks>
    [Fact]
    public void TheRealClockMeasuresSomething()
    {
        OperationTimer timer = new();

        using (timer.Measure(TimingBucket.Client))
        {
            Stopwatch spin = Stopwatch.StartNew();

            while (spin.ElapsedTicks == 0)
            {
                // Busy until the stopwatch advances at all.
            }
        }

        Assert.True(timer.Breakdown.Total > TimeSpan.Zero);
    }

    /// <summary>A tick source under the harness's control.</summary>
    private sealed class FakeClock
    {
        private long _ticks;

        public long Read() => _ticks;

        public void Advance(TimeSpan by) =>
            _ticks += (long)(by.TotalSeconds * Stopwatch.Frequency);
    }
}
