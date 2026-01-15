using System;
using System.IO;
using System.Text;

namespace RimTransAI.Services;

/// <summary>
/// 简单的日志服务，将日志写入文件
/// </summary>
public class Logger
{
    private static readonly object _lock = new object();
    private static string? _logFilePath;
    private static bool _isInitialized = false;

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
        WriteLog("INFO", message);
    }

    /// <summary>
    /// 写入警告日志
    /// </summary>
    public static void Warning(string message)
    {
        WriteLog("WARN", message);
    }

    /// <summary>
    /// 写入错误日志
    /// </summary>
    public static void Error(string message)
    {
        WriteLog("ERROR", message);
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

        WriteLog("ERROR", sb.ToString());
    }

    /// <summary>
    /// 写入调试日志
    /// </summary>
    public static void Debug(string message)
    {
        WriteLog("DEBUG", message);
    }

    /// <summary>
    /// 核心写入方法
    /// </summary>
    private static void WriteLog(string level, string message)
    {
        if (!_isInitialized || string.IsNullOrEmpty(_logFilePath))
        {
            Console.WriteLine($"[{level}] {message}");
            return;
        }

        try
        {
            lock (_lock)
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string logLine = $"[{timestamp}] [{level}] {message}";

                // 同时输出到控制台和文件
                Console.WriteLine(logLine);
                File.AppendAllText(_logFilePath, logLine + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"写入日志失败: {ex.Message}");
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
