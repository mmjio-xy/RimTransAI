using FluentAssertions;
using RimTransAI.Services.Scanning;
using Xunit;

namespace RimTransAI.Tests.Services.Scanning;

public class DefPathBuilderTests
{
    [Fact]
    public void BuildKey_WithPathSegments_ReturnsCanonicalPath()
    {
        var builder = new DefPathBuilder();

        var key = builder.BuildKey("TestGun", ["rulesStrings", "0"]);

        key.Should().Be("TestGun.rulesStrings.0");
    }

    [Fact]
    public void NormalizeKey_WithLegacyBracketSyntax_ConvertsToDotPath()
    {
        var builder = new DefPathBuilder();

        var key = builder.NormalizeKey(" TestGun.rulesStrings[0] ");

        key.Should().Be("TestGun.rulesStrings.0");
    }
}
