using FluentAssertions;
using RimTransAI.Services.Scanning;
using Xunit;

namespace RimTransAI.Tests.Services.Scanning;

public class ScanOrchestratorTests
{
    [Fact]
    public void Scan_Stage0_ReturnsEmptyResult()
    {
        var orchestrator = new ScanOrchestrator();
        var context = new ScanContext(
            "C:\\Mod",
            "English",
            "English",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var result = orchestrator.Scan(context, new Dictionary<string, HashSet<string>>());

        result.Items.Should().BeEmpty();
        result.Sources.DefFiles.Should().BeEmpty();
        result.Sources.KeyedFiles.Should().BeEmpty();
        result.Sources.DefInjectedFiles.Should().BeEmpty();
    }
}
