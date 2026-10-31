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
    public void CreateDefault_KeywordTagDoesNotBlacklistStagesFields()
    {
        var rules = FieldExtractionRuleSet.CreateDefault();

        // "tag" 与 "stages" 只是子串关系，整词匹配后不应再误杀这些字段
        rules.IsBlacklisted("stages").Should().BeFalse();
        rules.IsBlacklisted("lifeStages").Should().BeFalse();
        rules.IsBlacklisted("scoreStages").Should().BeFalse();
    }

    [Fact]
    public void CreateDefault_TechnicalKeywordsStillMatchWholeWords()
    {
        var rules = FieldExtractionRuleSet.CreateDefault();

        rules.IsBlacklisted("tags").Should().BeTrue();
        rules.IsBlacklisted("tradeTags").Should().BeTrue();
        rules.IsBlacklisted("texPath").Should().BeTrue();
        rules.IsBlacklisted("soundCast").Should().BeTrue();
        rules.IsBlacklisted("workerClass").Should().BeTrue();
        rules.IsBlacklisted("graphicData").Should().BeTrue();
    }

    [Fact]
    public void CreateDefault_PathLikeContentIsBlocked()
    {
        var rules = FieldExtractionRuleSet.CreateDefault();

        rules.IsPathLikeContent("Textures/UI/Icon.png").Should().BeTrue();
        rules.IsPathLikeContent("Human readable text").Should().BeFalse();
        rules.IsPathLikeContent("More of an output port than input/output.").Should().BeFalse();
    }
}
