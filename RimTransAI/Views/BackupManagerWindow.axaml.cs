using Avalonia.Controls;
using RimTransAI.ViewModels;

namespace RimTransAI.Views;

public partial class BackupManagerWindow : Window
{
    public BackupManagerWindow()
    {
        InitializeComponent();
        SetupDataGridRowLoading();
    }

    public BackupManagerWindow(ViewModels.BackupManagerViewModel viewModel)
    {
        InitializeComponent();
        viewModel.CurrentWindow = this;
        DataContext = viewModel;
        SetupDataGridRowLoading();
    }

    private void SetupDataGridRowLoading()
    {
        var dataGrid = this.FindControl<DataGrid>("BackupGrid");
        if (dataGrid != null)
        {
            dataGrid.LoadingRow += DataGrid_LoadingRow;
        }
    }

    private void DataGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.DataContext is BackupInfoViewModel backup)
        {
            if (!backup.FileExists)
            {
                e.Row.Classes.Add("missing");
            }
            else
            {
                e.Row.Classes.Remove("missing");
            }
        }
    }
}
