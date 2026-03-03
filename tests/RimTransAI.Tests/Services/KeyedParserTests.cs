using System.Reflection;
using FluentAssertions;
using RimTransAI.Models;
using RimTransAI.Services;
using Xunit;

namespace RimTransAI.Tests.Services;

public class KeyedParserTests
{
    private static List<TranslationItem> InvokeParseKeyedFile(string filePath, string version = "")
    {
        var service = new ModParserService(new ReflectionAnalyzer(), new ConfigService());
        var method = typeof(ModParserService).GetMethod("ParseKeyedFile", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull("ParseKeyedFile must exist for keyed parsing");

        var result = method!.Invoke(service, new object[] { filePath, version });
        result.Should().BeOfType<List<TranslationItem>>();
        return (List<TranslationItem>)result!;
    }

    [Fact]
    public void ParseKeyedFile_ValidFile_ExtractsAllElements()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(tempFile, """
            <LanguageData>
              <simple>Hello</simple>
              <multiline>Line1\nLine2</multiline>
              <todo>TODO</todo>
              <simple>Second</simple>
            </LanguageData>
            """);

            // Act
            var results = InvokeParseKeyedFile(tempFile, "1.5");

            // Assert
            results.Should().HaveCount(2);
            results.Should().Contain(x => x.Key == "simple" && x.OriginalText == "Hello");
            results.Should().Contain(x => x.Key == "multiline" && x.OriginalText == "Line1\nLine2");
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void ParseKeyedFile_EmptyElements_AreFiltered()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(tempFile, """
            <LanguageData>
              <empty></empty>
              <blank>   </blank>
              <todo>TODO</todo>
            </LanguageData>
            """);

            // Act
            var results = InvokeParseKeyedFile(tempFile);

            // Assert
            results.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void ParseKeyedFile_NonLanguageDataRoot_ReturnsEmpty()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(tempFile, """
            <Defs>
              <ThingDef>
                <defName>Test</defName>
              </ThingDef>
            </Defs>
            """);

            // Act
            var results = InvokeParseKeyedFile(tempFile);

            // Assert
            results.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
