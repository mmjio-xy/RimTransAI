using Avalonia.Controls;
using RimTransAI.ViewModels;

namespace RimTransAI.Views;

public partial class ConfirmRestoreDialog : Window
{
    public ConfirmRestoreDialog()
    {
        InitializeComponent();
    }

    public ConfirmRestoreDialog(string modName, string version, string targetLanguage, string backupFileName, string backupDate, string backupSize)
    {
        InitializeComponent();
        var viewModel = new ConfirmRestoreDialogViewModel(modName, version, targetLanguage, backupFileName, backupDate, backupSize);
        viewModel.CurrentWindow = this;
        DataContext = viewModel;
    }
}
