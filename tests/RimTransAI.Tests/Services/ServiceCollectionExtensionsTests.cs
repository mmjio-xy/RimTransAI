using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RimTransAI.Services;
using RimTransAI.ViewModels;
using Xunit;

namespace RimTransAI.Tests.Services;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddRimTransAIServices_ResolvesAllLoggedServicesAndViewModels()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRimTransAIServices();
        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

        provider.GetRequiredService<ReflectionAnalyzer>().Should().NotBeNull();
        provider.GetRequiredService<ModParserService>().Should().NotBeNull();
        provider.GetRequiredService<LlmService>().Should().NotBeNull();
        provider.GetRequiredService<FileGeneratorService>().Should().NotBeNull();
        provider.GetRequiredService<BackupService>().Should().NotBeNull();
        provider.GetRequiredService<WorkspaceService>().Should().NotBeNull();
        provider.GetRequiredService<MainWindowViewModel>().Should().NotBeNull();
        provider.GetRequiredService<SettingsViewModel>().Should().NotBeNull();
        provider.GetRequiredService<BackupManagerViewModel>().Should().NotBeNull();
    }
}
