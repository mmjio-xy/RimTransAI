using FluentAssertions;
using RimTransAI.Models;
using RimTransAI.Services;
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
}
