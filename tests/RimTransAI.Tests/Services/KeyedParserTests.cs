using FluentAssertions;
using RimTransAI.Services.Scanning;
using Xunit;

namespace RimTransAI.Tests.Services;

public class KeyedParserTests
{
    [Fact]
    public void Extract_WithValidKeyedFile_ExtractsAllLeafElements()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(tempFile, """
                <LanguageData>
                  <simple>Hello</simple>
                  <multiline>Line1\nLine2</multiline>
                </LanguageData>
                """);

            var sources = new XmlSourceCollection();
            sources.KeyedFiles.Add(new XmlSourceFile(
                tempFile,
                "Languages/English/Keyed/Main.xml",
                "1.5",
                "Keyed",
                0));

            var result = new DefFieldExtractionEngine().Extract(
                new ScanContext(Path.GetTempPath(), "English", "English", [], "1.5"),
                sources,
                new Dictionary<string, HashSet<string>>());

            result.Should().Contain(x => x.Key == "simple" && x.OriginalText == "Hello");
            result.Should().Contain(x => x.Key == "multiline" && x.OriginalText == "Line1\nLine2");
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
    public void Extract_WithPlaceholderAndEmptyValues_FiltersThemOut()
    {
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

            var sources = new XmlSourceCollection();
            sources.KeyedFiles.Add(new XmlSourceFile(
                tempFile,
                "Languages/English/Keyed/Main.xml",
                "",
                "Keyed",
                0));

            var result = new DefFieldExtractionEngine().Extract(
                new ScanContext(Path.GetTempPath(), "English", "English", [], "1.5"),
                sources,
                new Dictionary<string, HashSet<string>>());

            result.Should().NotContain(x => x.DefType == "Keyed");
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
