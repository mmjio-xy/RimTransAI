using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RimTransAI.Models;
using RimTransAI.ViewModels;

namespace RimTransAI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 处理单元格编辑完成事件
    /// </summary>
    private void OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        // 仅处理译文列的编辑
        if (e.Column.Header?.ToString() == "译文 (预览)" && e.EditAction == DataGridEditAction.Commit)
        {
            // 获取当前编辑的行数据
            if (e.Row.DataContext is TranslationItem item)
            {
                // 自动更新状态为"已翻译"
                item.Status = "已翻译";
            }
        }
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