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
        // 初始化日志服务
        Logger.Initialize();
        Logger.Info("开始初始化 Avalonia 应用程序");

        AvaloniaXamlLoader.Load(this);

        Logger.Info("Avalonia 应用程序初始化完成");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            Logger.Info("开始框架初始化");

            // 1. 配置依赖注入
            var collection = new ServiceCollection();

            // 注册 ViewModels
            collection.AddTransient<MainWindowViewModel>();
            collection.AddTransient<SettingsViewModel>();

            // 这里注册 Services
            collection.AddSingleton<ReflectionAnalyzer>();
            collection.AddSingleton<ModParserService>();
            collection.AddSingleton<LlmService>();
            collection.AddSingleton<FileGeneratorService>();
            // 注册配置服务
            collection.AddSingleton<ConfigService>();

            Services = collection.BuildServiceProvider();
            Logger.Info("依赖注入配置完成");

            // 2. 启动时应用保存的主题
            var configService = Services.GetRequiredService<ConfigService>();
            SetTheme(configService.CurrentConfig.AppTheme);
            Logger.Info($"应用主题: {configService.CurrentConfig.AppTheme}");

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
                Logger.Info("主窗口创建完成");
            }

            base.OnFrameworkInitializationCompleted();
            Logger.Info("框架初始化完成");
        }
        catch (Exception ex)
        {
            Logger.Error("框架初始化失败", ex);
            throw;
        }
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
        Current.RequestedThemeVariant = themeName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
    }
}