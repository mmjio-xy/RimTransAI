using FluentAssertions;
using RimTransAI.Services.Scanning;
using Xunit;

namespace RimTransAI.Tests.Services.Scanning;

public class XmlSourceCollectorTests
{
    [Fact]
    public void Collect_WithCodeLinkedAndDefLinked_PrefersLegacyFolders()
    {
        var root = CreateTempModRoot();
        try
        {
            var loadFolder = Path.Combine(root, "1.5");
            var langDir = Path.Combine(loadFolder, "Languages", "English");
            Directory.CreateDirectory(Path.Combine(loadFolder, "Defs"));
            Directory.CreateDirectory(Path.Combine(langDir, "CodeLinked"));
            Directory.CreateDirectory(Path.Combine(langDir, "Keyed"));
            Directory.CreateDirectory(Path.Combine(langDir, "DefLinked", "ThingDef"));
            Directory.CreateDirectory(Path.Combine(langDir, "DefInjected", "ThingDef"));
            Directory.CreateDirectory(Path.Combine(langDir, "Backstories"));
            Directory.CreateDirectory(Path.Combine(langDir, "Strings", "UI"));
            Directory.CreateDirectory(Path.Combine(langDir, "WordInfo", "Gender"));

            File.WriteAllText(Path.Combine(loadFolder, "Defs", "A.xml"), "<Defs />");
            File.WriteAllText(Path.Combine(langDir, "CodeLinked", "Code.xml"), "<LanguageData><A>1</A></LanguageData>");
            File.WriteAllText(Path.Combine(langDir, "Keyed", "Keyed.xml"), "<LanguageData><B>2</B></LanguageData>");
            File.WriteAllText(Path.Combine(langDir, "DefLinked", "ThingDef", "Old.xml"), "<LanguageData><A>B</A></LanguageData>");
            File.WriteAllText(Path.Combine(langDir, "DefInjected", "ThingDef", "New.xml"), "<LanguageData><A>B</A></LanguageData>");
            File.WriteAllText(Path.Combine(langDir, "Backstories", "Backstories.xml"), "<BackstoryTranslations />");
            File.WriteAllText(Path.Combine(langDir, "Strings", "UI", "menu.txt"), "line1");
            File.WriteAllText(Path.Combine(langDir, "WordInfo", "Gender", "Male.txt"), "king");

            var collector = new XmlSourceCollector();
            var context = new ScanContext(root, "English", "English", [], "1.5");
            var sources = collector.Collect(
                context,
                [new LoadFolderPlanEntry(loadFolder, "1.5", "1.5", 0)],
                [new LanguageDirectoryEntry(langDir, loadFolder, "1.5", 0)],
                new FileRegistry());

            sources.DefFiles.Should().HaveCount(1);
            sources.KeyedFiles.Should().ContainSingle(x => x.FullPath.EndsWith("Code.xml"));
            sources.KeyedFiles.Should().NotContain(x => x.FullPath.EndsWith("Keyed.xml"));
            sources.DefInjectedFiles.Should().ContainSingle(x => x.FullPath.EndsWith("Old.xml"));
            sources.DefInjectedFiles.Should().NotContain(x => x.FullPath.EndsWith("New.xml"));
            sources.BackstoryFiles.Should().ContainSingle(x => x.FullPath.EndsWith(Path.Combine("Backstories", "Backstories.xml")));
            sources.StringFiles.Should().ContainSingle();
            sources.WordInfoFiles.Should().ContainSingle();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Collect_KeepsDefsFromDifferentVersionsWhenRelativePathMatches()
    {
        var root = CreateTempModRoot();
        try
        {
            var v15 = Path.Combine(root, "1.5");
            var common = Path.Combine(root, "Common");
            Directory.CreateDirectory(Path.Combine(v15, "Defs", "Sub"));
            Directory.CreateDirectory(Path.Combine(common, "Defs", "Sub"));

            File.WriteAllText(Path.Combine(v15, "Defs", "Sub", "A.xml"), "<Defs />");
            File.WriteAllText(Path.Combine(common, "Defs", "Sub", "A.xml"), "<Defs />");

            var collector = new XmlSourceCollector();
            var context = new ScanContext(root, "English", "English", [], "1.5");
            var sources = collector.Collect(
                context,
                [
                    new LoadFolderPlanEntry(v15, "1.5", "1.5", 0),
                    new LoadFolderPlanEntry(common, "Common", "", 1)
                ],
                [],
                new FileRegistry());

            sources.DefFiles.Should().HaveCount(2);
            sources.DefFiles.Should().Contain(x => x.Version == "1.5" && x.RelativePath == "Defs/Sub/A.xml");
            sources.DefFiles.Should().Contain(x => x.Version == "" && x.RelativePath == "Defs/Sub/A.xml");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempModRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rta_src_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
