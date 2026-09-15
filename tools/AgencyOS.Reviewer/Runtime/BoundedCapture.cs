using System.Globalization;
using System.Text;
using System.IO;

namespace AgencyOS.Reviewer.Runtime;

/// <summary>
/// Writes a process's output to a file that cannot grow without limit.
/// </summary>
/// <remarks>
/// <para>
/// Audit 002 Phase C captured the API's stdout to a scratch file with no ceiling
/// and filled the disk. A disk-full event during an audit is not a nuisance: it
/// corrupts screenshots, truncates trees, and can leave fixture state half
/// written, so the evidence it destroys is exactly the evidence the audit is
/// there to produce.
/// </para>
/// <para>
/// The policy keeps three things and drops only the fourth. It keeps the
/// beginning, because startup is where configuration problems appear; it keeps
/// every line that looks like a failure, because that is the reason anyone reads
/// a log afterwards; and it keeps a rolling tail, because the end is where the
/// run stopped. What it drops is the routine middle, and it says so where the
/// drop happened rather than leaving a reader to wonder.
/// </para>
/// </remarks>
internal sealed class BoundedCapture : IDisposable
{
    private readonly StreamWriter _head;
    private readonly Queue<string> _tail = new();
    private readonly List<string> _failures = [];
    private readonly long _budget;
    private readonly int _tailLines;

    private long _written;
    private long _total;
    private long _dropped;
    private bool _announced;

    /// <summary>Starts a bounded capture.</summary>
    /// <param name="path">Where the kept output goes.</param>
    /// <param name="budgetBytes">How much routine output to keep from the start.</param>
    /// <param name="tailLines">How many of the most recent lines to keep.</param>
    internal BoundedCapture(string path, long budgetBytes = 256L * 1024 * 1024, int tailLines = 4000)
    {
        _head = new StreamWriter(path, append: false, Encoding.UTF8) { AutoFlush = false };
        _budget = budgetBytes;
        _tailLines = tailLines;
    }

    /// <summary>How much the process produced, whether or not it was kept.</summary>
    internal long TotalBytes => _total;

    /// <summary>How much was not written.</summary>
    internal long DroppedBytes => _dropped;

    /// <summary>Lines that looked like failures, all of which are kept.</summary>
    internal IReadOnlyList<string> Failures => _failures;

    /// <summary>Whether a line is one the audit would want to read afterwards.</summary>
    /// <param name="line">One line of output.</param>
    /// <returns><see langword="true"/> when it carries a failure.</returns>
    /// <remarks>
    /// Deliberately generous. Keeping a routine line by mistake costs a few
    /// bytes; dropping the one line that explains a failed run costs the run.
    /// </remarks>
    internal static bool LooksLikeFailure(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return line.Contains("\"LogLevel\":\"Error\"", StringComparison.Ordinal)
            || line.Contains("\"LogLevel\":\"Critical\"", StringComparison.Ordinal)
            || line.Contains("\"LogLevel\":\"Warning\"", StringComparison.Ordinal)
            || line.Contains("Exception", StringComparison.Ordinal)
            || line.Contains("Unhandled", StringComparison.Ordinal)
            || line.Contains("fail:", StringComparison.Ordinal);
    }

    /// <summary>Takes one line of the process's output.</summary>
    /// <param name="line">The line.</param>
    internal void Write(string? line)
    {
        if (line is null)
        {
            return;
        }

        _total += line.Length + 1;

        if (LooksLikeFailure(line))
        {
            // Kept whatever the budget says. A log without its failures is not a
            // smaller log, it is a different and useless one.
            _failures.Add(line);
            _head.WriteLine(line);
            _written += line.Length + 1;

            return;
        }

        if (_written < _budget)
        {
            _head.WriteLine(line);
            _written += line.Length + 1;

            return;
        }

        if (!_announced)
        {
            _head.WriteLine(
                "--- capture reached its budget of "
                + (_budget / (1024 * 1024)).ToString(CultureInfo.InvariantCulture)
                + " MB. Routine output past this point is dropped; failures and the "
                + "last " + _tailLines.ToString(CultureInfo.InvariantCulture)
                + " lines are still kept. ---");

            _announced = true;
        }

        _dropped += line.Length + 1;
        _tail.Enqueue(line);

        while (_tail.Count > _tailLines)
        {
            _tail.Dequeue();
        }
    }

    /// <summary>Finishes the file, appending the tail and what was dropped.</summary>
    public void Dispose()
    {
        if (_announced)
        {
            _head.WriteLine();
            _head.WriteLine(
                "--- "
                + (_dropped / (1024 * 1024)).ToString(CultureInfo.InvariantCulture)
                + " MB of routine output was dropped. The last "
                + _tail.Count.ToString(CultureInfo.InvariantCulture)
                + " lines follow. ---");

            foreach (string line in _tail)
            {
                _head.WriteLine(line);
            }
        }

        _head.WriteLine();
        _head.WriteLine(
            "--- the process produced "
            + (_total / (1024 * 1024)).ToString(CultureInfo.InvariantCulture)
            + " MB in total; "
            + (_written / (1024 * 1024)).ToString(CultureInfo.InvariantCulture)
            + " MB was kept, and "
            + _failures.Count.ToString(CultureInfo.InvariantCulture)
            + " line(s) looked like failures. ---");

        _head.Flush();
        _head.Dispose();
    }
}
