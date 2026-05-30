using System;
using System.Threading.Tasks;
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
    /// 自定义标题栏拖拽
    /// </summary>
    private void OnTitleBarPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    /// <summary>
    /// 点击译文列编辑按钮，弹出编辑窗口。
    /// </summary>
    private async void OnEditTranslationClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        if (button.Tag is not TranslationItem item)
            return;

        await TranslationEditWindow.ShowEditDialog(this, item);
    }

    /// <summary>
    /// 双击译文文本，弹出编辑窗口。
    /// </summary>
    private async void OnEditTranslationDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Grid grid)
            return;

        if (grid.Tag is not TranslationItem item)
            return;

        await TranslationEditWindow.ShowEditDialog(this, item);
    }
}
