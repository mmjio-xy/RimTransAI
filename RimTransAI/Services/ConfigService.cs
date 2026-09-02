using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RimTransAI.Models;

namespace RimTransAI.Services;

public class ConfigService
{
    private const string FileName = "settings.json";
    private readonly string _filePath;
    private readonly ILogger<ConfigService> _logger;
    private bool _isReadOnlyDueToFutureConfig;

    public ConfigService(ILogger<ConfigService>? logger = null, string? filePath = null)
    {
        _logger = logger ?? NullLogger<ConfigService>.Instance;
        // 存放在 EXE 同级目录
        _filePath = filePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
        LoadConfig();
    }

    // 内存中的配置缓存（初始化为默认值，避免 CS8618 警告）
    public AppConfig CurrentConfig { get; private set; } = new AppConfig();

    public void LoadConfig()
    {
        _isReadOnlyDueToFutureConfig = false;

        if (File.Exists(_filePath))
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                CurrentConfig = JsonSerializer.Deserialize(json, AppJsonContext.Default.AppConfig)
                                ?? new AppConfig();

                var requiresLegacyApiMigration = DoesNotContainVersion(json);
                var hasConfigVersion = TryGetConfigVersion(json, out var configVersion);
                var futureConfigVersion = hasConfigVersion &&
                                          configVersion > AppConfig.CurrentConfigVersion;
                if (requiresLegacyApiMigration)
                {
                    var legacyApiSettings = JsonSerializer.Deserialize(
                        json,
                        AppJsonContext.Default.LegacyApiSettings) ?? new LegacyApiSettings();
                    CurrentConfig.TranslationApis =
                    [
                        new TranslationApiProfile
                        {
                            Name = "默认 API",
                            IsEnabled = true,
                            ApiUrl = legacyApiSettings.ApiUrl,
                            ApiKey = legacyApiSettings.ApiKey,
                            AutoCompleteApiUrl = legacyApiSettings.AutoCompleteApiUrl,
                            RequestTimeoutSeconds = legacyApiSettings.ApiRequestTimeoutSeconds,
                            Models =
                            [
                                new TranslationApiModel
                                {
                                    ModelId = legacyApiSettings.TargetModel,
                                    IsEnabled = true
                                }
                            ]
                        }
                    ];
                }

                _isReadOnlyDueToFutureConfig = futureConfigVersion;
                var requiresConfigVersionUpgrade = !requiresLegacyApiMigration && !hasConfigVersion;

                NormalizeConfig(CurrentConfig);

                if (futureConfigVersion)
                {
                    // 保留未来版本标记，避免内存中的配置看起来像当前格式。
                    CurrentConfig.Version = ReadVersion(json, CurrentConfig.Version);
                    CurrentConfig.ConfigVersion = configVersion;
                }

                NormalizeModSourceFolders(CurrentConfig);

                if (_isReadOnlyDueToFutureConfig)
                {
                    _logger.LogUserWarning(
                        "配置文件结构版本 {ConfigVersion} 高于当前支持版本 {CurrentConfigVersion}，已以只读方式载入：{ConfigPath}",
                        configVersion,
                        AppConfig.CurrentConfigVersion,
                        _filePath);
                }
                else if ((requiresLegacyApiMigration || requiresConfigVersionUpgrade) && !SaveConfig(CurrentConfig))
                {
                    _logger.LogUserWarning("配置已载入，但升级后的配置写回失败：{ConfigPath}", _filePath);
                }
                else if (requiresLegacyApiMigration)
                {
                    _logger.LogInformation("已将旧 API 配置迁移为多 API 配置 ConfigPath={ConfigPath}", _filePath);
                }
                else if (requiresConfigVersionUpgrade)
                {
                    _logger.LogInformation("已补写配置结构版本 ConfigVersion={ConfigVersion} ConfigPath={ConfigPath}",
                        AppConfig.CurrentConfigVersion,
                        _filePath);
                }

                return;
            }
            catch (JsonException ex)
            {
                _logger.LogUserWarning(ex, "配置文件格式错误，已使用默认配置：{ConfigPath}", _filePath);
            }
            catch (IOException ex)
            {
                _logger.LogUserWarning(ex, "配置文件读取失败，已使用默认配置：{ConfigPath}", _filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载配置失败，使用默认配置 ConfigPath={ConfigPath}", _filePath);
            }
        }

        CurrentConfig = new AppConfig();
    }

    public bool SaveConfig(AppConfig config)
    {
        try
        {
            if (_isReadOnlyDueToFutureConfig)
            {
                _logger.LogUserWarning(
                    "配置文件结构版本高于当前支持版本，已阻止保存以避免覆盖未知字段：{ConfigPath}",
                    _filePath);
                return false;
            }

            NormalizeConfig(config);

            // 使用 AOT 友好的序列化方法
            var json = JsonSerializer.Serialize(config, AppJsonContext.Default.AppConfig);
            File.WriteAllText(_filePath, json);

            // 更新内存缓存
            CurrentConfig = config;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存配置失败 ConfigPath={ConfigPath}", _filePath);
            return false;
        }
    }

    public static void NormalizeConfig(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        config.Version = AppConfig.CurrentProgramVersion;
        config.ConfigVersion = AppConfig.CurrentConfigVersion;
        config.MaxThreads = Math.Clamp(config.MaxThreads, 1, 10);
        config.ThreadIntervalMs = Math.Clamp(config.ThreadIntervalMs, 0, 1000);
        config.TranslationApis ??= new List<TranslationApiProfile>();

        foreach (var api in config.TranslationApis)
        {
            api.Name = api.Name?.Trim() ?? string.Empty;
            api.ApiUrl = api.ApiUrl?.Trim() ?? string.Empty;
            api.ApiKey = api.ApiKey?.Trim() ?? string.Empty;
            api.RequestTimeoutSeconds = Math.Clamp(api.RequestTimeoutSeconds, 30, 1800);
            api.Models ??= new List<TranslationApiModel>();

            foreach (var model in api.Models)
            {
                model.ModelId = model.ModelId?.Trim() ?? string.Empty;
            }
        }
    }

    private static bool DoesNotContainVersion(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind != JsonValueKind.Object ||
               !document.RootElement.EnumerateObject()
                   .Any(property => string.Equals(property.Name, "version", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetConfigVersion(string json, out int configVersion)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "configVersion", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.Number &&
                    property.Value.TryGetInt32(out configVersion))
                {
                    return true;
                }
            }
        }

        configVersion = 0;
        return false;
    }

    private static string ReadVersion(string json, string fallback)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "version", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString() ?? fallback;
                }
            }
        }

        return fallback;
    }

    private static void NormalizeModSourceFolders(AppConfig config)
    {
        if (config.ModSourceFolders == null)
        {
            config.ModSourceFolders = new List<ModSourceFolder>();
            return;
        }

        config.ModSourceFolders = config.ModSourceFolders
            .Where(x => !string.IsNullOrWhiteSpace(x.FolderPath))
            .Select(x =>
            {
                if (string.IsNullOrWhiteSpace(x.Id))
                {
                    x.Id = Guid.NewGuid().ToString("N");
                }

                if (string.IsNullOrWhiteSpace(x.IconKey))
                {
                    x.IconKey = "Folder";
                }

                if (string.IsNullOrWhiteSpace(x.DisplayName))
                {
                    x.DisplayName = Path.GetFileName(x.FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                }

                return x;
            })
            .ToList();
    }
}

