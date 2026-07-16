using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RimTransAI.Models;
using RimTransAI.Services;

namespace RimTransAI.ViewModels;

public partial class ExcelImportExportViewModel : ViewModelBase
{
    private static readonly FilePickerFileType ExcelFileType = new("Excel 工作簿")
    {
        Patterns = ["*.xlsx"],
        MimeTypes = ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"]
    };

    private readonly TranslationExcelService _excelService;
    private readonly IReadOnlyList<TranslationItem> _exportItems;
    private readonly IReadOnlyList<TranslationItem> _constraintItems;
    private readonly ILogger<ExcelImportExportViewModel> _logger;
    private readonly string _suggestedFileName;

    [ObservableProperty] private string _resultMessage = "可导出当前表格，或选择 XLSX 文件预览后导入。";
    [ObservableProperty] private string _selectedFileName = "尚未选择导入文件";
    [ObservableProperty] private bool _canConfirmImport;
    [ObservableProperty] private bool _isBusy;

    public ExcelImportExportViewModel()
        : this(
            new TranslationExcelService(),
            [],
            [],
            "翻译表格",
            "全部",
            NullLogger<ExcelImportExportViewModel>.Instance)
    {
    }

    public ExcelImportExportViewModel(
        TranslationExcelService excelService,
        IReadOnlyList<TranslationItem> exportItems,
        IReadOnlyList<TranslationItem> constraintItems,
        string modName,
        string versionFilter,
        ILogger<ExcelImportExportViewModel>? logger = null)
    {
        _excelService = excelService;
        _exportItems = exportItems;
        _constraintItems = constraintItems;
        _logger = logger ?? NullLogger<ExcelImportExportViewModel>.Instance;
        ScopeDescription = $"当前表格：{modName} / 版本过滤：{versionFilter} / {_exportItems.Count} 行";
        _suggestedFileName = BuildSuggestedFileName(modName, versionFilter);
    }

    public Window? CurrentWindow { get; set; }
    public string ScopeDescription { get; }
    public ObservableCollection<TranslationExcelRow> PreviewRows { get; } = [];
    public TranslationExcelImportPreview? PreparedImport { get; private set; }

    [RelayCommand]
    private async Task ExportExcel()
    {
        if (CurrentWindow == null || IsBusy)
            return;

        var file = await CurrentWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出翻译表格",
            SuggestedFileName = _suggestedFileName,
            DefaultExtension = "xlsx",
            FileTypeChoices = [ExcelFileType],
            ShowOverwritePrompt = true
        });
        if (file == null)
            return;

        var path = file.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            IsBusy = true;
            await _excelService.ExportAsync(path, _exportItems);
            ResultMessage = $"已导出 {_exportItems.Count} 行：{Path.GetFileName(path)}";
            _logger.LogUserSuccess("Excel 翻译表格已导出，共 {RowCount} 行", _exportItems.Count);
        }
        catch (Exception ex)
        {
            ResultMessage = $"导出失败：{ex.Message}";
            _logger.LogUserError(ex, "导出 Excel 翻译表格失败：{ErrorMessage}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SelectImportExcel()
    {
        if (CurrentWindow == null || IsBusy)
            return;

        var files = await CurrentWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择要导入的翻译表格",
            AllowMultiple = false,
            FileTypeFilter = [ExcelFileType]
        });
        if (files.Count == 0)
            return;

        var path = files[0].Path.LocalPath;
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            IsBusy = true;
            CanConfirmImport = false;
            PreparedImport = null;
            PreviewRows.Clear();
            SelectedFileName = Path.GetFileName(path);

            var preview = await _excelService.CreateImportPreviewAsync(path, _constraintItems);
            PreparedImport = preview;
            foreach (var row in preview.Rows)
            {
                PreviewRows.Add(row);
            }

            if (!preview.CanApply)
            {
                ResultMessage = string.Join(Environment.NewLine, preview.Errors);
                return;
            }

            CanConfirmImport = true;
            ResultMessage =
                $"预览完成：更新 {preview.Updates.Count} 行，删除 {preview.DeletedItems.Count} 行，未变化 {preview.UnchangedCount} 行。";
        }
        catch (Exception ex)
        {
            ResultMessage = $"导入预览失败：{ex.Message}";
            _logger.LogUserError(ex, "读取 Excel 翻译表格失败：{ErrorMessage}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ConfirmImport()
    {
        if (!CanConfirmImport || PreparedImport is not { CanApply: true })
            return;

        CurrentWindow?.Close(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        CurrentWindow?.Close(false);
    }

    private static string BuildSuggestedFileName(string modName, string versionFilter)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeModName = new string(modName.Select(character =>
            invalidChars.Contains(character) ? '_' : character).ToArray());
        var safeVersion = new string(versionFilter.Select(character =>
            invalidChars.Contains(character) ? '_' : character).ToArray());
        return $"{safeModName}_{safeVersion}_翻译.xlsx";
    }
}
