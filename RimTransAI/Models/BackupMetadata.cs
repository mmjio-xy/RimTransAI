using System;
using System.Collections.Generic;

namespace RimTransAI.Models;

/// <summary>
/// 备份元数据文件结构
/// </summary>
public class BackupMetadataFile
{
    /// <summary>
    /// 元数据文件版本
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.Now;

    /// <summary>
    /// 备份条目列表
    /// </summary>
    public List<BackupMetadataEntry> Entries { get; set; } = new();
}

/// <summary>
/// 单个备份条目的元数据
/// </summary>
public class BackupMetadataEntry
{
    /// <summary>
    /// 备份文件名（不含路径）
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 备份文件的 SHA-256 哈希值
    /// </summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>
    /// 文件大小（字节）
    /// </summary>
    public long FileSizeBytes { get; set; }

    /// <summary>
    /// Mod 名称
    /// </summary>
    public string ModName { get; set; } = string.Empty;

    /// <summary>
    /// Mod 的 PackageId
    /// </summary>
    public string PackageId { get; set; } = string.Empty;

    /// <summary>
    /// 版本（1.5、1.4、Common、空字符串表示根目录）
    /// </summary>
    public string ModVersion { get; set; } = string.Empty;

    /// <summary>
    /// 版本显示名称（Root、1.5、1.4、Common）
    /// </summary>
    public string ModVersionDisplay { get; set; } = string.Empty;

    /// <summary>
    /// 目标语言（如 ChineseSimplified）
    /// </summary>
    public string TargetLanguage { get; set; } = string.Empty;

    /// <summary>
    /// 恢复相对路径（从 Mod 根目录起，如 "1.5/Languages/ChineseSimplified"）
    /// </summary>
    public string RestoreRelativePath { get; set; } = string.Empty;

    /// <summary>
    /// 备份创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 备份时的原始 Mod 路径（仅供参考）
    /// </summary>
    public string OriginalModPath { get; set; } = string.Empty;
}
