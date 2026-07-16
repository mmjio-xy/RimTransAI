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

    [Fact]
    public void GenerateFilesDetailed_WhenNodeCannotBeCreated_ReportsPartialSuccess()
    {
        var items = new List<TranslationItem>
        {
            new()
            {
                DefType = "Keyed",
                Version = "1.5",
                FilePath = Path.Combine(_tempDir, "1.5", "Languages", "English", "Keyed", "All.xml"),
                Key = "invalid key with spaces",
                OriginalText = "Hello",
                TranslatedText = "你好",
                Status = "已翻译"
            }
        };

        var result = _service.GenerateFilesDetailed(_tempDir, "ChineseSimplified", items);

        result.SuccessfulFileCount.Should().Be(1);
        result.FailedFileCount.Should().Be(0);
        result.FailedNodeCount.Should().Be(1);
        result.IsCompleteSuccess.Should().BeFalse();
    }

    [Fact]
    public void GenerateFilesDetailed_WhenBackstoryFieldIsUnsupported_ReportsPartialSuccess()
    {
        var items = new List<TranslationItem>
        {
            new()
            {
                DefType = "BackstoryDef",
                Version = "1.5",
                FilePath = Path.Combine(_tempDir, "1.5", "Languages", "English", "Backstories", "Backstories.xml"),
                Key = "Drone42.unsupported",
                OriginalText = "Hello",
                TranslatedText = "你好",
                Status = "已翻译"
            }
        };

        var result = _service.GenerateFilesDetailed(_tempDir, "ChineseSimplified", items);

        result.SuccessfulFileCount.Should().Be(1);
        result.FailedNodeCount.Should().Be(1);
        result.IsCompleteSuccess.Should().BeFalse();
    }

    [Fact]
    public void GenerateFilesDetailed_WithMultipleVersions_ReportsSuccessfulVersions()
    {
        var items = new[]
        {
            new TranslationItem
            {
                DefType = "Keyed",
                Version = "1.5",
                FilePath = Path.Combine(_tempDir, "1.5", "Languages", "English", "Keyed", "One.xml"),
                Key = "One",
                OriginalText = "One",
                TranslatedText = "一",
                Status = "已翻译"
            },
            new TranslationItem
            {
                DefType = "Keyed",
                Version = "1.6",
                FilePath = Path.Combine(_tempDir, "1.6", "Languages", "English", "Keyed", "Two.xml"),
                Key = "Two",
                OriginalText = "Two",
                TranslatedText = "二",
                Status = "已翻译"
            }
        };

        var result = _service.GenerateFilesDetailed(_tempDir, "ChineseSimplified", items);

        result.SuccessfulFileCount.Should().Be(2);
        result.SuccessfulVersions.Should().Equal("1.5", "1.6");
    }

    [Fact]
    public void GenerateFilesDetailed_WhenFieldIsExcluded_RemovesItFromGeneratedXml()
    {
        var outputPath = Path.Combine(
            _tempDir,
            "1.5",
            "Languages",
            "ChineseSimplified",
            "Keyed",
            "All.xml");
        var items = new[]
        {
            new TranslationItem
            {
                DefType = "Keyed",
                Version = "1.5",
                FilePath = Path.Combine(_tempDir, "1.5", "Languages", "English", "Keyed", "All.xml"),
                Key = "Keep",
                OriginalText = "Keep",
                TranslatedText = "保留",
                Status = "已翻译"
            },
            new TranslationItem
            {
                DefType = "Keyed",
                Version = "1.5",
                FilePath = Path.Combine(_tempDir, "1.5", "Languages", "English", "Keyed", "All.xml"),
                Key = "Remove",
                OriginalText = "Remove",
                TranslatedText = "删除",
                Status = "已翻译",
                IsExcluded = true
            }
        };

        _service.GenerateFilesDetailed(_tempDir, "ChineseSimplified", items);

        var document = XDocument.Load(outputPath);
        document.Root!.Element("Keep")!.Value.Should().Be("保留");
        document.Root.Element("Remove").Should().BeNull();
    }

    [Fact]
    public void GenerateFilesDetailed_WhenEveryFieldInOutputIsExcluded_DeletesOldFile()
    {
        var outputPath = Path.Combine(
            _tempDir,
            "1.5",
            "Languages",
            "ChineseSimplified",
            "Keyed",
            "All.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, "<LanguageData><Old>旧内容</Old></LanguageData>");
        var item = new TranslationItem
        {
            DefType = "Keyed",
            Version = "1.5",
            FilePath = Path.Combine(_tempDir, "1.5", "Languages", "English", "Keyed", "All.xml"),
            Key = "Old",
            OriginalText = "Old",
            TranslatedText = "旧内容",
            Status = "已翻译",
            IsExcluded = true
        };

        var result = _service.GenerateFilesDetailed(_tempDir, "ChineseSimplified", [item]);

        File.Exists(outputPath).Should().BeFalse();
        result.DeletedFileCount.Should().Be(1);
        result.SuccessfulVersions.Should().ContainSingle().Which.Should().Be("1.5");
    }
}
