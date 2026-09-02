using System;
using Avalonia.Controls;
using RimTransAI.ViewModels;

namespace RimTransAI.Views;

public partial class TranslationApiProfileEditorWindow : Window
{
    public TranslationApiProfileEditorWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is TranslationApiProfileEditorViewModel viewModel)
        {
            viewModel.CurrentWindow = this;
        }
    }
}
