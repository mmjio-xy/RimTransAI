using FluentAssertions;
using RimTransAI.Services;
using Xunit;

namespace RimTransAI.Tests.Services;

public class TokenEstimatorTests
{
    [Fact]
    public void EstimateTokens_EmptyString_ReturnsZero()
    {
        var result = TokenEstimator.EstimateTokens("");
        result.Should().Be(0);
    }

    [Fact]
    public void EstimateTokens_NullString_ReturnsZero()
    {
        var result = TokenEstimator.EstimateTokens(null!);
        result.Should().Be(0);
    }

    [Fact]
    public void EstimateTokens_EnglishText_ReturnsCorrectEstimate()
    {
        // 英文约 4 字符 = 1 token
        var result = TokenEstimator.EstimateTokens("Hello World"); // 11 字符
        result.Should().BeGreaterThan(0);
        result.Should().BeLessThanOrEqualTo(5); // 约 3 tokens
    }

    [Fact]
    public void EstimateTokens_ChineseText_ReturnsHigherEstimate()
    {
        // 中文约 1.5 token/字符
        var result = TokenEstimator.EstimateTokens("你好世界"); // 4 个中文字符
        result.Should().BeGreaterOrEqualTo(4); // 至少 4 * 1.5 = 6 tokens
    }

    [Fact]
    public void EstimateTokens_MixedText_HandlesCorrectly()
    {
        var result = TokenEstimator.EstimateTokens("Hello 世界");
        result.Should().BeGreaterThan(0);
    }
}
