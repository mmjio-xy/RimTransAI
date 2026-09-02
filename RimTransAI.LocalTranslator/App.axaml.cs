using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using RimTransAI.LocalTranslator.Configuration;
using RimTransAI.LocalTranslator.Diagnostics;
using RimTransAI.LocalTranslator.Services;
using RimTransAI.LocalTranslator.ViewModels;
using RimTransAI.LocalTranslator.Views;

namespace RimTransAI.LocalTranslator;

public partial class App : Application
{
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
    private readonly ServiceHealth _health = new();
    private TrayIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private LocalTranslationHost? _translatorHost;
    private NativeMenuItem? _startTrayItem;
    private NativeMenuItem? _stopTrayItem;
    private bool _isExiting;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _translatorHost = new LocalTranslationHost(_loggerFactory, _health);
            var viewModel = new MainWindowViewModel(
                new TranslatorSettingsStore(),
                _translatorHost,
                _health);
            _mainWindow = new MainWindow { DataContext = viewModel };
            desktop.MainWindow = _mainWindow;
            CreateTrayIcon();
            _health.Changed += (_, _) => Dispatcher.UIThread.Post(UpdateTrayMenu);
            desktop.Exit += async (_, _) =>
            {
                if (_translatorHost is not null) await _translatorHost.DisposeAsync();
                _trayIcon?.Dispose();
                _loggerFactory.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void CreateTrayIcon()
    {
        var menu = new NativeMenu();
        var showItem = new NativeMenuItem("显示窗口");
        showItem.Click += (_, _) => ShowMainWindow();
        _startTrayItem = new NativeMenuItem("启动服务");
        _startTrayItem.Click += async (_, _) => await StartServiceFromTrayAsync();
        _stopTrayItem = new NativeMenuItem("停止服务");
        _stopTrayItem.Click += async (_, _) => await StopServiceFromTrayAsync();
        var exitItem = new NativeMenuItem("退出");
        exitItem.Click += async (_, _) => await ExitApplicationAsync();
        menu.Add(showItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(_startTrayItem);
        menu.Add(_stopTrayItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(exitItem);

        using var iconStream = AssetLoader.Open(new Uri("avares://RimTransAI.LocalTranslator/Assets/RimTrans.ico"));
        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = "RimTransAI 本地翻译服务",
            Menu = menu,
            IsVisible = true
        };
        _trayIcon.Clicked += (_, _) => ShowMainWindow();
        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
        UpdateTrayMenu();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null) return;
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private async Task StartServiceFromTrayAsync()
    {
        if (_mainWindow?.DataContext is MainWindowViewModel viewModel)
            await viewModel.StartServiceCommand.ExecuteAsync(null);
    }

    private async Task StopServiceFromTrayAsync()
    {
        if (_mainWindow?.DataContext is MainWindowViewModel viewModel)
            await viewModel.StopServiceCommand.ExecuteAsync(null);
    }

    private void UpdateTrayMenu()
    {
        var isRunning = _translatorHost?.IsRunning == true;
        if (_startTrayItem is not null) _startTrayItem.IsEnabled = !isRunning;
        if (_stopTrayItem is not null) _stopTrayItem.IsEnabled = isRunning;
    }

    private async Task ExitApplicationAsync()
    {
        if (_isExiting) return;
        _isExiting = true;
        if (_translatorHost is not null) await _translatorHost.StopAsync();
        if (_trayIcon is not null) _trayIcon.IsVisible = false;
        if (_mainWindow is not null) _mainWindow.AllowClosing();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
    }
}
