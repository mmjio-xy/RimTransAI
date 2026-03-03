using System.Diagnostics;
using FluentAssertions;
using RimTransAI.Services.Scanning;
using Xunit;
using Xunit.Abstractions;

namespace RimTransAI.Tests.Services.Scanning;

public class ScanningPerformanceSmokeTests
{
    private readonly ITestOutputHelper _output;

    public ScanningPerformanceSmokeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Scan_DenseDefsSample_CompletesWithinBaseline()
    {
        var root = CreateTempModRoot();
        try
        {
            var loadFolder = Path.Combine(root, "1.5");
            Directory.CreateDirectory(Path.Combine(loadFolder, "Defs"));

            var defsPath = Path.Combine(loadFolder, "Defs", "BulkDefs.xml");
            using (var writer = new StreamWriter(defsPath))
            {
                writer.WriteLine("<Defs>");
                for (var i = 0; i < 600; i++)
                {
                    writer.WriteLine($"  <ThingDef><defName>PerfThing_{i}</defName><label>Label {i}</label><description>Description {i}</description></ThingDef>");
                }

                writer.WriteLine("</Defs>");
            }

            var orchestrator = new ScanOrchestrator();
            var context = new ScanContext(
                root,
                "English",
                "English",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                "1.5");

            var sw = Stopwatch.StartNew();
            var result = orchestrator.Scan(context, new Dictionary<string, HashSet<string>>());
            sw.Stop();

            _output.WriteLine($"DenseDefs elapsed={sw.ElapsedMilliseconds}ms, items={result.Items.Count}");

            result.Items.Count.Should().BeGreaterThan(1000);
            sw.ElapsedMilliseconds.Should().BeLessThan(5000);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Scan_KeyedConflictSample_CompletesWithinBaseline()
    {
        var root = CreateTempModRoot();
        try
        {
            var loadFolder = Path.Combine(root, "1.5");
            Directory.CreateDirectory(Path.Combine(loadFolder, "Languages", "English", "Keyed"));

            for (var i = 0; i < 220; i++)
            {
                var filePath = Path.Combine(loadFolder, "Languages", "English", "Keyed", $"K{i:D3}.xml");
                File.WriteAllText(filePath, $"<LanguageData><Greeting>Hello {i}</Greeting><Key{i}>Value {i}</Key{i}></LanguageData>");
            }

            var orchestrator = new ScanOrchestrator();
            var context = new ScanContext(
                root,
                "English",
                "English",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                "1.5");

            var sw = Stopwatch.StartNew();
            var result = orchestrator.Scan(context, new Dictionary<string, HashSet<string>>());
            sw.Stop();

            _output.WriteLine($"KeyedConflict elapsed={sw.ElapsedMilliseconds}ms, items={result.Items.Count}, conflicts={result.Diagnostics.ExtractionConflictCount}");

            result.Items.Should().Contain(x => x.Key == "Greeting");
            result.Diagnostics.ExtractionConflictCount.Should().BeGreaterThan(100);
            sw.ElapsedMilliseconds.Should().BeLessThan(5000);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempModRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rta_perf_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
