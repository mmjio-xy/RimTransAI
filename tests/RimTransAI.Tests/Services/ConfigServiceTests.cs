using FluentAssertions;
using RimTransAI.Models;
using RimTransAI.Services;
using Xunit;

namespace RimTransAI.Tests.Services;

public class ConfigServiceTests
{
    [Fact]
    public void Constructor_CreatesInstanceWithDefaultConfig()
    {
        // Act
        var service = new ConfigService();

        // Assert
        service.CurrentConfig.Should().NotBeNull();
    }

    [Fact]
    public void CurrentConfig_HasValidValues()
    {
        // Arrange
        var service = new ConfigService();

        // Assert - 验证配置有有效值（可能是默认值或已保存的配置）
        service.CurrentConfig.ApiUrl.Should().NotBeNullOrEmpty();
        service.CurrentConfig.TargetLanguage.Should().NotBeNullOrEmpty();
        service.CurrentConfig.AppTheme.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void SaveConfig_UpdatesCurrentConfig()
    {
        // Arrange
        var service = new ConfigService();
        var newConfig = new AppConfig
        {
            ApiUrl = "https://new.api.com",
            ApiKey = "new-key",
            TargetModel = "new-model",
            TargetLanguage = "ChineseTraditional",
            AppTheme = "Dark",
            DebugMode = true
        };

        // Act
        service.SaveConfig(newConfig);

        // Assert
        service.CurrentConfig.ApiUrl.Should().Be("https://new.api.com");
        service.CurrentConfig.DebugMode.Should().BeTrue();
    }
}
