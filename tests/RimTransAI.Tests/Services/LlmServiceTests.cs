using FluentAssertions;
using RimTransAI.Services;
using RimTransAI.Tests.Helpers;
using System.Text.Json;
using Xunit;

namespace RimTransAI.Tests.Services;

public class LlmServiceTests
{
    [Fact]
    public async Task TranslateBatchAsync_WithCancelledToken_PropagatesCancellation()
    {
        var logger = new RecordingLogger<LlmService>();
        using var service = new LlmService(logger);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var operation = () => service.TranslateBatchAsync(
            "api-key",
            new Dictionary<string, string> { ["source"] = "source" },
            "https://example.com",
            "model",
            cancellationToken: cts.Token);

        await operation.Should().ThrowAsync<OperationCanceledException>();
        logger.Records.Any(record => record.Message == "LLM 请求已取消")
            .Should().BeTrue();
        logger.Records.Any(record =>
                record.Properties.TryGetValue("Model", out var model) &&
                Equals(model, "model"))
            .Should().BeTrue();

        var requestRecord = logger.Records.Single(record =>
            record.Properties.ContainsKey("RequestJson"));
        requestRecord.Level.Should().Be(Microsoft.Extensions.Logging.LogLevel.Debug);

        var requestJson = requestRecord.Properties["RequestJson"].Should().BeOfType<string>().Subject;
        using var request = JsonDocument.Parse(requestJson);
        request.RootElement.GetProperty("endpoint").GetString()
            .Should().Be("https://example.com/v1");
        request.RootElement.GetProperty("model").GetString().Should().Be("model");
        request.RootElement.GetProperty("temperature").GetDouble().Should().Be(0.3);
        request.RootElement.GetProperty("response_format").GetProperty("type").GetString()
            .Should().Be("json_object");
        request.RootElement.GetProperty("timeout_seconds").GetInt32().Should().Be(480);

        var messages = request.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        messages.Should().HaveCount(2);
        messages[0].GetProperty("role").GetString().Should().Be("system");
        messages[0].GetProperty("content").GetString().Should().Contain("Simplified Chinese");
        messages[1].GetProperty("role").GetString().Should().Be("user");

        using var userContent = JsonDocument.Parse(messages[1].GetProperty("content").GetString()!);
        userContent.RootElement.GetProperty("source").GetString().Should().Be("source");
        requestJson.Should().NotContain("api-key");
    }

    [Theory]
    [InlineData("https://api.openai.com", "https://api.openai.com/v1/chat/completions")]
    [InlineData("https://api.openai.com/", "https://api.openai.com/v1/chat/completions")]
    [InlineData("https://api.openai.com/v1", "https://api.openai.com/v1/chat/completions")]
    [InlineData("https://api.openai.com/v1/", "https://api.openai.com/v1/chat/completions")]
    [InlineData("https://api.openai.com/v1/chat/completions", "https://api.openai.com/v1/chat/completions")]
    [InlineData("https://api.openai.com/v1/chat/completions/", "https://api.openai.com/v1/chat/completions")]
    // 无 /v1 前缀的 chat/completions 路径不应再被错误拼接
    [InlineData("https://api.deepseek.com/chat/completions", "https://api.deepseek.com/chat/completions")]
    [InlineData("http://localhost:11434/v1/chat/completions", "http://localhost:11434/v1/chat/completions")]
    public void NormalizeApiUrl_ReturnsExpectedUrl(string input, string expected)
    {
        // Act
        var result = LlmService.NormalizeApiUrl(input);

        // Assert
        result.Should().Be(expected);
    }
}
