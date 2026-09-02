using System.Text.Json;
using System.Linq;
using FluentAssertions;
using RimTransAI.Models;
using RimTransAI.Services;
using Xunit;

namespace RimTransAI.Tests.Services;

public class AppJsonContextTests
{
    [Fact]
    public void AppConfig_WithApiProfiles_ShouldSerializeAndDeserializeAllFields()
    {
        var source = new ModSourceFolder
        {
            Id = "source-1",
            DisplayName = "Steam Workshop",
            FolderPath = @"D:\RimWorld\Mods",
            IconKey = "FolderMultipleOutline",
            IsEnabled = true
        };
        var config = new AppConfig
        {
            TranslationApis =
            [
                new TranslationApiProfile
                {
                    Name = "Test API",
                    ApiUrl = "https://api.example.com",
                    ApiKey = "test-key",
                    AutoCompleteApiUrl = false,
                    RequestTimeoutSeconds = 720,
                    Models = [new TranslationApiModel { ModelId = "test-model" }]
                }
            ],
            ModSourceFolders = [source]
        };

        var json = JsonSerializer.Serialize(config, AppJsonContext.Default.AppConfig);
        var restored = JsonSerializer.Deserialize(json, AppJsonContext.Default.AppConfig);
        using var document = JsonDocument.Parse(json);

        document.RootElement.EnumerateObject().First().Name.Should().Be("version");
        document.RootElement.EnumerateObject().Skip(1).First().Name.Should().Be("configVersion");
        document.RootElement.GetProperty("configVersion").GetInt32().Should().Be(AppConfig.CurrentConfigVersion);
        json.Should().Contain("\"TranslationApis\"");
        json.Should().NotContain("\"ApiRequestTimeoutSeconds\"");
        json.Should().NotContain("\"IconKind\"");

        restored.Should().NotBeNull();
        restored!.TranslationApis.Should().ContainSingle();
        restored.TranslationApis[0].Name.Should().Be("Test API");
        restored.TranslationApis[0].RequestTimeoutSeconds.Should().Be(720);
        restored.TranslationApis[0].Models.Should().ContainSingle();
        restored.TranslationApis[0].Models[0].ModelId.Should().Be("test-model");
        restored.ModSourceFolders.Should().ContainSingle();
        restored.ModSourceFolders[0].Id.Should().Be("source-1");
        restored.ModSourceFolders[0].DisplayName.Should().Be("Steam Workshop");
    }
}
