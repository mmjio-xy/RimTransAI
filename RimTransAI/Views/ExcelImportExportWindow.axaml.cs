using System;
using Avalonia.Controls;
using RimTransAI.ViewModels;

namespace RimTransAI.Views;

public partial class ExcelImportExportWindow : Window
{
    public ExcelImportExportWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ExcelImportExportViewModel viewModel)
        {
            viewModel.CurrentWindow = this;
        }
    }
}
