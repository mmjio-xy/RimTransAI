using FluentAssertions;
using RimTransAI.Models;
using Xunit;

namespace RimTransAI.Tests.Models;

public class AppConfigTests
{
    [Fact]
    public void DefaultValues_ContainVersionAndOneDefaultApiProfile()
    {
        var config = new AppConfig();

        config.Version.Should().Be(AppConfig.CurrentProgramVersion);
        config.ConfigVersion.Should().Be(AppConfig.CurrentConfigVersion);
        config.TranslationApis.Should().ContainSingle();
        config.TranslationApis[0].Name.Should().Be("默认 API");
        config.TranslationApis[0].IsEnabled.Should().BeTrue();
        config.TranslationApis[0].ApiUrl.Should().Be("https://api.openai.com");
        config.TranslationApis[0].ApiKey.Should().BeEmpty();
        config.TranslationApis[0].Models.Should().ContainSingle();
        config.TranslationApis[0].Models[0].ModelId.Should().Be("gpt-3.5-turbo");
        config.TargetLanguage.Should().Be("ChineseSimplified");
        config.AppTheme.Should().Be("Light");
        config.AssemblyCSharpPath.Should().BeEmpty();
        config.DebugMode.Should().BeFalse();
    }

    [Fact]
    public void ProfileClone_CreatesIndependentModelCollection()
    {
        var profile = new TranslationApiProfile
        {
            Name = "Primary",
            ApiKey = "test-key",
            Models = [new TranslationApiModel { ModelId = "model-a" }]
        };

        var clone = profile.Clone();
        clone.Models[0].ModelId = "model-b";

        clone.Should().NotBeSameAs(profile);
        clone.Models.Should().NotBeSameAs(profile.Models);
        profile.Models[0].ModelId.Should().Be("model-a");
    }
}
