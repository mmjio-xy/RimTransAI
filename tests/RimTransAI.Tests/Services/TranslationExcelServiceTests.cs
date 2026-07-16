using System.Data;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using MiniExcelLibs;
using RimTransAI.Models;
using RimTransAI.Services;
using Xunit;

namespace RimTransAI.Tests.Services;

public sealed class TranslationExcelServiceTests : IDisposable
{
    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), $"rta_excel_{Guid.NewGuid():N}");
    private readonly TranslationExcelService _service = new();

    public TranslationExcelServiceTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task ExportAsync_WritesOnlyVisibleTableColumnsAndRows()
    {
        var path = Path.Combine(_tempDirectory, "export.xlsx");
        var items = new[]
        {
            CreateItem("1.5", "A.label", "Alpha", "甲"),
            CreateItem("1.6", "B.label", "Beta", "乙")
        };

        await _service.ExportAsync(path, items);

        MiniExcel.GetColumns(path, useHeaderRow: true, sheetName: TranslationExcelService.SheetName)
            .Should().Equal("_RowId", "版本", "Key", "原文", "译文");
        IsInternalIdColumnHidden(path).Should().BeTrue();
        MiniExcel.Query(path, useHeaderRow: true, sheetName: TranslationExcelService.SheetName)
            .Should().HaveCount(2);
    }

    [Fact]
    public async Task ExportAsync_WhenTableIsEmpty_PreservesFourVisibleHeaders()
    {
        var path = Path.Combine(_tempDirectory, "empty-export.xlsx");
        var currentItems = new[]
        {
            CreateItem("1.5", "A.label", "Alpha", "甲"),
            CreateItem("1.5", "B.label", "Beta", "乙")
        };

        await _service.ExportAsync(path, []);
        var preview = await _service.CreateImportPreviewAsync(path, currentItems);

        IsInternalIdColumnHidden(path).Should().BeTrue();
        preview.CanApply.Should().BeTrue();
        preview.Rows.Should().BeEmpty();
        preview.DeletedItems.Should().BeEquivalentTo(currentItems);
    }

    [Fact]
    public async Task ImportPreview_WhenRowChangesAndAnotherIsDeleted_AppliesBothChanges()
    {
        var path = Path.Combine(_tempDirectory, "import.xlsx");
        var first = CreateItem("1.5", "A.label", "Alpha", "旧译文");
        var deleted = CreateItem("1.5", "B.label", "Beta", "乙");
        WriteWorkbook(path, [CreateExcelRow(first, "新译文")]);

        var preview = await _service.CreateImportPreviewAsync(path, [first, deleted]);

        preview.Errors.Should().BeEmpty();
        preview.CanApply.Should().BeTrue();
        preview.Updates.Should().ContainSingle();
        preview.DeletedItems.Should().ContainSingle().Which.Should().BeSameAs(deleted);

        _service.ApplyImport(preview);

        first.TranslatedText.Should().Be("新译文");
        first.Status.Should().Be("已翻译");
        first.IsExcluded.Should().BeFalse();
        deleted.IsExcluded.Should().BeTrue();
    }

    [Fact]
    public async Task ImportPreview_WhenTranslationIsBlank_SetsStatusToUntranslated()
    {
        var path = Path.Combine(_tempDirectory, "blank.xlsx");
        var item = CreateItem("1.5", "A.label", "Alpha", "旧译文");
        WriteWorkbook(path, [CreateExcelRow(item, string.Empty)]);

        var preview = await _service.CreateImportPreviewAsync(path, [item]);
        preview.Errors.Should().BeEmpty();
        _service.ApplyImport(preview);

        item.TranslatedText.Should().BeEmpty();
        item.Status.Should().Be("未翻译");
    }

    [Fact]
    public async Task ImportPreview_WhenRowsDoNotMatchCurrentScope_BlocksConfirmation()
    {
        var path = Path.Combine(_tempDirectory, "wrong-mod.xlsx");
        WriteWorkbook(path, [new TranslationExcelRow("UNKNOWN", "1.6", "Other.label", "Other", "其他")]);

        var preview = await _service.CreateImportPreviewAsync(
            path,
            [CreateItem("1.5", "A.label", "Alpha", "甲")]);

        preview.CanApply.Should().BeFalse();
        preview.Errors.Should().ContainSingle(message => message.Contains("不一致", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportPreview_WhenConstraintColumnsAreEdited_BlocksConfirmation()
    {
        var path = Path.Combine(_tempDirectory, "edited-key.xlsx");
        var item = CreateItem("1.5", "A.label", "Alpha", "甲");
        WriteWorkbook(
            path,
            [new TranslationExcelRow(
                TranslationExcelService.CreateRowId(item),
                item.Version,
                "Changed.label",
                item.OriginalText,
                item.TranslatedText)]);

        var preview = await _service.CreateImportPreviewAsync(path, [item]);

        preview.CanApply.Should().BeFalse();
        preview.Errors.Should().ContainSingle(message => message.Contains("修改", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImportPreview_WhenAllRowsAreDeleted_MarksEntireScopeAsDeleted()
    {
        var path = Path.Combine(_tempDirectory, "delete-all.xlsx");
        var items = new[]
        {
            CreateItem("1.5", "A.label", "Alpha", "甲"),
            CreateItem("1.5", "B.label", "Beta", "乙")
        };
        WriteWorkbook(path, [CreateExcelRow(items[0], "甲")]);
        RemoveDataRows(path);

        var preview = await _service.CreateImportPreviewAsync(path, items);

        preview.Errors.Should().BeEmpty();
        preview.CanApply.Should().BeTrue();
        preview.Rows.Should().BeEmpty();
        preview.DeletedItems.Should().BeEquivalentTo(items);
    }

    [Fact]
    public async Task ImportPreview_WhenPreviouslyDeletedRowReturns_RestoresItOnConfirmation()
    {
        var path = Path.Combine(_tempDirectory, "restore-row.xlsx");
        var item = CreateItem("1.5", "A.label", "Alpha", "甲");
        item.IsExcluded = true;
        item.Status = "未翻译";
        WriteWorkbook(path, [CreateExcelRow(item, "甲")]);

        var preview = await _service.CreateImportPreviewAsync(path, [item]);
        _service.ApplyImport(preview);

        preview.Updates.Should().BeEmpty();
        item.IsExcluded.Should().BeFalse();
        item.Status.Should().Be("已翻译");
    }

    private static TranslationItem CreateItem(
        string version,
        string key,
        string originalText,
        string translatedText) =>
        new()
        {
            Version = version,
            Key = key,
            OriginalText = originalText,
            TranslatedText = translatedText,
            Status = string.IsNullOrWhiteSpace(translatedText) ? "未翻译" : "已翻译"
        };

    private static TranslationExcelRow CreateExcelRow(
        TranslationItem item,
        string translatedText) =>
        new(
            TranslationExcelService.CreateRowId(item),
            item.Version,
            item.Key,
            item.OriginalText,
            translatedText);

    private static void WriteWorkbook(string path, IEnumerable<TranslationExcelRow> rows)
    {
        var table = new DataTable(TranslationExcelService.SheetName);
        table.Columns.Add(TranslationExcelService.InternalIdColumn, typeof(string));
        table.Columns.Add(TranslationExcelService.VersionColumn, typeof(string));
        table.Columns.Add(TranslationExcelService.KeyColumn, typeof(string));
        table.Columns.Add(TranslationExcelService.OriginalTextColumn, typeof(string));
        table.Columns.Add(TranslationExcelService.TranslatedTextColumn, typeof(string));
        foreach (var row in rows)
        {
            table.Rows.Add(row.InternalId, row.Version, row.Key, row.OriginalText, row.TranslatedText);
        }

        MiniExcel.SaveAs(
            path,
            table,
            printHeader: true,
            sheetName: TranslationExcelService.SheetName,
            excelType: ExcelType.XLSX,
            overwriteFile: true);
    }

    private static void RemoveDataRows(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
        XDocument document;
        using (var stream = entry.Open())
        {
            document = XDocument.Load(stream);
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

    private static bool IsInternalIdColumnHidden(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
        using var input = entry.Open();
        var document = XDocument.Load(input);
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return document
            .Descendants(spreadsheet + "col")
            .Any(column =>
                (int?)column.Attribute("min") == 1 &&
                (int?)column.Attribute("max") == 1 &&
                (int?)column.Attribute("hidden") == 1);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
