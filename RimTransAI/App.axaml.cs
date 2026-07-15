using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RimTransAI.Services;
using RimTransAI.ViewModels;
using RimTransAI.Views;

namespace RimTransAI;

public partial class App : Application
{
    private static bool _globalExceptionHandlersRegistered;

    public IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        // 初始化日志服务
        Logger.Initialize();
        RegisterGlobalExceptionHandlers();
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

            collection.AddLogging(builder =>
            {
                builder.ClearProviders();
            });
            collection.AddSingleton<ILoggerProvider>(
                _ => new Serilog.Extensions.Logging.SerilogLoggerProvider(
                    Serilog.Log.Logger,
                    dispose: false));

            // 注册 ViewModels
            collection.AddTransient<MainWindowViewModel>();
            collection.AddTransient<SettingsViewModel>();
            collection.AddTransient<BackupManagerViewModel>();

            // 这里注册 Services
            collection.AddSingleton<ReflectionAnalyzer>();
            collection.AddSingleton<ModParserService>();
            collection.AddSingleton<LlmService>();
            collection.AddSingleton<FileGeneratorService>();
            collection.AddSingleton<BatchingService>(); // 智能分批服务
            // 注册配置服务
            collection.AddSingleton<ConfigService>();
            // 注册 Mod 信息服务
            collection.AddSingleton<ModInfoService>();
            collection.AddSingleton<IconCatalogService>();
            collection.AddSingleton<WorkspaceService>();
            // 注册备份服务
            collection.AddSingleton<BackupService>();

            Services = collection.BuildServiceProvider();
            Logger.Info("依赖注入配置完成");

            // 2. 启动时应用保存的主题
            var configService = Services.GetRequiredService<ConfigService>();
            Logger.SetApiKey(configService.CurrentConfig.ApiKey);
            SetTheme(configService.CurrentConfig.AppTheme);
            Logger.Info($"应用主题: {configService.CurrentConfig.AppTheme}");

            // 3. 应用调试模式设置
            Logger.SetDebugMode(configService.CurrentConfig.DebugMode);
            Logger.Info($"调试模式: {(configService.CurrentConfig.DebugMode ? "已启用" : "已禁用")}");
            Logger.Debug($"API 配置 — URL: {configService.CurrentConfig.ApiUrl} | 模型: {configService.CurrentConfig.TargetModel} | 超时: {configService.CurrentConfig.ApiRequestTimeoutSeconds}s");

            // 4. 启动主窗口
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var vm = Services.GetRequiredService<MainWindowViewModel>();
                desktop.MainWindow = new MainWindow
                {
                    DataContext = vm
                };
                desktop.Exit += (_, _) => Logger.Shutdown();
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

    private static void RegisterGlobalExceptionHandlers()
    {
        if (_globalExceptionHandlersRegistered)
            return;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                Logger.Error("发生未处理的应用程序异常", exception);
            }
            else
            {
                Logger.Error($"发生未处理的应用程序异常: {args.ExceptionObject}");
            }

            if (args.IsTerminating)
            {
                Logger.Shutdown();
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Error("发生未观察到的任务异常", args.Exception);
            args.SetObserved();
        };

        _globalExceptionHandlersRegistered = true;
    }

    // === 静态切换主题方法 ===
    public static void SetTheme(string themeName)
    {
        if (Current is null) return;

        // 根据字符串切换 Avalonia 11 的 ThemeVariant
        Current.RequestedThemeVariant = themeName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
    }
}
