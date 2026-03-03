using FluentAssertions;
using RimTransAI.Models;
using RimTransAI.Services;
using System.Text.Json;
using Xunit;

namespace RimTransAI.Tests.Services;

public class AppJsonContextTests
{
    [Fact]
    public void AppConfig_WithModSourceFolders_ShouldSerializeAndDeserializeAllFields()
    {
        // Arrange
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
            ApiUrl = "https://api.example.com",
            ApiKey = "test-key",
            TargetModel = "test-model",
            ApiRequestTimeoutSeconds = 720,
            ModSourceFolders = [source]
        };

        // Act
        var json = JsonSerializer.Serialize(config, AppJsonContext.Default.AppConfig);
        var restored = JsonSerializer.Deserialize(json, AppJsonContext.Default.AppConfig);

        // Assert
        json.Should().Contain("\"DisplayName\"");
        json.Should().Contain("\"FolderPath\"");
        json.Should().Contain("\"IconKey\"");
        json.Should().Contain("\"ApiRequestTimeoutSeconds\"");
        json.Should().NotContain("\"IconKind\"");

        restored.Should().NotBeNull();
        restored!.ModSourceFolders.Should().HaveCount(1);
        restored.ModSourceFolders[0].Id.Should().Be("source-1");
        restored.ModSourceFolders[0].DisplayName.Should().Be("Steam Workshop");
        restored.ModSourceFolders[0].FolderPath.Should().Be(@"D:\RimWorld\Mods");
        restored.ModSourceFolders[0].IconKey.Should().Be("FolderMultipleOutline");
        restored.ModSourceFolders[0].IsEnabled.Should().BeTrue();
        restored.ApiRequestTimeoutSeconds.Should().Be(720);
    }
}
