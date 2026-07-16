using FluentAssertions;
using Microsoft.Extensions.Logging;
using RimTransAI.Models;
using RimTransAI.Services;
using RimTransAI.Tests.Helpers;
using Xunit;

namespace RimTransAI.Tests.Services;

public class BackupServiceTests : IDisposable
{
    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), $"rta_backup_{Guid.NewGuid():N}");

    public BackupServiceTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void RestoreFromBackup_WhenMetadataEntryIsMissing_LogsWarning()
    {
        var configService = new ConfigService(
            filePath: Path.Combine(_tempDirectory, "settings.json"));
        configService.SaveConfig(new AppConfig
        {
            BackupDirectory = _tempDirectory,
            EnableAutoBackup = true
        }).Should().BeTrue();
        var logger = new RecordingLogger<BackupService>();
        var service = new BackupService(configService, logger);

        var result = service.RestoreFromBackup(
            _tempDirectory,
            "missing.package",
            "1.5");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("未找到匹配");
        logger.Records.Should().Contain(record =>
            record.Level == LogLevel.Warning &&
            record.Message.Contains("未找到匹配记录", StringComparison.Ordinal));
    }

    [Fact]
    public void BackupTranslationFolder_WhenMetadataCannotBeSaved_RollsBackArchive()
    {
        var backupDirectory = Path.Combine(_tempDirectory, "backups");
        var modRoot = Path.Combine(_tempDirectory, "mod");
        Directory.CreateDirectory(backupDirectory);
        Directory.CreateDirectory(Path.Combine(backupDirectory, "backup_metadata.json"));
        var languageDirectory = Path.Combine(
            modRoot,
            "1.5",
            "Languages",
            "ChineseSimplified");
        Directory.CreateDirectory(languageDirectory);
        File.WriteAllText(Path.Combine(languageDirectory, "Main.xml"), "<LanguageData />");
        var configService = new ConfigService(
            filePath: Path.Combine(_tempDirectory, "settings-metadata.json"));
        configService.SaveConfig(new AppConfig
        {
            BackupDirectory = backupDirectory,
            EnableAutoBackup = true
        }).Should().BeTrue();
        var logger = new RecordingLogger<BackupService>();
        var service = new BackupService(configService, logger);

        var result = service.BackupTranslationFolder(
            modRoot,
            "Test Mod",
            "test.package",
            "1.5",
            "ChineseSimplified");

        result.Should().BeNull();
        Directory.EnumerateFiles(backupDirectory, "*.zip").Should().BeEmpty();
        logger.Records.Should().Contain(record =>
            record.Level == LogLevel.Warning &&
            record.Message.Contains("元数据保存失败", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
