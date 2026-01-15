using System;
using System.IO;
using System.Text.Json;
using RimTransAI.Models;

namespace RimTransAI.Services;

public class ConfigService
{
    private const string FileName = "settings.json";
    private readonly string _filePath;

    public ConfigService()
    {
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
                return;
            }
            catch (JsonException ex)
            {
                Logger.Warning($"配置文件JSON格式错误: {ex.Message}，使用默认配置");
            }
            catch (IOException ex)
            {
                Logger.Warning($"配置文件读取失败: {ex.Message}，使用默认配置");
            }
            catch (Exception ex)
            {
                Logger.Error($"加载配置失败: {ex.GetType().Name} - {ex.Message}，使用默认配置");
            }
        }

        CurrentConfig = new AppConfig();
    }

    public void SaveConfig(AppConfig config)
    {
        try
        {
            // 使用 AOT 友好的序列化方法
            var json = JsonSerializer.Serialize(config, AppJsonContext.Default.AppConfig);
            File.WriteAllText(_filePath, json);

            // 更新内存缓存
            CurrentConfig = config;
        }
        catch (Exception ex)
        {
            Logger.Error($"保存配置失败", ex);
        }
    }
}