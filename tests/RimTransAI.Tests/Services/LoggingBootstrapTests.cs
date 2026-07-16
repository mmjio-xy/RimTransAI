using FluentAssertions;
using RimTransAI.Services;
using Serilog;
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
    public void ApiKeyRedactor_RedactsCurrentAndPreviousKeys()
    {
        var result = ApiKeyRedactor.Redact(
            "old-key and new-key",
            ["old-key", "new-key"]);

        result.Should().Be(
            $"{ApiKeyRedactor.RedactedValue} and {ApiKeyRedactor.RedactedValue}");
    }

    [Fact]
    public void DebugDisabled_DoesNotCreateLogsDirectoryOrFile()
    {
        var logsDirectory = CreateUnusedLogsDirectoryPath();
        try
        {
            LoggingBootstrap.Shutdown();
            LoggingBootstrap.Initialize(logsDirectory);
            LoggingBootstrap.SetDebugMode(false);

            Log.ForContext<LoggingBootstrapTests>().Information("ordinary event");
            LoggingBootstrap.Shutdown();

            Directory.Exists(logsDirectory).Should().BeFalse();
        }
        finally
        {
            LoggingBootstrap.Shutdown();
            DeleteDirectoryIfPresent(logsDirectory);
        }
    }

    [Fact]
    public void DebugEnabled_CreatesFileAndFlushesDebugEvents()
    {
        var logsDirectory = CreateUnusedLogsDirectoryPath();
        try
        {
            LoggingBootstrap.Shutdown();
            LoggingBootstrap.Initialize(logsDirectory);
            LoggingBootstrap.SetApiKey("secret-key");
            LoggingBootstrap.SetDebugMode(true);

            Log.ForContext<LoggingBootstrapTests>()
                .Debug("debug event with secret-key");
            var path = LoggingBootstrap.GetLogFilePath();
            LoggingBootstrap.Shutdown();

            path.Should().NotBeNullOrEmpty();
            File.Exists(path).Should().BeTrue();
            var content = File.ReadAllText(path!);
            content.Should().Contain("debug event");
            content.Should().Contain(ApiKeyRedactor.RedactedValue);
            content.Should().NotContain("secret-key");
        }
        finally
        {
            LoggingBootstrap.Shutdown();
            DeleteDirectoryIfPresent(logsDirectory);
        }
    }

    private static string CreateUnusedLogsDirectoryPath() =>
        Path.Combine(Path.GetTempPath(), $"RimTransAI_Logging_{Guid.NewGuid():N}", "Logs");

    private static void DeleteDirectoryIfPresent(string logsDirectory)
    {
        var root = Directory.GetParent(logsDirectory)?.FullName;
        if (root != null && Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
