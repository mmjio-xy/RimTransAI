using FluentAssertions;
using RimTransAI.Models;
using RimTransAI.Services;
using RimTransAI.Tests.Helpers;
using Xunit;

namespace RimTransAI.Tests.Services;

public class ConfigServiceTests
{
    [Fact]
    public void NormalizeConfig_ClampsConcurrencyAndTimeoutValues()
    {
        var config = new AppConfig
        {
            MaxThreads = 0,
            ThreadIntervalMs = 5000,
            ApiRequestTimeoutSeconds = 5
        };

        ConfigService.NormalizeConfig(config);

        config.MaxThreads.Should().Be(1);
        config.ThreadIntervalMs.Should().Be(1000);
        config.ApiRequestTimeoutSeconds.Should().Be(30);
    }

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
        var saved = service.SaveConfig(newConfig);

        // Assert
        saved.Should().BeTrue();
        service.CurrentConfig.ApiUrl.Should().Be("https://new.api.com");
        service.CurrentConfig.DebugMode.Should().BeTrue();
    }

    [Fact]
    public void SaveConfig_WhenTargetIsDirectory_ReturnsFalseAndLogsError()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rta_config_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var logger = new RecordingLogger<ConfigService>();
            var service = new ConfigService(logger, filePath: directory);

            var saved = service.SaveConfig(new AppConfig { ApiUrl = "https://example.com" });

            saved.Should().BeFalse();
            logger.Records.Should().Contain(record =>
                record.Level == Microsoft.Extensions.Logging.LogLevel.Error &&
                record.Message.Contains("保存配置失败", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Constructor_WhenJsonIsInvalid_UsesDefaultsAndLogsUserWarning()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rta_config_json_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, "settings.json");
        File.WriteAllText(filePath, "{ invalid json");
        try
        {
            var logger = new RecordingLogger<ConfigService>();

            var service = new ConfigService(logger, filePath);

            service.CurrentConfig.Should().NotBeNull();
            logger.Records.Should().Contain(record =>
                record.Level == Microsoft.Extensions.Logging.LogLevel.Warning &&
                record.Message.Contains("已使用默认配置", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
