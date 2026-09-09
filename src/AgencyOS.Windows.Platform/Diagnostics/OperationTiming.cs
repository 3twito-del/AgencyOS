using System.Diagnostics;
using System.Globalization;

namespace AgencyOS.Windows.Platform.Diagnostics;

/// <summary>
/// Where the time in an operation went.
/// </summary>
/// <remarks>
/// <para>
/// Four buckets, kept apart because they have different owners and different
/// fixes. Client time is AgencyOS's own code on this machine; network is the
/// wire; server is the API and PostgreSQL; model is a provider or a local
/// runtime. A single number hides which of the four moved, and "the app is slow"
/// has been used to justify optimizing the wrong one often enough to be worth
/// the extra fields (§X, ADR-0034).
/// </para>
/// <para>
/// Model time is separated from server time even when inference happens
/// server-side. A run that waited eleven seconds on a provider is not a slow
/// server, and a budget that conflated them would fail on somebody else's
/// latency.
/// </para>
/// </remarks>
public enum TimingBucket
{
    /// <summary>AgencyOS's own work on this machine: mapping, layout, rendering.</summary>
    Client = 1,

    /// <summary>Time on the wire, including connection setup.</summary>
    Network = 2,

    /// <summary>The API and the database.</summary>
    Server = 3,

    /// <summary>A model provider or a local inference runtime.</summary>
    Model = 4,
}

/// <summary>One measured span.</summary>
/// <param name="Bucket">Whose time it was.</param>
/// <param name="Elapsed">How long it took.</param>
public sealed record TimingSpan(TimingBucket Bucket, TimeSpan Elapsed);

/// <summary>
/// What one operation cost, broken down.
/// </summary>
/// <remarks>
/// Carries no operation name, no record identifier and no content — a timing
/// report is a diagnostic that gets pasted into support channels, and the
/// allow-list reasoning in <see cref="DiagnosticSummary"/> applies here too. The
/// caller names the operation; this says how long its parts took.
/// </remarks>
public sealed record TimingBreakdown(IReadOnlyList<TimingSpan> Spans)
{
    /// <summary>Everything, added up.</summary>
    public TimeSpan Total => Spans.Aggregate(TimeSpan.Zero, (sum, x) => sum + x.Elapsed);

    /// <summary>How long one bucket took across the whole operation.</summary>
    public TimeSpan In(TimingBucket bucket) =>
        Spans
            .Where(x => x.Bucket == bucket)
            .Aggregate(TimeSpan.Zero, (sum, x) => sum + x.Elapsed);

    /// <summary>
    /// The bucket that dominated, or null when nothing was measured.
    /// </summary>
    /// <remarks>
    /// The number worth reading first. It answers "whose problem is this" before
    /// anybody argues about the total.
    /// </remarks>
    public TimingBucket? Dominant =>
        Spans.Count == 0
            ? null
            : Enum.GetValues<TimingBucket>()
                .OrderByDescending(In)
                .First();

    /// <summary>Renders the breakdown for a diagnostic paste.</summary>
    public string Render()
    {
        if (Spans.Count == 0)
        {
            return "no measurements";
        }

        IEnumerable<string> parts = Enum.GetValues<TimingBucket>()
            .Select(bucket => (Bucket: bucket, Elapsed: In(bucket)))
            .Where(x => x.Elapsed > TimeSpan.Zero)
            .Select(x => string.Create(
                CultureInfo.InvariantCulture,
                $"{x.Bucket} {x.Elapsed.TotalMilliseconds:F0}ms"));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"total {Total.TotalMilliseconds:F0}ms ({string.Join(", ", parts)})");
    }
}

/// <summary>
/// Measures where an operation's time goes.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately small. It records spans and adds them up; it sets no budget,
/// fails no build and decides nothing. Budgets are derived from measurements on
/// known hardware, and a harness that also enforced them would invite somebody to
/// put a wall-clock threshold in CI — where a shared runner's timing is not
/// product truth (§X, §61).
/// </para>
/// <para>
/// The clock is injectable so the harness itself can be tested deterministically.
/// CI verifies that it measures and attributes correctly, which is the part that
/// can be wrong; how long anything actually takes is LAB evidence recorded
/// against named hardware.
/// </para>
/// </remarks>
public sealed class OperationTimer
{
    private readonly Func<long> _ticks;
    private readonly List<TimingSpan> _spans = [];

    /// <summary>A timer over the system clock.</summary>
    public OperationTimer()
        : this(() => Stopwatch.GetTimestamp())
    {
    }

    /// <param name="ticks">
    /// A monotonic tick source in <see cref="Stopwatch"/> units. Injected so the
    /// harness can be tested without waiting.
    /// </param>
    public OperationTimer(Func<long> ticks)
    {
        ArgumentNullException.ThrowIfNull(ticks);

        _ticks = ticks;
    }

    /// <summary>What has been measured so far.</summary>
    public TimingBreakdown Breakdown => new([.. _spans]);

    /// <summary>
    /// Measures a span and attributes it.
    /// </summary>
    /// <remarks>
    /// Returns a disposable rather than wrapping a delegate, so a caller can
    /// measure part of a method without restructuring it — which is what makes it
    /// possible to separate client time from network time inside one call.
    /// </remarks>
    public IDisposable Measure(TimingBucket bucket) => new Span(this, bucket, _ticks());

    private void Record(TimingBucket bucket, long start)
    {
        long elapsed = _ticks() - start;

        _spans.Add(new TimingSpan(
            bucket,
            TimeSpan.FromSeconds((double)elapsed / Stopwatch.Frequency)));
    }

    private sealed class Span : IDisposable
    {
        private readonly OperationTimer _timer;
        private readonly TimingBucket _bucket;
        private readonly long _start;
        private bool _done;

        public Span(OperationTimer timer, TimingBucket bucket, long start)
        {
            _timer = timer;
            _bucket = bucket;
            _start = start;
        }

        public void Dispose()
        {
            // Idempotent: a using block inside a retry loop should not record the
            // same span twice.
            if (_done)
            {
                return;
            }

            _done = true;
            _timer.Record(_bucket, _start);
        }
    }
}
