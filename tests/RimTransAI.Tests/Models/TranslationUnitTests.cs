using FluentAssertions;
using RimTransAI.Models;
using Xunit;

namespace RimTransAI.Tests.Models;

public class TranslationUnitTests
{
    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var unit = new TranslationUnit
        {
            Key = "ThingDef.Gun.label",
            DefType = "ThingDef",
            OriginalText = "Assault Rifle",
            SourceFile = "Test.xml",
            Context = "//def",
            Version = "1.5"
        };

        // Assert
        unit.Key.Should().Be("ThingDef.Gun.label");
        unit.DefType.Should().Be("ThingDef");
        unit.OriginalText.Should().Be("Assault Rifle");
        unit.SourceFile.Should().Be("Test.xml");
        unit.Context.Should().Be("//def");
        unit.Version.Should().Be("1.5");
    }

    [Fact]
    public void Constructor_WithNullValues_HandlesGracefully()
    {
        // Arrange & Act
        var unit = new TranslationUnit
        {
            Key = null!,
            DefType = null!,
            OriginalText = null!,
            SourceFile = null!,
            Context = null!,
            Version = null!
        };

        // Assert - 不应该抛出异常
        unit.Should().NotBeNull();
    }

    [Fact]
    public void Properties_CanBeSetAndGet()
    {
        // Arrange
        var unit = new TranslationUnit();

        // Act
        unit.Key = "RecipeDef.CraftingTable.label";
        unit.DefType = "RecipeDef";
        unit.OriginalText = "Crafting Table";
        unit.SourceFile = "new/path.xml";
        unit.Context = "//recipe";
        unit.Version = "1.4";

        // Assert
        unit.Key.Should().Be("RecipeDef.CraftingTable.label");
        unit.DefType.Should().Be("RecipeDef");
        unit.OriginalText.Should().Be("Crafting Table");
        unit.SourceFile.Should().Be("new/path.xml");
        unit.Context.Should().Be("//recipe");
        unit.Version.Should().Be("1.4");
    }

    [Fact]
    public void DefaultValues_AreEmpty()
    {
        // Arrange & Act
        var unit = new TranslationUnit();

        // Assert
        unit.Key.Should().Be(string.Empty);
        unit.DefType.Should().Be(string.Empty);
        unit.OriginalText.Should().Be(string.Empty);
        unit.SourceFile.Should().Be(string.Empty);
        unit.Context.Should().Be(string.Empty);
        unit.Version.Should().Be(string.Empty);
    }
}
