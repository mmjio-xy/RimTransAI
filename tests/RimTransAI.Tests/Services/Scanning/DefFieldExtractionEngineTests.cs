using FluentAssertions;
using RimTransAI.Services.Scanning;
using Xunit;

namespace RimTransAI.Tests.Services.Scanning;

public class DefFieldExtractionEngineTests
{
    [Fact]
    public void Extract_ParsesDefsAndBuildsDefInjectedKey()
    {
        var root = CreateTempRoot();
        try
        {
            var defsFile = Path.Combine(root, "ThingDefs.xml");
            File.WriteAllText(defsFile, """
                <Defs>
                  <ThingDef>
                    <defName>TestGun</defName>
                    <label>Test Gun</label>
                  </ThingDef>
                </Defs>
                """);

            var sources = new XmlSourceCollection();
            sources.DefFiles.Add(new XmlSourceFile(defsFile, "Defs/ThingDefs.xml", "1.5", "Defs", 0));

            var engine = new DefFieldExtractionEngine();
            var result = engine.Extract(
                new ScanContext(root, "English", "English", [], "1.5"),
                sources,
                new Dictionary<string, HashSet<string>>());

            result.Should().ContainSingle();
            result[0].Key.Should().Be("TestGun.label");
            result[0].OriginalText.Should().Be("Test Gun");
            result[0].DefType.Should().Be("ThingDef");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Extract_KeyedUsesSetOrAddSemantics()
    {
        var root = CreateTempRoot();
        try
        {
            var first = Path.Combine(root, "a.xml");
            var second = Path.Combine(root, "b.xml");
            File.WriteAllText(first, "<LanguageData><Greeting>Hello</Greeting><Skip>TODO</Skip></LanguageData>");
            File.WriteAllText(second, "<LanguageData><Greeting>Hi</Greeting></LanguageData>");

            var sources = new XmlSourceCollection();
            sources.KeyedFiles.Add(new XmlSourceFile(first, "Languages/English/Keyed/a.xml", "1.5", "Keyed", 0));
            sources.KeyedFiles.Add(new XmlSourceFile(second, "Languages/English/Keyed/b.xml", "1.5", "Keyed", 1));

            var engine = new DefFieldExtractionEngine();
            var result = engine.Extract(
                new ScanContext(root, "English", "English", [], "1.5"),
                sources,
                new Dictionary<string, HashSet<string>>());

            result.Should().ContainSingle(x => x.DefType == "Keyed" && x.Key == "Greeting" && x.OriginalText == "Hi");
            result.Should().NotContain(x => x.Key == "Skip");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Extract_DefsListField_UsesIndexedPath()
    {
        var root = CreateTempRoot();
        try
        {
            var defsFile = Path.Combine(root, "ThingDefs.xml");
            File.WriteAllText(defsFile, """
                <Defs>
                  <ThingDef>
                    <defName>TestGun</defName>
                    <rulesStrings>
                      <li>Rule A</li>
                      <li>Rule B</li>
                    </rulesStrings>
                  </ThingDef>
                </Defs>
                """);

            var sources = new XmlSourceCollection();
            sources.DefFiles.Add(new XmlSourceFile(defsFile, "Defs/ThingDefs.xml", "1.5", "Defs", 0));

            var engine = new DefFieldExtractionEngine();
            var result = engine.Extract(
                new ScanContext(root, "English", "English", [], "1.5"),
                sources,
                new Dictionary<string, HashSet<string>>());

            result.Should().Contain(x => x.Key == "TestGun.rulesStrings.0" && x.OriginalText == "Rule A");
            result.Should().Contain(x => x.Key == "TestGun.rulesStrings.1" && x.OriginalText == "Rule B");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Extract_WithInvalidDefsRoot_SkipsDefsFile()
    {
        var root = CreateTempRoot();
        try
        {
            var defsFile = Path.Combine(root, "Invalid.xml");
            File.WriteAllText(defsFile, "<LanguageData><A>B</A></LanguageData>");

            var sources = new XmlSourceCollection();
            sources.DefFiles.Add(new XmlSourceFile(defsFile, "Defs/Invalid.xml", "1.5", "Defs", 0));

            var engine = new DefFieldExtractionEngine();
            var result = engine.Extract(
                new ScanContext(root, "English", "English", [], "1.5"),
                sources,
                new Dictionary<string, HashSet<string>>());

            result.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Extract_DefsUsesReflectionMap_ForNonWhitelistedField()
    {
        var root = CreateTempRoot();
        try
        {
            var defsFile = Path.Combine(root, "ThingDefs.xml");
            File.WriteAllText(defsFile, """
                <Defs>
                  <ThingDef>
                    <defName>ReflectGun</defName>
                    <customLine>From Reflection</customLine>
                  </ThingDef>
                </Defs>
                """);

            var sources = new XmlSourceCollection();
            sources.DefFiles.Add(new XmlSourceFile(defsFile, "Defs/ThingDefs.xml", "1.5", "Defs", 0));

            var reflectionMap = new Dictionary<string, HashSet<string>>
            {
                ["ThingDef"] = new(StringComparer.OrdinalIgnoreCase) { "customLine" }
            };

            var engine = new DefFieldExtractionEngine();
            var result = engine.Extract(
                new ScanContext(root, "English", "English", [], "1.5"),
                sources,
                reflectionMap);

            result.Should().ContainSingle(x => x.Key == "ReflectGun.customLine" && x.OriginalText == "From Reflection");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rta_extract_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
