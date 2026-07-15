using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace RimTransAI.Tests.Helpers;

public sealed record RecordedLog(
    LogLevel Level,
    string Message,
    Exception? Exception,
    IReadOnlyDictionary<string, object?> Properties);

public sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<RecordedLog> _records = new();

    public IReadOnlyCollection<RecordedLog> Records => _records.ToArray();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var properties = state is IEnumerable<KeyValuePair<string, object?>> values
            ? values
                .Where(x => x.Key != "{OriginalFormat}")
                .ToDictionary(x => x.Key, x => x.Value)
            : new Dictionary<string, object?>();

        _records.Enqueue(new RecordedLog(
            logLevel,
            formatter(state, exception),
            exception,
            properties));
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose()
        {
        }
    }
}
