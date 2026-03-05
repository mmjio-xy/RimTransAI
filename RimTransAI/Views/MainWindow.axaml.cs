using System;
using Avalonia.Controls;
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
        if (e.Column.Header?.ToString() == "译文" && e.EditAction == DataGridEditAction.Commit)
        {
            // 获取当前编辑的行数据
            if (e.Row.DataContext is TranslationItem item)
            {
                // 自动更新状态为"已翻译"
                item.Status = "已翻译";
            }
        }
    }
}
