using FluentAssertions;
using RimTransAI.Models;
using RimTransAI.Services;
using Serilog.Extensions.Logging;
using Serilog;
using Xunit;

namespace RimTransAI.Tests.Services;

public class OperationLogSinkTests
{
    [Fact]
    public void NonDebugMode_OnlyEmitsUserVisibleAndErrorEvents()
    {
        var buffer = new OperationLogBuffer();
        var sink = new OperationLogSink(() => false);
        sink.Attach(buffer);
        using var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("diagnostic info");
        logger.Warning("internal warning");
        logger.ForContext(OperationLogSink.UiVisibleProperty, true)
            .ForContext(OperationLogSink.UiLevelProperty, nameof(OperationLogLevel.Success))
            .Information("key step");
        logger.Error("critical failure");

        buffer.Entries.Select(entry => entry.Message)
            .Should().Equal("key step", "critical failure");
        buffer.Entries.Select(entry => entry.Level)
            .Should().Equal(OperationLogLevel.Success, OperationLogLevel.Error);
    }

    [Fact]
    public void DebugMode_EmitsDiagnosticsAndRedactsSecrets()
    {
        var buffer = new OperationLogBuffer();
        var sink = new OperationLogSink(
            () => true,
            value => ApiKeyRedactor.Redact(value, "secret-key"));
        sink.Attach(buffer);
        using var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Debug("request uses secret-key");

        buffer.Entries.Should().ContainSingle();
        buffer.Entries[0].Message.Should().Be($"[DBG] request uses {ApiKeyRedactor.RedactedValue}");
        buffer.Entries[0].Level.Should().Be(OperationLogLevel.Info);
    }

    [Fact]
    public void MicrosoftLogger_UserEventScope_ReachesOperationSink()
    {
        var buffer = new OperationLogBuffer();
        var sink = new OperationLogSink(() => false);
        sink.Attach(buffer);
        using var serilog = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();
        using var provider = new SerilogLoggerProvider(serilog, dispose: false);
        var logger = provider.CreateLogger(nameof(OperationLogSinkTests));

        logger.LogUserSuccess("扫描完成，共 {Count} 条", 12);

        buffer.Entries.Should().ContainSingle();
        buffer.Entries[0].Message.Should().Be("扫描完成，共 12 条");
        buffer.Entries[0].Level.Should().Be(OperationLogLevel.Success);
    }
}
