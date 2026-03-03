using FluentAssertions;
using RimTransAI.Services.Scanning;
using Xunit;

namespace RimTransAI.Tests.Services.Scanning;

public class DefFieldExtractionEngineTests
{
    [Fact]
    public void Extract_Stage0_ReturnsEmptyItems()
    {
        var engine = new DefFieldExtractionEngine();
        var context = new ScanContext(
            "C:\\Mod",
            "English",
            "English",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var items = engine.Extract(context, new XmlSourceCollection(), new Dictionary<string, HashSet<string>>());

        items.Should().BeEmpty();
    }
}
