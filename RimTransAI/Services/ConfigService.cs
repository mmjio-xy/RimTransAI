using System;
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

    public ConfigService(ILogger<ConfigService>? logger = null)
    {
        _logger = logger ?? NullLogger<ConfigService>.Instance;
        // 存放在 EXE 同级目录
        _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
        LoadConfig();
    }

    // 内存中的配置缓存（初始化为默认值，避免 CS8618 警告）
    public AppConfig CurrentConfig { get; private set; } = new AppConfig();

    public void LoadConfig()
    {
        if (File.Exists(_filePath))
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                //传入 AppJsonContext.Default.AppConfig
                CurrentConfig = JsonSerializer.Deserialize(json, AppJsonContext.Default.AppConfig)
                                ?? new AppConfig();

                NormalizeConfig(CurrentConfig);

                // 兼容历史配置：清理旧版本序列化异常导致的空来源项，并补齐默认值
                if (CurrentConfig.ModSourceFolders != null)
                {
                    CurrentConfig.ModSourceFolders = CurrentConfig.ModSourceFolders
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
                return;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "配置文件 JSON 格式错误，使用默认配置 ConfigPath={ConfigPath}", _filePath);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "配置文件读取失败，使用默认配置 ConfigPath={ConfigPath}", _filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载配置失败，使用默认配置 ConfigPath={ConfigPath}", _filePath);
            }
        }

        CurrentConfig = new AppConfig();
    }

    public void SaveConfig(AppConfig config)
    {
        try
        {
            NormalizeConfig(config);

            // 使用 AOT 友好的序列化方法
            var json = JsonSerializer.Serialize(config, AppJsonContext.Default.AppConfig);
            File.WriteAllText(_filePath, json);

            // 更新内存缓存
            CurrentConfig = config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存配置失败 ConfigPath={ConfigPath}", _filePath);
        }
    }

    public static void NormalizeConfig(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        config.MaxThreads = Math.Clamp(config.MaxThreads, 1, 10);
        config.ThreadIntervalMs = Math.Clamp(config.ThreadIntervalMs, 0, 1000);
        config.ApiRequestTimeoutSeconds = Math.Clamp(config.ApiRequestTimeoutSeconds, 30, 1800);
    }
}

