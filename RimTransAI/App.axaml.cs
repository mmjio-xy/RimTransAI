using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using RimTransAI.ViewModels;
using RimTransAI.Views;
using System;
using RimTransAI.Services;

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
        collection.AddSingleton<ModParserService>();
        collection.AddSingleton<LlmService>();
        collection.AddSingleton<FileGeneratorService>();
        
        // 将来在这里注册 Services
        // collection.AddSingleton<IModParserService, ModParserService>();
        // collection.AddSingleton<ILlmService, LlmService>();

        Services = collection.BuildServiceProvider();

        // 2. 启动主窗口
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 移除 Avalonia 自带的数据验证，避免重复验证
            BindingPlugins.DataValidators.RemoveAt(0);

            var vm = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}