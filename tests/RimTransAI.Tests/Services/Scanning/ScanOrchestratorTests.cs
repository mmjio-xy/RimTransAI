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
            result.Diagnostics.SourceFileAttemptCount.Should().Be(2);
            result.Diagnostics.SourceFileRegisteredCount.Should().Be(2);
            result.Diagnostics.SourceFileDeduplicatedCount.Should().Be(0);
            result.Diagnostics.ExtractedItemCount.Should().Be(result.Items.Count);
            result.Diagnostics.ExtractionConflictCount.Should().Be(0);
            result.Diagnostics.ExtractionErrorCount.Should().Be(0);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Scan_WhenKeyedKeyConflict_ReportsConflictCount()
    {
        var root = CreateTempModRoot();
        try
        {
            var loadFolder = Path.Combine(root, "1.5");
            Directory.CreateDirectory(Path.Combine(loadFolder, "Languages", "English", "Keyed"));
            File.WriteAllText(Path.Combine(loadFolder, "Languages", "English", "Keyed", "A.xml"),
                "<LanguageData><Greeting>Hello</Greeting></LanguageData>");
            File.WriteAllText(Path.Combine(loadFolder, "Languages", "English", "Keyed", "B.xml"),
                "<LanguageData><Greeting>Hi</Greeting></LanguageData>");

            var orchestrator = new ScanOrchestrator();
            var context = new ScanContext(
                root,
                "English",
                "English",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                "1.5");

            var result = orchestrator.Scan(context, new Dictionary<string, HashSet<string>>());

            result.Diagnostics.ExtractionConflictCount.Should().BeGreaterThan(0);
            result.Items.Should().ContainSingle(x => x.Key == "Greeting" && x.OriginalText == "Hi");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Scan_SameInputMultipleRuns_IsDeterministic()
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
                    <defName>StableItem</defName>
                    <label>Stable Label</label>
                    <rulesStrings>
                      <li>Rule A</li>
                      <li>Rule B</li>
                    </rulesStrings>
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

            var signatures = new List<string[]>();
            for (var i = 0; i < 3; i++)
            {
                var run = orchestrator.Scan(context, new Dictionary<string, HashSet<string>>());
                signatures.Add(run.Items.Select(x => $"{x.DefType}|{x.Key}|{x.OriginalText}").OrderBy(x => x).ToArray());
            }

            signatures[0].Should().Equal(signatures[1]);
            signatures[1].Should().Equal(signatures[2]);
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
