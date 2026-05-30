using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using RimTransAI.Models;

namespace RimTransAI.Views;

/// <summary>
/// 译文编辑弹窗，点击确认后将译文写回 TranslationItem。
/// </summary>
public partial class TranslationEditWindow : Window
{
    private readonly TranslationItem _item;

    public TranslationEditWindow()
    {
        InitializeComponent();
        _item = null!;
    }

    public TranslationEditWindow(TranslationItem item)
    {
        InitializeComponent();
        _item = item;

        // 填充内容
        KeyTextBlock.Text = item.Key;
        OriginalTextBlock.Text = item.OriginalText;
        TranslationTextBox.Text = item.TranslatedText;

        // 事件绑定
        CancelButton.Click += OnCancelClick;
        ConfirmButton.Click += OnConfirmClick;
    }

    /// <summary>
    /// 以模态方式显示编辑弹窗。
    /// </summary>
    public static async Task ShowEditDialog(Window owner, TranslationItem item)
    {
        var dialog = new TranslationEditWindow(item);
        await dialog.ShowDialog(owner);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        _item.TranslatedText = TranslationTextBox.Text ?? string.Empty;
        _item.Status = "已翻译";
        Close();
    }
}
