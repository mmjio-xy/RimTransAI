using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RimTransAI.LocalTranslator.ViewModels;

namespace RimTransAI.LocalTranslator.Views;

public partial class MainWindow : Window
{
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        Closing += (_, eventArgs) =>
        {
            if (_allowClose) return;
            eventArgs.Cancel = true;
            Hide();
        };
    }

    public void AllowClosing() => _allowClose = true;

    private async void CopyEndpoint_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel && Clipboard is not null)
            await Clipboard.SetTextAsync(viewModel.Endpoint);
    }

    private async void BrowseModel_Click(object? sender, RoutedEventArgs eventArgs)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 GGUF 翻译模型",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("GGUF 模型") { Patterns = ["*.gguf"] }]
        });
        if (files.Count > 0 && DataContext is MainWindowViewModel viewModel)
            viewModel.SetModelPath(files[0].Path.LocalPath);
    }
}
