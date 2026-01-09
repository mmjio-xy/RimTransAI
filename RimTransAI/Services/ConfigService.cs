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

    // 内存中的配置缓存
    public AppConfig CurrentConfig { get; private set; }

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
            catch
            {
                // 读取失败则忽略，使用默认值
            }
        }

        CurrentConfig = new AppConfig();
    }

    public void SaveConfig(AppConfig config)
    {
        try
        {
            // 使用 AOT 友好的上下文进行序列化 (格式化输出方便人工阅读)
            var options = new JsonSerializerOptions
            { 
                WriteIndented = true,
                TypeInfoResolver = AppJsonContext.Default // 绑定 AOT 上下文
            };
            // 使用 options 进行序列化
            var json = JsonSerializer.Serialize(config, typeof(AppConfig), options);
            File.WriteAllText(_filePath, json);
            CurrentConfig = config;

            // 更新内存缓存
            CurrentConfig = config;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"保存配置失败: {ex.Message}");
        }
    }
}