using System;
using Avalonia.Controls;
using RimTransAI.ViewModels;

namespace RimTransAI.Views;

public partial class ModSourceEditorWindow : Window
{
    public ModSourceEditorWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ModSourceEditorViewModel vm)
        {
            vm.CurrentWindow = this;
        }
    }
}
