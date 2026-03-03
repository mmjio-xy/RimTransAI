using FluentAssertions;
using RimTransAI.Models;
using Xunit;

namespace RimTransAI.Tests.Models;

public class AppConfigTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        // Act
        var config = new AppConfig();

        // Assert
        config.ApiUrl.Should().Be("https://api.openai.com");
        config.ApiKey.Should().BeEmpty();
        config.TargetModel.Should().Be("gpt-3.5-turbo");
        config.TargetLanguage.Should().Be("ChineseSimplified");
        config.AppTheme.Should().Be("Light");
        config.AssemblyCSharpPath.Should().BeEmpty();
        config.DebugMode.Should().BeFalse();
        config.ApiRequestTimeoutSeconds.Should().Be(480);
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        // Arrange
        var config = new AppConfig();

        // Act
        config.ApiUrl = "https://custom.api.com";
        config.ApiKey = "test-key";
        config.DebugMode = true;
        config.ApiRequestTimeoutSeconds = 900;

        // Assert
        config.ApiUrl.Should().Be("https://custom.api.com");
        config.ApiKey.Should().Be("test-key");
        config.DebugMode.Should().BeTrue();
        config.ApiRequestTimeoutSeconds.Should().Be(900);
    }
}
