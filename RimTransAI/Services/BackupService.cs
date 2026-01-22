using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RimTransAI.Models;

namespace RimTransAI.Services;

public class BackupService
{
    private readonly ConfigService _configService;
    private const string MetadataFileName = "backup_metadata.json";

    public BackupService(ConfigService configService)
    {
        _configService = configService;
    }

    /// <summary>
    /// 获取元数据文件路径
    /// </summary>
    private string GetMetadataFilePath()
    {
        return Path.Combine(GetBackupDirectory(), MetadataFileName);
    }

    /// <summary>
    /// 读取元数据文件
    /// </summary>
    public BackupMetadataFile LoadMetadata()
    {
        var metadataPath = GetMetadataFilePath();
        if (!File.Exists(metadataPath))
        {
            return new BackupMetadataFile();
        }

        try
        {
            var json = File.ReadAllText(metadataPath, Encoding.UTF8);
            return JsonSerializer.Deserialize(json, AppJsonContext.Default.BackupMetadataFile) ?? new BackupMetadataFile();
        }
        catch (Exception ex)
        {
            Logger.Warning($"读取元数据文件失败: {ex.Message}");
            return new BackupMetadataFile();
        }
    }

    /// <summary>
    /// 保存元数据文件
    /// </summary>
    private void SaveMetadata(BackupMetadataFile metadata)
    {
        var metadataPath = GetMetadataFilePath();
        metadata.LastUpdated = DateTime.Now;

        try
        {
            var json = JsonSerializer.Serialize(metadata, AppJsonContext.Default.BackupMetadataFile);
            File.WriteAllText(metadataPath, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Logger.Error($"保存元数据文件失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 添加备份条目到元数据
    /// </summary>
    private void AddMetadataEntry(BackupMetadataEntry entry)
    {
        var metadata = LoadMetadata();
        metadata.Entries.Add(entry);
        SaveMetadata(metadata);
    }

    /// <summary>
    /// 从元数据中移除条目
    /// </summary>
    private void RemoveMetadataEntry(string fileName)
    {
        var metadata = LoadMetadata();
        metadata.Entries.RemoveAll(e => e.FileName == fileName);
        SaveMetadata(metadata);
    }

    /// <summary>
    /// 获取备份存储目录
    /// </summary>
    public string GetBackupDirectory()
    {
        var config = _configService.CurrentConfig;

        if (!string.IsNullOrWhiteSpace(config.BackupDirectory) && Directory.Exists(config.BackupDirectory))
        {
            return config.BackupDirectory;
        }

        // 默认路径: %AppData%/RimTransAI/Backups
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var defaultBackupDir = Path.Combine(appDataPath, "RimTransAI", "Backups");

        if (!Directory.Exists(defaultBackupDir))
        {
            Directory.CreateDirectory(defaultBackupDir);
        }

        return defaultBackupDir;
    }

    /// <summary>
    /// 备份翻译文件夹
    /// </summary>
    /// <param name="modRootPath">Mod 根目录</param>
    /// <param name="modName">Mod 名称</param>
    /// <param name="packageId">Mod 的 PackageId</param>
    /// <param name="version">版本（如 1.5、1.4、Common、空字符串表示根目录）</param>
    /// <param name="targetLang">目标语言（如 ChineseSimplified）</param>
    /// <returns>备份文件路径，如果不需要备份则返回 null</returns>
    public string? BackupTranslationFolder(string modRootPath, string modName, string packageId, string version, string targetLang)
    {
        var config = _configService.CurrentConfig;

        // 检查是否启用自动备份
        if (!config.EnableAutoBackup)
        {
            Logger.Info("自动备份已禁用，跳过备份");
            return null;
        }

        // 1. 确定翻译文件夹路径
        string versionPath = string.IsNullOrEmpty(version) ? modRootPath : Path.Combine(modRootPath, version);
        string languagesDir = Path.Combine(versionPath, "Languages");

        if (!Directory.Exists(languagesDir))
        {
            Logger.Info($"翻译文件夹不存在，跳过备份: {languagesDir}");
            return null;
        }

        // 2. 不区分大小写查找目标语言文件夹
        string? targetLangFolder = Directory.EnumerateDirectories(languagesDir)
            .FirstOrDefault(dir => string.Equals(
                Path.GetFileName(dir),
                targetLang,
                StringComparison.OrdinalIgnoreCase
            ));

        if (targetLangFolder == null)
        {
            Logger.Info($"目标语言文件夹不存在，跳过备份: {targetLang}");
            return null;
        }

        // 3. 生成备份文件名
        string sanitizedPackageId = packageId.Replace(".", "_");
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string versionDisplay = string.IsNullOrEmpty(version) ? "Root" : version;
        string zipFileName = $"{sanitizedPackageId}_{versionDisplay}_{timestamp}.zip";

        // 4. 确定备份存储路径
        string backupDir = GetBackupDirectory();
        string zipPath = Path.Combine(backupDir, zipFileName);

        // 5. 创建压缩
        try
        {
            var compressionLevel = (CompressionLevel)config.BackupCompressionLevel;

            ZipFile.CreateFromDirectory(
                sourceDirectoryName: targetLangFolder,
                destinationArchiveFileName: zipPath,
                compressionLevel: compressionLevel,
                includeBaseDirectory: true,  // 包含语言文件夹本身
                entryNameEncoding: System.Text.Encoding.UTF8
            );

            // 计算文件哈希
            string fileHash = ComputeFileHash(zipPath);
            var fileInfo = new FileInfo(zipPath);

            // 计算恢复相对路径（从 Mod 根目录起）
            string restoreRelativePath = string.IsNullOrEmpty(version)
                ? "Languages"
                : Path.Combine(version, "Languages");

            // 写入元数据文件
            var metadataEntry = new BackupMetadataEntry
            {
                FileName = zipFileName,
                FileHash = fileHash,
                FileSizeBytes = fileInfo.Length,
                ModName = modName,
                PackageId = packageId,
                ModVersion = version,
                ModVersionDisplay = versionDisplay,
                TargetLanguage = targetLang,
                RestoreRelativePath = restoreRelativePath,
                CreatedAt = DateTime.Now,
                OriginalModPath = modRootPath
            };
            AddMetadataEntry(metadataEntry);

            long fileSizeKb = fileInfo.Length / 1024;
            Logger.Info($"备份已创建: {zipFileName} ({fileSizeKb} KB)");

            // 6. 清理旧备份
            CleanupOldBackups(sanitizedPackageId, versionDisplay);

            return zipPath;
        }
        catch (Exception ex)
        {
            Logger.Error($"备份失败: {ex.Message}", ex);
            return null;
        }
    }

    /// <summary>
    /// 检查是否存在指定版本的备份
    /// </summary>
    /// <param name="packageId">Mod 的 PackageId</param>
    /// <param name="version">版本</param>
    /// <returns>最新备份文件的完整路径，如果不存在则返回 null</returns>
    public string? CheckBackupExists(string packageId, string version)
    {
        string sanitizedPackageId = packageId.Replace(".", "_");
        string versionDisplay = string.IsNullOrEmpty(version) ? "Root" : version;

        string backupDir = GetBackupDirectory();

        if (!Directory.Exists(backupDir))
        {
            return null;
        }

        // 查找匹配的备份文件（按文件名匹配）
        // 转义正则特殊字符
        string escapedPackageId = Regex.Escape(sanitizedPackageId);
        string escapedVersion = Regex.Escape(versionDisplay);
        string pattern = $"^{escapedPackageId}_{escapedVersion}_.*\\.zip$";
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);

        var backupFiles = Directory.GetFiles(backupDir, "*.zip")
            .Where(file => regex.IsMatch(Path.GetFileName(file)))
            .OrderByDescending(file => File.GetCreationTime(file))
            .ToList();

        return backupFiles.FirstOrDefault();
    }

    /// <summary>
    /// 获取所有备份文件信息
    /// </summary>
    /// <param name="packageId">Mod 的 PackageId（可选，如果不指定则返回所有备份）</param>
    /// <returns>备份文件信息列表</returns>
    public List<BackupInfo> GetAllBackups(string? packageId = null)
    {
        string backupDir = GetBackupDirectory();
        var backups = new List<BackupInfo>();

        // 从元数据文件读取
        var metadata = LoadMetadata();

        foreach (var entry in metadata.Entries)
        {
            // 如果指定了 packageId，则过滤
            if (!string.IsNullOrEmpty(packageId) &&
                !string.Equals(entry.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string filePath = Path.Combine(backupDir, entry.FileName);
            bool fileExists = File.Exists(filePath);

            var backupInfo = new BackupInfo
            {
                FilePath = filePath,
                ModName = entry.ModName,
                PackageId = entry.PackageId,
                Hash = entry.FileHash,
                Version = entry.ModVersion,
                VersionDisplay = entry.ModVersionDisplay,
                CreationTime = entry.CreatedAt,
                FileSizeBytes = entry.FileSizeBytes,
                FileSizeKb = entry.FileSizeBytes / 1024,
                FileSizeMb = entry.FileSizeBytes / (1024.0 * 1024.0),
                RestoreRelativePath = entry.RestoreRelativePath,
                TargetLanguage = entry.TargetLanguage,
                FileExists = fileExists
            };

            backups.Add(backupInfo);
        }

        // 按创建时间降序排序
        return backups.OrderByDescending(b => b.CreationTime).ToList();
    }

    /// <summary>
    /// 恢复翻译文件夹
    /// </summary>
    /// <param name="modRootPath">Mod 根目录</param>
    /// <param name="version">版本</param>
    /// <param name="targetLang">目标语言</param>
    /// <returns>是否成功恢复</returns>
    public bool RestoreTranslationFolder(string modRootPath, string version, string targetLang)
    {
        return false;
    }

    /// <summary>
    /// 恢复翻译文件夹（带 PackageId）- 旧方法保留兼容
    /// </summary>
    public bool RestoreTranslationFolder(string modRootPath, string packageId, string version, string targetLang)
    {
        var result = RestoreFromBackup(modRootPath, packageId, version);
        return result.Success;
    }

    /// <summary>
    /// 从备份恢复（新方法，返回详细结果）
    /// </summary>
    public RestoreResult RestoreFromBackup(string modRootPath, string packageId, string version)
    {
        var result = new RestoreResult();

        // 1. 从元数据查找备份
        var metadata = LoadMetadata();
        var entry = metadata.Entries
            .Where(e => string.Equals(e.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
            .Where(e => string.Equals(e.ModVersion, version, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefault();

        if (entry == null)
        {
            result.ErrorMessage = "未找到匹配的备份记录";
            return result;
        }

        string backupDir = GetBackupDirectory();
        string backupPath = Path.Combine(backupDir, entry.FileName);

        // 2. 检查备份文件是否存在
        if (!File.Exists(backupPath))
        {
            result.ErrorMessage = $"备份文件不存在: {entry.FileName}";
            return result;
        }

        // 3. 校验哈希
        try
        {
            string computedHash = ComputeFileHash(backupPath);
            if (!string.Equals(computedHash, entry.FileHash, StringComparison.OrdinalIgnoreCase))
            {
                result.ErrorMessage = $"备份文件哈希校验失败，文件可能已损坏";
                return result;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"哈希校验异常: {ex.Message}");
        }

        // 4. 确定目标路径
        string languagesDir = Path.Combine(modRootPath, entry.RestoreRelativePath);
        string targetLangPath = Path.Combine(languagesDir, entry.TargetLanguage);

        // 5. 删除现有文件夹
        if (Directory.Exists(targetLangPath))
        {
            try
            {
                Directory.Delete(targetLangPath, recursive: true);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"删除现有文件夹失败: {ex.Message}";
                return result;
            }
        }

        // 6. 创建目录
        if (!Directory.Exists(languagesDir))
        {
            Directory.CreateDirectory(languagesDir);
        }

        // 7. 解压
        try
        {
            ZipFile.ExtractToDirectory(backupPath, languagesDir, Encoding.UTF8, true);
            result.Success = true;
            result.RestoredPath = targetLangPath;
            Logger.Info($"恢复成功: {entry.FileName} -> {targetLangPath}");
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"解压失败: {ex.Message}";
            Logger.Error($"恢复失败: {ex.Message}", ex);
        }

        return result;
    }

    /// <summary>
    /// 删除指定的备份文件
    /// </summary>
    /// <param name="backupPath">备份文件路径</param>
    /// <returns>是否成功删除</returns>
    public bool DeleteBackup(string backupPath)
    {
        try
        {
            string fileName = Path.GetFileName(backupPath);

            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
                Logger.Info($"已删除备份: {fileName}");
            }

            // 同步更新元数据
            RemoveMetadataEntry(fileName);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"删除备份失败: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// 清理旧备份
    /// </summary>
    private void CleanupOldBackups(string sanitizedPackageId, string versionDisplay)
    {
        var config = _configService.CurrentConfig;

        if (config.MaxBackupCount <= 0)
        {
            return;  // 不限制数量
        }

        string backupDir = GetBackupDirectory();

        if (!Directory.Exists(backupDir))
        {
            return;
        }

        // 查找匹配的备份文件
        // 转义正则特殊字符
        string escapedPackageId = Regex.Escape(sanitizedPackageId);
        string escapedVersion = Regex.Escape(versionDisplay);
        string pattern = $"^{escapedPackageId}_{escapedVersion}_.*\\.zip$";
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);

        var backupFiles = Directory.GetFiles(backupDir, "*.zip")
            .Where(file => regex.IsMatch(Path.GetFileName(file)))
            .OrderByDescending(file => File.GetCreationTime(file))
            .ToList();

        // 如果超过最大数量，删除最旧的备份
        if (backupFiles.Count > config.MaxBackupCount)
        {
            var filesToDelete = backupFiles.Skip(config.MaxBackupCount).ToList();

            foreach (var file in filesToDelete)
            {
                try
                {
                    File.Delete(file);
                    Logger.Info($"已删除旧备份: {Path.GetFileName(file)}");
                }
                catch (Exception ex)
                {
                    Logger.Warning($"删除旧备份失败 {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// 计算文件的 SHA-256 哈希值
    /// </summary>
    private string ComputeFileHash(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha256.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    /// <summary>
    /// 从 ZIP 注释中解析备份信息
    /// </summary>
    /// <param name="zipPath">ZIP 文件路径</param>
    /// <returns>解析出的备份信息，如果失败则返回 null</returns>
    private BackupInfo? ParseBackupFromComment(string zipPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            if (string.IsNullOrEmpty(archive.Comment))
            {
                return null;
            }

            // 解析注释格式：ModName: xxx | PackageId: xxx | Version: xxx | Created: xxx | Hash: xxx
            var parts = archive.Comment.Split('|').Select(p => p.Trim()).ToList();
            var info = new BackupInfo
            {
                FilePath = zipPath,
                FileSizeBytes = new FileInfo(zipPath).Length
            };

            foreach (var part in parts)
            {
                if (part.StartsWith("ModName:", StringComparison.OrdinalIgnoreCase))
                {
                    info.ModName = part.Substring("ModName:".Length).Trim();
                }
                else if (part.StartsWith("PackageId:", StringComparison.OrdinalIgnoreCase))
                {
                    info.PackageId = part.Substring("PackageId:".Length).Trim();
                }
                else if (part.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
                {
                    info.VersionDisplay = part.Substring("Version:".Length).Trim();
                    info.Version = info.VersionDisplay == "Root" ? "" : info.VersionDisplay;
                }
                else if (part.StartsWith("Created:", StringComparison.OrdinalIgnoreCase))
                {
                    if (DateTime.TryParse(part.Substring("Created:".Length).Trim(), out var createdTime))
                    {
                        info.CreationTime = createdTime;
                    }
                }
                else if (part.StartsWith("Hash:", StringComparison.OrdinalIgnoreCase))
                {
                    info.Hash = part.Substring("Hash:".Length).Trim();
                }
            }

            // 如果无法从注释解析，返回 null（将使用文件名作为备用）
            return info;
        }
        catch (Exception ex)
        {
            Logger.Warning($"解析 ZIP 注释失败: {zipPath} - {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// 备份文件信息
/// </summary>
public class BackupInfo
{
    /// <summary>
    /// 备份文件完整路径
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Mod 名称
    /// </summary>
    public string ModName { get; set; } = string.Empty;

    /// <summary>
    /// Mod 的 PackageId
    /// </summary>
    public string PackageId { get; set; } = string.Empty;

    /// <summary>
    /// 备份文件的 SHA-256 哈希值
    /// </summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// 版本（1.5、1.4、Common、空字符串表示根目录）
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// 版本显示名称（Root、1.5、1.4、Common）
    /// </summary>
    public string VersionDisplay { get; set; } = string.Empty;

    /// <summary>
    /// 时间戳字符串（yyyyMMdd_HHmmss）
    /// </summary>
    public string TimestampStr { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// 文件大小（字节）
    /// </summary>
    public long FileSizeBytes { get; set; }

    /// <summary>
    /// 文件大小（KB）
    /// </summary>
    public long FileSizeKb { get; set; }

    /// <summary>
    /// 文件大小（MB）
    /// </summary>
    public double FileSizeMb { get; set; }

    /// <summary>
    /// 恢复相对路径（从 Mod 根目录起）
    /// </summary>
    public string RestoreRelativePath { get; set; } = string.Empty;

    /// <summary>
    /// 目标语言
    /// </summary>
    public string TargetLanguage { get; set; } = string.Empty;

    /// <summary>
    /// 备份文件是否存在
    /// </summary>
    public bool FileExists { get; set; } = true;
}

/// <summary>
/// 恢复操作结果
/// </summary>
public class RestoreResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// 恢复到的路径
    /// </summary>
    public string RestoredPath { get; set; } = string.Empty;
}
