using Microsoft.Extensions.DependencyInjection;
using RimTransAI.ViewModels;

namespace RimTransAI.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRimTransAIServices(this IServiceCollection services)
    {
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<BackupManagerViewModel>();

        services.AddSingleton<ReflectionAnalyzer>();
        services.AddSingleton<ModParserService>();
        services.AddSingleton<LlmService>();
        services.AddSingleton<FileGeneratorService>();
        services.AddSingleton<BatchingService>();
        services.AddSingleton<ConfigService>();
        services.AddSingleton<ModInfoService>();
        services.AddSingleton<IconCatalogService>();
        services.AddSingleton<WorkspaceService>();
        services.AddSingleton<BackupService>();
        services.AddSingleton<OperationLogBuffer>();

        return services;
    }
}
