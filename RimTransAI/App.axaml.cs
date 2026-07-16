using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RimTransAI.Services;
using RimTransAI.ViewModels;
using RimTransAI.Views;

namespace RimTransAI;

public partial class App : Application
{
    private static bool _globalExceptionHandlersRegistered;
    private ILogger<App>? _logger;

    public IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        // 初始化日志服务
        LoggingBootstrap.Initialize();
        Avalonia.Logging.Logger.Sink = new AvaloniaSerilogSink();
        RegisterGlobalExceptionHandlers();
        var startupLogger = Serilog.Log.ForContext<App>();
        startupLogger.Information("开始初始化 Avalonia 应用程序");

        AvaloniaXamlLoader.Load(this);

        startupLogger.Information("Avalonia 应用程序初始化完成");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            Serilog.Log.ForContext<App>().Information("开始框架初始化");

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

            collection.AddRimTransAIServices();

            Services = collection.BuildServiceProvider();
            _logger = Services.GetRequiredService<ILogger<App>>();
            var operationLogBuffer = Services.GetRequiredService<OperationLogBuffer>();
            LoggingBootstrap.AttachOperationLogBuffer(operationLogBuffer, DispatchToUiThread);
            _logger.LogInformation("依赖注入配置完成");

            // 2. 启动时应用保存的主题
            var configService = Services.GetRequiredService<ConfigService>();
            LoggingBootstrap.SetApiKey(configService.CurrentConfig.ApiKey);
            SetTheme(configService.CurrentConfig.AppTheme);
            _logger.LogInformation("应用主题 Theme={Theme}", configService.CurrentConfig.AppTheme);

            // 3. 应用调试模式设置
            LoggingBootstrap.SetDebugMode(configService.CurrentConfig.DebugMode);
            _logger.LogInformation("调试模式 DebugEnabled={DebugEnabled}", configService.CurrentConfig.DebugMode);
            _logger.LogDebug(
                "API 配置 ApiUrl={ApiUrl} Model={Model} TimeoutSeconds={TimeoutSeconds}",
                configService.CurrentConfig.ApiUrl,
                configService.CurrentConfig.TargetModel,
                configService.CurrentConfig.ApiRequestTimeoutSeconds);

            // 4. 启动主窗口
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var vm = Services.GetRequiredService<MainWindowViewModel>();
                desktop.MainWindow = new MainWindow
                {
                    DataContext = vm
                };
                desktop.Exit += (_, _) => LoggingBootstrap.Shutdown();
                _logger.LogInformation("主窗口创建完成");
            }

            base.OnFrameworkInitializationCompleted();
            _logger.LogInformation("框架初始化完成");
        }
        catch (Exception ex)
        {
            if (_logger != null)
            {
                _logger.LogError(ex, "框架初始化失败");
            }
            else
            {
                Serilog.Log.ForContext<App>().Error(ex, "框架初始化失败");
            }
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
                Serilog.Log.ForContext<App>().Fatal(exception, "发生未处理的应用程序异常");
            }
            else
            {
                Serilog.Log.ForContext<App>().Fatal(
                    "发生未处理的应用程序异常 ExceptionObject={ExceptionObject}",
                    args.ExceptionObject);
            }

            if (args.IsTerminating)
            {
                LoggingBootstrap.Shutdown();
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Serilog.Log.ForContext<App>().Error(args.Exception, "发生未观察到的任务异常");
            args.SetObserved();
        };

        _globalExceptionHandlersRegistered = true;
    }

    private static void DispatchToUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }

    // === 静态切换主题方法 ===
    public static void SetTheme(string themeName)
    {
        if (Current is null) return;

        // 根据字符串切换 Avalonia 11 的 ThemeVariant
        Current.RequestedThemeVariant = themeName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
    }
}
