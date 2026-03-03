using FluentAssertions;
using RimTransAI.Models;
using RimTransAI.Services;
using System.Xml.Linq;
using Xunit;

namespace RimTransAI.Tests.Services;

public class FileGeneratorServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileGeneratorService _service;

    public FileGeneratorServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"RimTransAI_Test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _service = new FileGeneratorService();
        Logger.Initialize();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public void GenerateFiles_WithEmptyItems_ReturnsZero()
    {
        // Arrange
        var items = new List<TranslationItem>();

        // Act
        var result = _service.GenerateFiles(_tempDir, "ChineseSimplified", items);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void GenerateFiles_WithNoTranslatedItems_ReturnsZero()
    {
        // Arrange
        var items = new List<TranslationItem>
        {
            new() { Key = "Test.label", Status = "等待中", TranslatedText = "" }
        };

        // Act
        var result = _service.GenerateFiles(_tempDir, "ChineseSimplified", items);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void GenerateFiles_BackstoryItems_WritesLegacyBackstoriesFile()
    {
        // Arrange
        var items = new List<TranslationItem>
        {
            new()
            {
                DefType = "BackstoryDef",
                Version = "1.5",
                FilePath = Path.Combine(_tempDir, "1.5", "Languages", "English", "Backstories", "Backstories.xml"),
                Key = "Drone42.title",
                OriginalText = "Drone",
                TranslatedText = "无人机",
                Status = "已翻译"
            },
            new()
            {
                DefType = "BackstoryDef",
                Version = "1.5",
                FilePath = Path.Combine(_tempDir, "1.5", "Languages", "English", "Backstories", "Backstories.xml"),
                Key = "Drone42.description",
                OriginalText = "(none)",
                TranslatedText = "（无）",
                Status = "已翻译"
            }
        };

        // Act
        var result = _service.GenerateFiles(_tempDir, "ChineseSimplified", items);

        // Assert
        result.Should().Be(1);

        var outputPath = Path.Combine(_tempDir, "1.5", "Languages", "ChineseSimplified", "Backstories", "Backstories.xml");
        File.Exists(outputPath).Should().BeTrue();

        var doc = XDocument.Load(outputPath);
        doc.Root!.Name.LocalName.Should().Be("BackstoryTranslations");
        var entry = doc.Root.Elements().FirstOrDefault(x => x.Name.LocalName == "Drone42");
        entry.Should().NotBeNull();
        entry!.Element("title")!.Value.Should().Be("无人机");
        entry.Element("desc")!.Value.Should().Be("（无）");
    }

    [Fact]
    public void GenerateFiles_CodeLinkedSource_SavesIntoKeyedFolder()
    {
        // Arrange
        var items = new List<TranslationItem>
        {
            new()
            {
                DefType = "Keyed",
                Version = "1.5",
                FilePath = Path.Combine(_tempDir, "1.5", "Languages", "English", "CodeLinked", "All.xml"),
                Key = "Greeting",
                OriginalText = "Hello",
                TranslatedText = "你好",
                Status = "已翻译"
            }
        };

        // Act
        var result = _service.GenerateFiles(_tempDir, "ChineseSimplified", items);

        // Assert
        result.Should().Be(1);
        var outputPath = Path.Combine(_tempDir, "1.5", "Languages", "ChineseSimplified", "Keyed", "All.xml");
        File.Exists(outputPath).Should().BeTrue();
    }

    [Fact]
    public void GenerateFiles_DefInjectedSource_PreservesRelativeSubPath()
    {
        // Arrange
        var items = new List<TranslationItem>
        {
            new()
            {
                DefType = "ThingDef",
                Version = "1.5",
                FilePath = Path.Combine(_tempDir, "1.5", "Languages", "English", "DefInjected", "ThingDef", "SubDir", "Buildings.xml"),
                Key = "A.label",
                OriginalText = "A",
                TranslatedText = "甲",
                Status = "已翻译"
            }
        };

        // Act
        var result = _service.GenerateFiles(_tempDir, "ChineseSimplified", items);

        // Assert
        result.Should().Be(1);
        var outputPath = Path.Combine(_tempDir, "1.5", "Languages", "ChineseSimplified", "DefInjected", "ThingDef", "SubDir", "Buildings.xml");
        File.Exists(outputPath).Should().BeTrue();
    }
}
