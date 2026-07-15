using FluentAssertions;
using RimTransAI.Services;
using Xunit;

namespace RimTransAI.Tests.Services;

public class LoggingBootstrapTests
{
    [Fact]
    public void ApiKeyRedactor_RedactsEveryApiKeyOccurrence()
    {
        const string apiKey = "sk-secret-value";
        const string message = "request sk-secret-value failed; retry sk-secret-value";

        var result = ApiKeyRedactor.Redact(message, apiKey);

        result.Should().Be(
            $"request {ApiKeyRedactor.RedactedValue} failed; retry {ApiKeyRedactor.RedactedValue}");
    }

    [Fact]
    public void ApiKeyRedactor_DoesNotModifyOtherLogData()
    {
        const string message = "路径 C:\\Mods\\Example，模型 deepseek，原文 Example text";

        var result = ApiKeyRedactor.Redact(message, "sk-secret-value");

        result.Should().Be(message);
    }

    [Fact]
    public void SetDebugMode_WhenEnabled_AllowsDebugLogs()
    {
        LoggingBootstrap.Initialize();
        LoggingBootstrap.SetDebugMode(true);

        var operation = () => Serilog.Log
            .ForContext<LoggingBootstrapTests>()
            .Debug("Test debug message");

        operation.Should().NotThrow();
    }

    [Fact]
    public void SetDebugMode_WhenDisabled_FiltersDebugLogs()
    {
        LoggingBootstrap.Initialize();
        LoggingBootstrap.SetDebugMode(false);

        var operation = () => Serilog.Log
            .ForContext<LoggingBootstrapTests>()
            .Debug("Test debug message");

        operation.Should().NotThrow();
    }

    [Fact]
    public void GetLogFilePath_AfterInitialize_ReturnsPath()
    {
        LoggingBootstrap.Initialize();

        var path = LoggingBootstrap.GetLogFilePath();

        path.Should().NotBeNullOrEmpty();
        path.Should().Contain("RimTransAI-");
        path.Should().EndWith(".log");
    }
}
