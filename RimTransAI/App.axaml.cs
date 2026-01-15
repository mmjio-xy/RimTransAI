using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using RimTransAI.Services;
using RimTransAI.ViewModels;
using RimTransAI.Views;

namespace RimTransAI;

public partial class App : Application
{
    public IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 1. 配置依赖注入
        var collection = new ServiceCollection();
        
        // 注册 ViewModels
        collection.AddTransient<MainWindowViewModel>();
        collection.AddTransient<SettingsViewModel>();
        
        // 将来在这里注册 Services
        collection.AddSingleton<ModParserService>();
        collection.AddSingleton<LlmService>();
        collection.AddSingleton<FileGeneratorService>();
        // 注册配置服务 
        collection.AddSingleton<ConfigService>();


        Services = collection.BuildServiceProvider();

        // 2. 启动时应用保存的主题
        var configService = Services.GetRequiredService<ConfigService>();
        SetTheme(configService.CurrentConfig.AppTheme);

        // 3. 启动主窗口
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 移除 Avalonia 自带的数据验证，避免重复验证
            // 抑制 IL2026 警告：这是 Avalonia 框架的已知行为，在运行时是安全的
            DisableAvaloniaDataValidation();

            var vm = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access",
        Justification = "Avalonia data validation removal is safe in this context")]
    private static void DisableAvaloniaDataValidation()
    {
        BindingPlugins.DataValidators.RemoveAt(0);
    }

    // === 静态切换主题方法 ===
    public static void SetTheme(string themeName)
    {
        if (Current is null) return;

        // 根据字符串切换 Avalonia 11 的 ThemeVariant
        if (themeName == "Dark")
        {
            Current.RequestedThemeVariant = ThemeVariant.Dark;
        }
        else
        {
            Current.RequestedThemeVariant = ThemeVariant.Light;
        }
    }
}