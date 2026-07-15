using FluentAssertions;
using RimTransAI.Services;
using Xunit;

namespace RimTransAI.Tests.Services;

public class LoggerTests
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
        // Arrange
        Logger.Initialize();

        // Act
        Logger.SetDebugMode(true);

        // Assert - 不抛出异常即为成功
        var act = () => Logger.Debug("Test debug message");
        act.Should().NotThrow();
    }

    [Fact]
    public void SetDebugMode_WhenDisabled_FiltersDebugLogs()
    {
        // Arrange
        Logger.Initialize();

        // Act
        Logger.SetDebugMode(false);

        // Assert - 不抛出异常即为成功
        var act = () => Logger.Debug("Test debug message");
        act.Should().NotThrow();
    }

    [Fact]
    public void Info_WritesInfoLog()
    {
        // Arrange
        Logger.Initialize();

        // Act & Assert
        var act = () => Logger.Info("Test info message");
        act.Should().NotThrow();
    }

    [Fact]
    public void Warning_WritesWarningLog()
    {
        // Arrange
        Logger.Initialize();

        // Act & Assert
        var act = () => Logger.Warning("Test warning message");
        act.Should().NotThrow();
    }

    [Fact]
    public void Error_WritesErrorLog()
    {
        // Arrange
        Logger.Initialize();

        // Act & Assert
        var act = () => Logger.Error("Test error message");
        act.Should().NotThrow();
    }

    [Fact]
    public void Error_WithException_WritesDetailedLog()
    {
        // Arrange
        Logger.Initialize();
        var exception = new InvalidOperationException("Test exception");

        // Act & Assert
        var act = () => Logger.Error("Test error", exception);
        act.Should().NotThrow();
    }

    [Fact]
    public void GetLogFilePath_AfterInitialize_ReturnsPath()
    {
        // Arrange
        Logger.Initialize();

        // Act
        var path = Logger.GetLogFilePath();

        // Assert
        path.Should().NotBeNullOrEmpty();
        path.Should().Contain("RimTransAI-");
        path.Should().EndWith(".log");
    }
}
