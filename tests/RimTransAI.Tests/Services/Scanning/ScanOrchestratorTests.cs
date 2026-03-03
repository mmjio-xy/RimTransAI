using FluentAssertions;
using RimTransAI.Services.Scanning;
using Xunit;

namespace RimTransAI.Tests.Services.Scanning;

public class ScanOrchestratorTests
{
    [Fact]
    public void Scan_EndToEnd_CollectsAndExtractsDefsAndKeyed()
    {
        var root = CreateTempModRoot();
        try
        {
            var loadFolder = Path.Combine(root, "1.5");
            Directory.CreateDirectory(Path.Combine(loadFolder, "Defs"));
            Directory.CreateDirectory(Path.Combine(loadFolder, "Languages", "English", "Keyed"));

            File.WriteAllText(Path.Combine(loadFolder, "Defs", "ThingDefs.xml"), """
                <Defs>
                  <ThingDef>
                    <defName>TestItem</defName>
                    <label>Test Label</label>
                  </ThingDef>
                </Defs>
                """);
            File.WriteAllText(Path.Combine(loadFolder, "Languages", "English", "Keyed", "Main.xml"),
                "<LanguageData><Greeting>Hello</Greeting></LanguageData>");

            var orchestrator = new ScanOrchestrator();
            var context = new ScanContext(
                root,
                "English",
                "English",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                "1.5");

            var result = orchestrator.Scan(context, new Dictionary<string, HashSet<string>>());

            result.Sources.DefFiles.Should().ContainSingle();
            result.Sources.KeyedFiles.Should().ContainSingle();
            result.Items.Should().Contain(x => x.Key == "TestItem.label" && x.DefType == "ThingDef");
            result.Items.Should().Contain(x => x.Key == "Greeting" && x.DefType == "Keyed");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempModRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rta_orch_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
