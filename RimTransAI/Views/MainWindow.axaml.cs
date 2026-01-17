using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RimTransAI.ViewModels;

namespace RimTransAI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 点击遮罩层关闭 Mod 信息面板
    /// </summary>
    private void OnOverlayPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CloseModInfoPanelCommand.Execute(null);
        }
    }
}