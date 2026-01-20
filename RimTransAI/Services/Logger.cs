using System;
using System.IO;
using System.Text;

namespace RimTransAI.Services;

/// <summary>
/// 日志级别枚举
/// </summary>
public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3
}

/// <summary>
/// 简单的日志服务，将日志写入文件
/// </summary>
public class Logger
{
    private static readonly object _lock = new object();
    private static string? _logFilePath;
    private static bool _isInitialized = false;
    private static LogLevel _minimumLevel = LogLevel.Info;

    /// <summary>
    /// 设置调试模式
    /// </summary>
    public static void SetDebugMode(bool enabled)
    {
        _minimumLevel = enabled ? LogLevel.Debug : LogLevel.Info;
        Info($"调试模式已{(enabled ? "开启" : "关闭")}，最小日志级别: {_minimumLevel}");
    }

    /// <summary>
    /// 初始化日志服务
    /// </summary>
    public static void Initialize()
    {
        if (_isInitialized) return;

        try
        {
            // 在程序目录下创建 Logs 文件夹
            string logsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            if (!Directory.Exists(logsDir))
            {
                Directory.CreateDirectory(logsDir);
            }

            // 日志文件名：RimTransAI_20260115_143025.log
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _logFilePath = Path.Combine(logsDir, $"RimTransAI_{timestamp}.log");

            _isInitialized = true;

            // 写入启动信息
            Info("========================================");
            Info($"RimTransAI 启动 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Info($"日志文件: {_logFilePath}");
            Info("========================================");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"初始化日志服务失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 写入信息日志
    /// </summary>
    public static void Info(string message)
    {
        WriteLog(LogLevel.Info, "INFO", message);
    }

    /// <summary>
    /// 写入警告日志
    /// </summary>
    public static void Warning(string message)
    {
        WriteLog(LogLevel.Warning, "WARN", message);
    }

    /// <summary>
    /// 写入错误日志
    /// </summary>
    public static void Error(string message)
    {
        WriteLog(LogLevel.Error, "ERROR", message);
    }

    /// <summary>
    /// 写入异常日志
    /// </summary>
    public static void Error(string message, Exception ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine(message);
        sb.AppendLine($"异常类型: {ex.GetType().FullName}");
        sb.AppendLine($"异常消息: {ex.Message}");
        sb.AppendLine($"堆栈跟踪: {ex.StackTrace}");

        if (ex.InnerException != null)
        {
            sb.AppendLine($"内部异常: {ex.InnerException.Message}");
            sb.AppendLine($"内部堆栈: {ex.InnerException.StackTrace}");
        }

        WriteLog(LogLevel.Error, "ERROR", sb.ToString());
    }

    /// <summary>
    /// 写入调试日志
    /// </summary>
    public static void Debug(string message)
    {
        WriteLog(LogLevel.Debug, "DEBUG", message);
    }

    /// <summary>
    /// 核心写入方法（线程安全）
    /// </summary>
    private static void WriteLog(LogLevel level, string levelStr, string message)
    {
        lock (_lock)
        {
            // 日志级别过滤（在锁内检查，避免多线程竞态）
            if (level < _minimumLevel) return;

            if (!_isInitialized || string.IsNullOrEmpty(_logFilePath))
            {
                Console.WriteLine($"[{levelStr}] {message}");
                return;
            }

            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string logLine = $"[{timestamp}] [{levelStr}] {message}";

                // 同时输出到控制台和文件
                Console.WriteLine(logLine);
                File.AppendAllText(_logFilePath, logLine + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"写入日志失败: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 获取当前日志文件路径
    /// </summary>
    public static string? GetLogFilePath()
    {
        return _logFilePath;
    }
}
