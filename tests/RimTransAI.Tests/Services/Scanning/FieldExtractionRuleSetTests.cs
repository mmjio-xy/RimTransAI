using FluentAssertions;
using RimTransAI.Services.Scanning;
using Xunit;

namespace RimTransAI.Tests.Services.Scanning;

public class FieldExtractionRuleSetTests
{
    [Fact]
    public void CreateDefault_BlacklistContainsDefName()
    {
        var rules = FieldExtractionRuleSet.CreateDefault();

        rules.IsBlacklisted("defName").Should().BeTrue();
    }

    [Fact]
    public void CreateDefault_SmartSuffixMatchesCustomTextField()
    {
        var rules = FieldExtractionRuleSet.CreateDefault();

        rules.IsSmartSuffixMatch("successText").Should().BeTrue();
    }

    [Fact]
    public void CreateDefault_PathLikeContentIsBlocked()
    {
        var rules = FieldExtractionRuleSet.CreateDefault();

        rules.IsPathLikeContent("Textures/UI/Icon.png").Should().BeTrue();
        rules.IsPathLikeContent("Human readable text").Should().BeFalse();
    }
}
