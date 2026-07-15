using Avalonia.Logging;
using FluentAssertions;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using RimTransAI.Services;
using Xunit;

namespace RimTransAI.Tests.Services;

public class AvaloniaSerilogSinkTests
{
    [Fact]
    public void Log_PreservesLevelAreaSourceAndFormattedMessage()
    {
        var collector = new CollectingSink();
        using var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(collector)
            .CreateLogger();
        var sink = new AvaloniaSerilogSink(logger);
        var source = new object();

        sink.Log(Avalonia.Logging.LogEventLevel.Warning, "Binding", source, "Value {0} is invalid", 42);

        var logEvent = collector.Events.Should().ContainSingle().Subject;
        logEvent.Level.Should().Be(Serilog.Events.LogEventLevel.Warning);
        logEvent.Properties["SourceContext"].ToString().Should().Be("\"Avalonia.Binding\"");
        logEvent.Properties["AvaloniaSource"].ToString().Should().Be("\"System.Object\"");
        logEvent.Properties["AvaloniaMessage"].ToString().Should().Be("\"Value 42 is invalid\"");
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
