using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace AgencyOS.Tests.Integration.Infrastructure;

/// <summary>
/// Keeps the host's own log so a failing test can say what the server saw.
/// </summary>
/// <remarks>
/// <para>
/// A 500 from the API reaches a test as an opaque problem document: a status, a
/// title, and a trace identifier that leads nowhere a test can follow. The host
/// logs the real exception, but the test host's console does not survive into a CI
/// run's output, so the one piece of information needed to diagnose the failure is
/// the one piece that is thrown away.
/// </para>
/// <para>
/// This keeps the last few errors in memory so an assertion can quote them. It
/// exists because a CI cycle spent guessing at an unexplained 500 is a CI cycle
/// that teaches nothing.
/// </para>
/// </remarks>
public sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _errors = new();

    public ILogger CreateLogger(string categoryName) => new Sink(this, categoryName);

    /// <summary>The most recent server-side errors, newest last.</summary>
    public IReadOnlyList<string> Errors => [.. _errors];

    /// <summary>Everything recorded so far, for an assertion message.</summary>
    public string Describe() => _errors.IsEmpty
        ? "(the host logged no error)"
        : string.Join(Environment.NewLine, _errors);

    public void Clear() => _errors.Clear();

    public void Dispose() => _errors.Clear();

    private void Record(string message)
    {
        _errors.Enqueue(message);

        while (_errors.Count > 20 && _errors.TryDequeue(out _))
        {
            // Bounded: a long suite would otherwise accumulate every error it
            // deliberately provoked.
        }
    }

    private sealed class Sink : ILogger
    {
        private readonly CapturedLogs _owner;
        private readonly string _category;

        public Sink(CapturedLogs owner, string category)
        {
            _owner = owner;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(formatter);

            _owner.Record(
                $"[{_category}] {formatter(state, exception)}"
                    + (exception is null ? string.Empty : $" :: {exception}"));
        }
    }
}
