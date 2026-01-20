using FluentAssertions;
using RimTransAI.Models;
using Xunit;

namespace RimTransAI.Tests.Models;

public class InjectionItemTests
{
    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var item = new InjectionItem
        {
            DefName = "TestDef",
            FieldName = "label",
            OriginalText = "Test label"
        };

        // Assert
        item.DefName.Should().Be("TestDef");
        item.FieldName.Should().Be("label");
        item.OriginalText.Should().Be("Test label");
    }

    [Fact]
    public void FullKey_ReturnsCorrectFormat()
    {
        // Arrange & Act
        var item = new InjectionItem
        {
            DefName = "ThingDef",
            FieldName = "label"
        };

        // Assert
        item.FullKey.Should().Be("ThingDef.label");
    }

    [Fact]
    public void FullKey_WithEmptyValues_ReturnsDot()
    {
        // Arrange & Act
        var item = new InjectionItem
        {
            DefName = "",
            FieldName = ""
        };

        // Assert
        item.FullKey.Should().Be(".");
    }

    [Fact]
    public void DefaultValues_AreEmptyStrings()
    {
        // Arrange & Act
        var item = new InjectionItem();

        // Assert
        item.DefName.Should().Be(string.Empty);
        item.FieldName.Should().Be(string.Empty);
        item.OriginalText.Should().Be(string.Empty);
    }

    [Fact]
    public void Properties_CanBeSetAndGet()
    {
        // Arrange
        var item = new InjectionItem();

        // Act
        item.DefName = "NewDef";
        item.FieldName = "NewField";
        item.OriginalText = "New Text";

        // Assert
        item.DefName.Should().Be("NewDef");
        item.FieldName.Should().Be("NewField");
        item.OriginalText.Should().Be("New Text");
    }
}
