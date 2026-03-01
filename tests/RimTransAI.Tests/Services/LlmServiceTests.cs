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
    public void NormalizeApiUrl_ReturnsExpectedUrl(string input, string expected)
    {
        // Act
        var result = LlmService.NormalizeApiUrl(input);

        // Assert
        result.Should().Be(expected);
    }
}
