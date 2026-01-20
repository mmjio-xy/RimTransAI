using FluentAssertions;
using RimTransAI.Models;
using Xunit;

namespace RimTransAI.Tests.Models;

public class ModInfoTests
{
    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var modInfo = new ModInfo
        {
            Name = "Test Mod",
            Author = "Test Author",
            Description = "Test Description",
            SupportedVersions = new System.Collections.Generic.List<string> { "1.4", "1.5" }
        };

        // Assert
        modInfo.Name.Should().Be("Test Mod");
        modInfo.Author.Should().Be("Test Author");
        modInfo.Description.Should().Be("Test Description");
        modInfo.SupportedVersions.Should().HaveCount(2);
        modInfo.SupportedVersions.Should().ContainInOrder(new[] { "1.4", "1.5" });
    }

    [Fact]
    public void Constructor_WithNullValues_HandlesGracefully()
    {
        // Arrange & Act
        var modInfo = new ModInfo
        {
            Name = null!,
            Author = null!,
            Description = null!,
            SupportedVersions = null!
        };

        // Assert - 不应该抛出异常
        modInfo.Should().NotBeNull();
    }

    [Fact]
    public void Properties_CanBeSetAndGet()
    {
        // Arrange
        var modInfo = new ModInfo();

        // Act
        modInfo.Name = "New Mod Name";
        modInfo.Author = "New Author";
        modInfo.Description = "New Description";
        modInfo.SupportedVersions = new System.Collections.Generic.List<string> { "1.5" };

        // Assert
        modInfo.Name.Should().Be("New Mod Name");
        modInfo.Author.Should().Be("New Author");
        modInfo.Description.Should().Be("New Description");
        modInfo.SupportedVersions.Should().HaveCount(1);
    }

    [Fact]
    public void ModDependency_DefaultValues_AreEmpty()
    {
        // Arrange & Act
        var dependency = new ModDependency();

        // Assert
        dependency.PackageId.Should().Be(string.Empty);
        dependency.DisplayName.Should().Be(string.Empty);
    }

    [Fact]
    public void ModDependency_CanBeInstantiated()
    {
        // Arrange & Act
        var dependency = new ModDependency
        {
            PackageId = "Core.RimWorld",
            DisplayName = "Core"
        };

        // Assert
        dependency.PackageId.Should().Be("Core.RimWorld");
        dependency.DisplayName.Should().Be("Core");
    }
}
