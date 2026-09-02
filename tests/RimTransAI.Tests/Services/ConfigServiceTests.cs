using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using RimTransAI.Models;
using RimTransAI.Services;
using RimTransAI.Tests.Helpers;
using Xunit;

namespace RimTransAI.Tests.Services;

public class ConfigServiceTests
{
    [Fact]
    public void NormalizeConfig_ClampsConcurrencyAndApiTimeoutValues()
    {
        var config = new AppConfig
        {
            MaxThreads = 0,
            ThreadIntervalMs = 5000,
            TranslationApis =
            [
                new TranslationApiProfile
                {
                    Name = " Primary ",
                    RequestTimeoutSeconds = 5,
                    Models = [new TranslationApiModel { ModelId = " model-a " }]
                }
            ]
        };

        ConfigService.NormalizeConfig(config);

        config.Version.Should().Be(AppConfig.CurrentProgramVersion);
        config.MaxThreads.Should().Be(1);
        config.ThreadIntervalMs.Should().Be(1000);
        config.TranslationApis[0].Name.Should().Be("Primary");
        config.TranslationApis[0].RequestTimeoutSeconds.Should().Be(30);
        config.TranslationApis[0].Models[0].ModelId.Should().Be("model-a");
    }

    [Fact]
    public void Constructor_CreatesDefaultConfigWithApiProfile()
    {
        using var scope = new TemporaryConfigFile();

        var service = new ConfigService(filePath: scope.FilePath);

        service.CurrentConfig.TranslationApis.Should().ContainSingle();
        service.CurrentConfig.TranslationApis[0].ApiUrl.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void SaveConfig_UpdatesCurrentConfig()
    {
        using var scope = new TemporaryConfigFile();
        var service = new ConfigService(filePath: scope.FilePath);
        var newConfig = new AppConfig
        {
            TranslationApis =
            [
                new TranslationApiProfile
                {
                    Name = "Secondary",
                    ApiUrl = "https://new.api.com",
                    ApiKey = "new-key",
                    Models = [new TranslationApiModel { ModelId = "new-model" }]
                }
            ],
            TargetLanguage = "ChineseTraditional",
            AppTheme = "Dark",
            DebugMode = true
        };

        var saved = service.SaveConfig(newConfig);

        saved.Should().BeTrue();
        service.CurrentConfig.TranslationApis.Should().ContainSingle();
        service.CurrentConfig.TranslationApis[0].Name.Should().Be("Secondary");
        service.CurrentConfig.TranslationApis[0].ApiUrl.Should().Be("https://new.api.com");
        service.CurrentConfig.DebugMode.Should().BeTrue();
    }

    [Fact]
    public void LoadConfig_WithoutVersion_MigratesLegacyApiFieldsAndWritesNewFormat()
    {
        using var scope = new TemporaryConfigFile();
        File.WriteAllText(
            scope.FilePath,
            """
            {
              "ApiUrl": "https://legacy.example.com",
              "ApiKey": "legacy-key",
              "TargetModel": "legacy-model",
              "AutoCompleteApiUrl": false,
              "ApiRequestTimeoutSeconds": 720,
              "TargetLanguage": "ChineseTraditional"
            }
            """);

        var service = new ConfigService(filePath: scope.FilePath);
        var migratedJson = File.ReadAllText(scope.FilePath);
        using var document = JsonDocument.Parse(migratedJson);

        service.CurrentConfig.TranslationApis.Should().ContainSingle();
        var api = service.CurrentConfig.TranslationApis[0];
        api.Name.Should().Be("默认 API");
        api.IsEnabled.Should().BeTrue();
        api.ApiUrl.Should().Be("https://legacy.example.com");
        api.ApiKey.Should().Be("legacy-key");
        api.AutoCompleteApiUrl.Should().BeFalse();
        api.RequestTimeoutSeconds.Should().Be(720);
        api.Models.Should().ContainSingle();
        api.Models[0].ModelId.Should().Be("legacy-model");
        document.RootElement.EnumerateObject().First().Name.Should().Be("version");
        migratedJson.Should().NotContain("ApiRequestTimeoutSeconds");
        migratedJson.Should().NotContain("TargetModel");
    }

    [Fact]
    public void LoadConfig_WithVersion_DoesNotTreatLegacyFieldsAsMigratable()
    {
        using var scope = new TemporaryConfigFile();
        File.WriteAllText(
            scope.FilePath,
            """
            {
              "version": "0.1.0",
              "ApiUrl": "https://legacy.example.com",
              "ApiKey": "legacy-key",
              "TargetModel": "legacy-model"
            }
            """);

        var service = new ConfigService(filePath: scope.FilePath);

        service.CurrentConfig.TranslationApis.Should().ContainSingle();
        service.CurrentConfig.TranslationApis[0].ApiUrl.Should().Be("https://api.openai.com");
        service.CurrentConfig.TranslationApis[0].ApiKey.Should().BeEmpty();
    }

    [Fact]
    public void SaveConfig_WhenTargetIsDirectory_ReturnsFalseAndLogsError()
    {
        using var scope = new TemporaryConfigFile();
        var logger = new RecordingLogger<ConfigService>();
        var service = new ConfigService(logger, filePath: scope.DirectoryPath);

        var saved = service.SaveConfig(new AppConfig());

        saved.Should().BeFalse();
        logger.Records.Should().Contain(record =>
            record.Level == Microsoft.Extensions.Logging.LogLevel.Error &&
            record.Message.Contains("保存配置失败", StringComparison.Ordinal));
    }

    [Fact]
    public void Constructor_WhenJsonIsInvalid_UsesDefaultsAndLogsUserWarning()
    {
        using var scope = new TemporaryConfigFile();
        File.WriteAllText(scope.FilePath, "{ invalid json");
        var logger = new RecordingLogger<ConfigService>();

        var service = new ConfigService(logger, scope.FilePath);

        service.CurrentConfig.Should().NotBeNull();
        logger.Records.Should().Contain(record =>
            record.Level == Microsoft.Extensions.Logging.LogLevel.Warning &&
            record.Message.Contains("已使用默认配置", StringComparison.Ordinal));
    }

    private sealed class TemporaryConfigFile : IDisposable
    {
        public TemporaryConfigFile()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), $"rta_config_{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
            FilePath = Path.Combine(DirectoryPath, "settings.json");
        }

        public string DirectoryPath { get; }
        public string FilePath { get; }

        public void Dispose()
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
