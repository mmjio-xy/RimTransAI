using FluentAssertions;
using RimTransAI.Services;
using Xunit;

namespace RimTransAI.Tests.Services;

public class LlmServiceTests
{
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
