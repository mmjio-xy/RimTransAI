using FluentAssertions;
using RimTransAI.Models;
using Xunit;

namespace RimTransAI.Tests.Models;

public class TranslationItemTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        // Act
        var item = new TranslationItem();

        // Assert
        item.Key.Should().BeEmpty();
        item.OriginalText.Should().BeEmpty();
        item.TranslatedText.Should().BeEmpty();
        item.Status.Should().Be("等待中");
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        // Arrange
        var item = new TranslationItem();

        // Act
        item.Key = "TestDef.label";
        item.OriginalText = "Test Item";
        item.TranslatedText = "测试物品";
        item.Status = "已翻译";
        item.DefType = "ThingDef";
        item.Version = "1.5";
        item.FilePath = "Defs/Test.xml";

        // Assert
        item.Key.Should().Be("TestDef.label");
        item.OriginalText.Should().Be("Test Item");
        item.TranslatedText.Should().Be("测试物品");
        item.Status.Should().Be("已翻译");
        item.DefType.Should().Be("ThingDef");
        item.Version.Should().Be("1.5");
        item.FilePath.Should().Be("Defs/Test.xml");
    }
}
