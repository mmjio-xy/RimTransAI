using FluentAssertions;
using RimTransAI.Services.Scanning;
using Xunit;

namespace RimTransAI.Tests.Services.Scanning;

public class XmlSourceCollectorTests
{
    [Fact]
    public void Collect_Stage0_ReturnsEmptySourceCollection()
    {
        var collector = new XmlSourceCollector();
        var context = new ScanContext(
            "C:\\Mod",
            "English",
            "English",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var sources = collector.Collect(context, [], [], new FileRegistry());

        sources.DefFiles.Should().BeEmpty();
        sources.KeyedFiles.Should().BeEmpty();
        sources.DefInjectedFiles.Should().BeEmpty();
    }
}
