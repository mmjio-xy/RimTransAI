using System.Xml.Linq;
using FluentAssertions;
using RimTransAI.Services;
using RimTransAI.Tests.Helpers;
using Xunit;

namespace RimTransAI.Tests.Services;

public class TranslationExtractorTests
{
    private readonly Dictionary<string, HashSet<string>> _emptyReflectionMap = new();

    #region 构造函数测试

    [Fact]
    public void Constructor_WithNullReflectionMap_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new TranslationExtractor(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("reflectionMap");
    }

    [Fact]
    public void Constructor_WithEmptyReflectionMap_CreatesInstance()
    {
        // Act
        var extractor = new TranslationExtractor(_emptyReflectionMap);

        // Assert
        extractor.Should().NotBeNull();
    }

    #endregion

    #region 标准字段提取测试

    [Fact]
    public void Extract_WithLabelField_ExtractsLabel()
    {
        // Arrange
        var extractor = new TranslationExtractor(_emptyReflectionMap);
        var def = XmlTestHelper.CreateThingDef("TestItem", label: "Test Label");
        var defs = new List<XElement> { def };

        // Act
        var results = extractor.Extract(defs, "test.xml", "1.5");

        // Assert
        results.Should().ContainSingle();
        results[0].Key.Should().Be("TestItem.label");
        results[0].OriginalText.Should().Be("Test Label");
    }

    [Fact]
    public void Extract_WithDescriptionField_ExtractsDescription()
    {
        // Arrange
        var extractor = new TranslationExtractor(_emptyReflectionMap);
        var def = XmlTestHelper.CreateThingDef("TestItem", description: "Test Description");
        var defs = new List<XElement> { def };

        // Act
        var results = extractor.Extract(defs, "test.xml", "1.5");

        // Assert
        results.Should().ContainSingle();
        results[0].Key.Should().Be("TestItem.description");
        results[0].OriginalText.Should().Be("Test Description");
    }

    [Fact]
    public void Extract_WithLabelAndDescription_ExtractsBoth()
    {
        // Arrange
        var extractor = new TranslationExtractor(_emptyReflectionMap);
        var def = XmlTestHelper.CreateThingDef("TestItem", "Test Label", "Test Description");
        var defs = new List<XElement> { def };

        // Act
        var results = extractor.Extract(defs, "test.xml", "1.5");

        // Assert
        results.Should().HaveCount(2);
        results.Should().Contain(r => r.Key == "TestItem.label");
        results.Should().Contain(r => r.Key == "TestItem.description");
    }

    #endregion

    #region 黑名单过滤测试

    [Theory]
    [InlineData("texPath")]
    [InlineData("graphicPath")]
    [InlineData("soundPath")]
    [InlineData("iconPath")]
    [InlineData("filePath")]
    [InlineData("texturePath")]
    public void Extract_WithBlacklistedPathField_SkipsField(string fieldName)
    {
        // Arrange
        var extractor = new TranslationExtractor(_emptyReflectionMap);
        var def = new XElement("ThingDef",
            new XElement("defName", "TestItem"),
            new XElement(fieldName, "Things/Test/Path"));
        var defs = new List<XElement> { def };

        // Act
        var results = extractor.Extract(defs, "test.xml", "1.5");

        // Assert
        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData("weaponTags")]
    [InlineData("tags")]
    [InlineData("linkableTags")]
    public void Extract_WithBlacklistedTagField_SkipsField(string fieldName)
    {
        // Arrange
        var extractor = new TranslationExtractor(_emptyReflectionMap);
        var def = new XElement("ThingDef",
            new XElement("defName", "TestItem"),
            new XElement(fieldName, "SomeTag"));
        var defs = new List<XElement> { def };

        // Act
        var results = extractor.Extract(defs, "test.xml", "1.5");

        // Assert
        results.Should().BeEmpty();
    }

    #endregion
}
