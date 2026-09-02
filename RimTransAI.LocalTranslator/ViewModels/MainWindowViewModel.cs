using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RimTransAI.LocalTranslator.Configuration;
using RimTransAI.LocalTranslator.Diagnostics;
using RimTransAI.LocalTranslator.Services;

namespace RimTransAI.LocalTranslator.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly TranslatorSettingsStore _settingsStore;
    private readonly LocalTranslationHost _host;
    private readonly ServiceHealth _health;

    [ObservableProperty] private string _modelPath = string.Empty;
    [ObservableProperty] private int _port;
    [ObservableProperty] private int _contextSize;
    [ObservableProperty] private int _maxTokensPerItem;
    [ObservableProperty] private int _maxItemsPerRequest;
    [ObservableProperty] private string _statusText = "服务未启动";
    [ObservableProperty] private string _statusDetail = "选择模型文件后启动服务。";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isServiceRunning;
    [ObservableProperty] private bool _isModelLoading;

    public MainWindowViewModel(TranslatorSettingsStore settingsStore, LocalTranslationHost host, ServiceHealth health)
    {
        _settingsStore = settingsStore;
        _host = host;
        _health = health;
        ApplySettings(_settingsStore.Load());
        UpdateHealth(_health.Current);
        _health.Changed += (_, snapshot) => Dispatcher.UIThread.Post(() => UpdateHealth(snapshot));
    }

    public string Endpoint => $"http://127.0.0.1:{Port}";

    public bool CanEditSettings => !IsServiceRunning && !IsBusy;

    public void SetModelPath(string path)
    {
        ModelPath = path;
    }

    partial void OnPortChanged(int value) => OnPropertyChanged(nameof(Endpoint));

    partial void OnIsServiceRunningChanged(bool value) => OnPropertyChanged(nameof(CanEditSettings));

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanEditSettings));

    [RelayCommand]
    private async Task StartServiceAsync()
    {
        if (IsBusy || IsServiceRunning) return;
        IsBusy = true;
        try
        {
            var options = CreateOptions();
            _settingsStore.Save(options);
            await _host.StartAsync(options);
        }
        catch (Exception exception)
        {
            StatusText = "启动失败";
            StatusDetail = exception.Message;
        }
        finally
        {
            IsBusy = false;
            IsServiceRunning = _host.IsRunning;
            OnPropertyChanged(nameof(CanEditSettings));
        }
    }

    [RelayCommand]
    private async Task StopServiceAsync()
    {
        if (IsBusy || !_host.IsRunning) return;
        IsBusy = true;
        try
        {
            await _host.StopAsync();
        }
        catch (Exception exception)
        {
            StatusText = "停止失败";
            StatusDetail = exception.Message;
        }
        finally
        {
            IsBusy = false;
            IsServiceRunning = _host.IsRunning;
            OnPropertyChanged(nameof(CanEditSettings));
        }
    }

    private TranslatorOptions CreateOptions() => new()
    {
        ModelPath = ModelPath.Trim(),
        Port = Port,
        ContextSize = ContextSize,
        MaxTokensPerItem = MaxTokensPerItem,
        MaxItemsPerRequest = MaxItemsPerRequest
    };

    private void ApplySettings(TranslatorOptions options)
    {
        ModelPath = options.ModelPath;
        Port = options.Port;
        ContextSize = options.ContextSize;
        MaxTokensPerItem = options.MaxTokensPerItem;
        MaxItemsPerRequest = options.MaxItemsPerRequest;
    }

    private void UpdateHealth(ServiceHealthSnapshot snapshot)
    {
        IsServiceRunning = _host.IsRunning;
        IsModelLoading = snapshot.Status == TranslatorServiceStatus.Loading;
        (StatusText, StatusDetail) = snapshot.Status switch
        {
            TranslatorServiceStatus.Loading => ("正在加载模型", snapshot.Detail ?? "请稍候。"),
            TranslatorServiceStatus.Ready => ("服务已就绪", $"接口正在监听 {Endpoint}"),
            TranslatorServiceStatus.Failed => ("模型加载失败", snapshot.Detail ?? "请检查模型文件和运行环境。"),
            _ => ("服务未启动", "选择模型文件后启动服务。")
        };
        OnPropertyChanged(nameof(CanEditSettings));
    }
}
