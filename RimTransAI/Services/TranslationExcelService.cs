using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using MiniExcelLibs;
using RimTransAI.Models;

namespace RimTransAI.Services;

public sealed record TranslationExcelRow(
    string InternalId,
    string Version,
    string Key,
    string OriginalText,
    string TranslatedText);

public sealed record TranslationExcelUpdate(
    TranslationItem Item,
    string TranslatedText);

public sealed class TranslationExcelImportPreview
{
    public IReadOnlyList<TranslationExcelRow> Rows { get; init; } = [];
    public IReadOnlyList<TranslationExcelUpdate> MatchedItems { get; init; } = [];
    public IReadOnlyList<TranslationExcelUpdate> Updates { get; init; } = [];
    public IReadOnlyList<TranslationItem> DeletedItems { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public int UnchangedCount { get; init; }
    public bool CanApply => Errors.Count == 0;
}

public sealed class TranslationExcelService
{
    public const string SheetName = "Translations";
    public const string InternalIdColumn = "_RowId";
    public const string VersionColumn = "版本";
    public const string KeyColumn = "Key";
    public const string OriginalTextColumn = "原文";
    public const string TranslatedTextColumn = "译文";

    private static readonly string[] RequiredColumns =
        [InternalIdColumn, VersionColumn, KeyColumn, OriginalTextColumn, TranslatedTextColumn];

    public async Task ExportAsync(
        string filePath,
        IEnumerable<TranslationItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(items);

        var table = CreateExportTable(items.Where(item => !item.IsExcluded));
        var isEmpty = table.Rows.Count == 0;
        if (isEmpty)
        {
            table.Rows.Add(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        }
        await MiniExcel.SaveAsAsync(
            filePath,
            table,
            printHeader: true,
            sheetName: SheetName,
            excelType: ExcelType.XLSX,
            configuration: null,
            overwriteFile: true,
            cancellationToken: cancellationToken);
        if (isEmpty)
        {
            RemoveDataRows(filePath);
        }
        HideInternalIdColumn(filePath);
    }

    public async Task<TranslationExcelImportPreview> CreateImportPreviewAsync(
        string filePath,
        IReadOnlyList<TranslationItem> currentScopeItems,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(currentScopeItems);

        if (!File.Exists(filePath))
        {
            return ErrorPreview("Excel 文件不存在。");
        }

        try
        {
            var columns = MiniExcel.GetColumns(
                filePath,
                useHeaderRow: true,
                sheetName: SheetName,
                excelType: ExcelType.XLSX,
                startCell: "A1",
                configuration: null);
            var missingColumns = RequiredColumns
                .Where(required => !columns.Contains(required, StringComparer.Ordinal))
                .ToArray();
            if (missingColumns.Length > 0)
            {
                return ErrorPreview($"表格缺少必要列：{string.Join("、", missingColumns)}。");
            }

            IEnumerable<object> importedRows;
            try
            {
                var query = await MiniExcel.QueryAsync(
                    filePath,
                    useHeaderRow: true,
                    sheetName: SheetName,
                    excelType: ExcelType.XLSX,
                    startCell: "A1",
                    configuration: null,
                    cancellationToken: cancellationToken);
                importedRows = query?.ToList() ?? [];
            }
            catch (ArgumentNullException ex) when (ex.ParamName == "source")
            {
                // MiniExcel 对仅保留表头的工作表返回空源；这是“删除全部行”的有效输入。
                importedRows = [];
            }
            return BuildPreview(importedRows, currentScopeItems);
        }
        catch (ArgumentNullException ex) when (
            ex.ParamName == "source" && IsHeaderOnlyWorksheet(filePath))
        {
            return BuildPreview([], currentScopeItems);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            return ErrorPreview($"Excel 文件读取失败：{ex.Message}");
        }
    }

    public void ApplyImport(TranslationExcelImportPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (!preview.CanApply)
        {
            throw new InvalidOperationException("导入预览包含错误，不能应用。");
        }

        foreach (var update in preview.MatchedItems)
        {
            update.Item.IsExcluded = false;
            update.Item.TranslatedText = update.TranslatedText;
            update.Item.Status = string.IsNullOrWhiteSpace(update.TranslatedText)
                ? "未翻译"
                : "已翻译";
        }

        foreach (var item in preview.DeletedItems)
        {
            item.IsExcluded = true;
        }
    }

    private static DataTable CreateExportTable(IEnumerable<TranslationItem> items)
    {
        var table = new DataTable(SheetName);
        foreach (var column in RequiredColumns)
        {
            table.Columns.Add(column, typeof(string));
        }

        foreach (var item in items)
        {
            table.Rows.Add(
                CreateRowId(item),
                item.Version ?? string.Empty,
                item.Key ?? string.Empty,
                item.OriginalText ?? string.Empty,
                item.TranslatedText ?? string.Empty);
        }

        return table;
    }

    private static TranslationExcelImportPreview BuildPreview(
        IEnumerable<object> importedRows,
        IReadOnlyList<TranslationItem> currentScopeItems)
    {
        var errors = new List<string>();
        var rows = importedRows
            .Select(ReadRow)
            .ToList();

        var currentGroups = currentScopeItems
            .GroupBy(CreateRowId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList());
        var importedGroups = rows.GroupBy(row => row.InternalId, StringComparer.Ordinal).ToList();

        var duplicateCurrent = currentGroups.Where(pair => pair.Value.Count > 1).ToList();
        if (duplicateCurrent.Count > 0)
        {
            errors.Add($"当前 Mod 表格中存在 {duplicateCurrent.Count} 组重复的内部行标识，无法可靠同步。");
        }

        var duplicateImported = importedGroups.Where(group => group.Count() > 1).ToList();
        if (duplicateImported.Count > 0)
        {
            errors.Add($"导入表格中存在 {duplicateImported.Count} 组重复的内部行标识。");
        }

        var unknownRows = importedGroups
            .Where(group => !currentGroups.ContainsKey(group.Key))
            .SelectMany(group => group)
            .ToList();
        if (unknownRows.Count > 0)
        {
            errors.Add($"有 {unknownRows.Count} 行与当前 Mod 或当前版本过滤结果不一致。");
        }

        var modifiedConstraints = importedGroups
            .Where(group => currentGroups.TryGetValue(group.Key, out var items) && items.Count == 1)
            .Select(group => new { Row = group.First(), Item = currentGroups[group.Key].Single() })
            .Where(entry =>
                !string.Equals(entry.Row.Version, entry.Item.Version, StringComparison.Ordinal) ||
                !string.Equals(entry.Row.Key, entry.Item.Key, StringComparison.Ordinal) ||
                !string.Equals(entry.Row.OriginalText, entry.Item.OriginalText, StringComparison.Ordinal))
            .ToList();
        if (modifiedConstraints.Count > 0)
        {
            errors.Add($"有 {modifiedConstraints.Count} 行修改了版本、Key 或原文，无法导入。");
        }

        if (errors.Count > 0)
        {
            return new TranslationExcelImportPreview
            {
                Rows = rows,
                Errors = errors
            };
        }

        var updates = new List<TranslationExcelUpdate>();
        var matchedItems = new List<TranslationExcelUpdate>();
        var importedIds = new HashSet<string>(StringComparer.Ordinal);
        var unchangedCount = 0;
        foreach (var group in importedGroups)
        {
            var row = group.Single();
            var item = currentGroups[group.Key].Single();
            importedIds.Add(group.Key);
            var matchedItem = new TranslationExcelUpdate(item, row.TranslatedText);
            matchedItems.Add(matchedItem);
            if (string.Equals(item.TranslatedText, row.TranslatedText, StringComparison.Ordinal))
            {
                unchangedCount++;
            }
            else
            {
                updates.Add(matchedItem);
            }
        }

        var deletedItems = currentGroups
            .Where(pair => !importedIds.Contains(pair.Key))
            .SelectMany(pair => pair.Value)
            .Where(item => !item.IsExcluded)
            .ToList();

        return new TranslationExcelImportPreview
        {
            Rows = rows,
            MatchedItems = matchedItems,
            Updates = updates,
            DeletedItems = deletedItems,
            UnchangedCount = unchangedCount
        };
    }

    private static TranslationExcelRow ReadRow(object row)
    {
        if (row is not IDictionary<string, object> values)
        {
            throw new InvalidDataException("Excel 行格式无法识别。");
        }

        return new TranslationExcelRow(
            CellText(values, InternalIdColumn),
            CellText(values, VersionColumn),
            CellText(values, KeyColumn),
            CellText(values, OriginalTextColumn),
            CellText(values, TranslatedTextColumn));
    }

    private static string CellText(IDictionary<string, object> row, string columnName) =>
        !row.TryGetValue(columnName, out var value) || value == null
            ? string.Empty
            : Convert.ToString(value) ?? string.Empty;

    public static string CreateRowId(TranslationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var value = string.Join(
            '\n',
            item.Version ?? string.Empty,
            item.Key ?? string.Empty,
            item.OriginalText ?? string.Empty,
            NormalizePath(item.FilePath),
            item.DefType ?? string.Empty,
            item.ExtractionReasonCode ?? string.Empty,
            item.ExtractionSourceContext ?? string.Empty,
            item.IsListReplacement.ToString());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static TranslationExcelImportPreview ErrorPreview(string error) =>
        new() { Errors = [error] };

    private static bool IsHeaderOnlyWorksheet(string filePath)
    {
        var sheetNames = MiniExcel.GetSheetNames(filePath);
        var sheetIndex = sheetNames.FindIndex(name =>
            string.Equals(name, SheetName, StringComparison.Ordinal));
        if (sheetIndex < 0)
            return false;

        var dimensions = MiniExcel.GetSheetDimensions(filePath);
        return sheetIndex < dimensions.Count &&
               dimensions[sheetIndex].Rows.Count == 1 &&
               dimensions[sheetIndex].Columns.Count == RequiredColumns.Length;
    }

    private static string NormalizePath(string? path) =>
        (path ?? string.Empty)
        .Replace('\\', '/')
        .Trim()
        .ToUpperInvariant();

    private static void HideInternalIdColumn(string filePath)
    {
        using var archive = ZipFile.Open(filePath, ZipArchiveMode.Update);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml")
                    ?? throw new InvalidDataException("Excel 工作表结构无效。");
        XDocument document;
        using (var input = entry.Open())
        {
            document = XDocument.Load(input);
        }

        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheet = document.Root ?? throw new InvalidDataException("Excel 工作表为空。");
        var columns = new XElement(
            spreadsheet + "cols",
            new XElement(
                spreadsheet + "col",
                new XAttribute("min", 1),
                new XAttribute("max", 1),
                new XAttribute("width", 2),
                new XAttribute("hidden", 1),
                new XAttribute("customWidth", 1)));
        var sheetData = worksheet.Element(spreadsheet + "sheetData")
                        ?? throw new InvalidDataException("Excel 工作表缺少数据区域。");
        sheetData.AddBeforeSelf(columns);

        entry.Delete();
        var replacement = archive.CreateEntry("xl/worksheets/sheet1.xml");
        using var output = replacement.Open();
        document.Save(output);
    }

    private static void RemoveDataRows(string filePath)
    {
        using var archive = ZipFile.Open(filePath, ZipArchiveMode.Update);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml")
                    ?? throw new InvalidDataException("Excel 工作表结构无效。");
        XDocument document;
        using (var input = entry.Open())
        {
            document = XDocument.Load(input);
        }

        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        document.Descendants(spreadsheet + "row")
            .Where(row => (int?)row.Attribute("r") > 1)
            .Remove();
        document.Descendants(spreadsheet + "dimension")
            .Single()
            .SetAttributeValue("ref", "A1:E1");

        entry.Delete();
        var replacement = archive.CreateEntry("xl/worksheets/sheet1.xml");
        using var output = replacement.Open();
        document.Save(output);
    }
}
