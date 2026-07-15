using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace RimTransAI.Services;

public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warning = 3,
    Error = 4
}

/// <summary>
/// 应用日志入口。保留静态 API 以兼容现有调用点，底层使用 Serilog 异步写入滚动文件。
/// </summary>
public static class Logger
{
    private const long FileSizeLimitBytes = 10 * 1024 * 1024;
    private const int RetainedFileCountLimit = 14;
    private const int AsyncBufferSize = 8192;
    private const string OutputTemplate =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

    private static readonly object InitializationLock = new();
    private static readonly LoggingLevelSwitch LevelSwitch = new(LogEventLevel.Information);
    private static bool _isInitialized;
    private static string? _logFilePath;
    private static string? _apiKey;

    public static void SetDebugMode(bool enabled)
    {
        LevelSwitch.MinimumLevel = enabled ? LogEventLevel.Debug : LogEventLevel.Information;
        Info($"调试模式已{(enabled ? "开启" : "关闭")}，最小日志级别: {LevelSwitch.MinimumLevel}");
    }

    /// <summary>
    /// 注册当前 API Key，仅用于日志脱敏。除 API Key 外不修改其他日志内容。
    /// </summary>
    public static void SetApiKey(string? apiKey)
    {
        Volatile.Write(ref _apiKey, string.IsNullOrEmpty(apiKey) ? null : apiKey);
    }

    public static void Initialize()
    {
        lock (InitializationLock)
        {
            if (_isInitialized)
                return;

            try
            {
                // 按用户要求继续使用程序目录下的 Logs 文件夹。
                var logsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                Directory.CreateDirectory(logsDirectory);

                // 使用连字符区分旧版的 RimTransAI_yyyyMMdd_HHmmss.log，避免滚动器误判旧文件序号。
                var rollingPath = Path.Combine(logsDirectory, "RimTransAI-.log");
                _logFilePath = Path.Combine(logsDirectory, $"RimTransAI-{DateTime.Now:yyyyMMdd}.log");

                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.ControlledBy(LevelSwitch)
                    .Enrich.FromLogContext()
                    .WriteTo.Console(new ApiKeyRedactingFormatter())
                    .WriteTo.Async(
                        sink => sink.File(
                            new ApiKeyRedactingFormatter(),
                            rollingPath,
                            rollingInterval: RollingInterval.Day,
                            fileSizeLimitBytes: FileSizeLimitBytes,
                            rollOnFileSizeLimit: true,
                            retainedFileCountLimit: RetainedFileCountLimit,
                            encoding: Encoding.UTF8,
                            shared: true,
                            flushToDiskInterval: TimeSpan.FromSeconds(1)),
                        bufferSize: AsyncBufferSize,
                        blockWhenFull: true)
                    .CreateLogger();

                _isInitialized = true;

                Info("========================================");
                Info($"RimTransAI 启动 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                Info($"日志文件: {_logFilePath}");
                Info("========================================");
            }
            catch (Exception ex)
            {
                _isInitialized = false;
                _logFilePath = null;
                Console.WriteLine($"初始化日志服务失败: {ex.Message}");
            }
        }
    }

    public static void Trace(string message)
    {
        Write(LogEventLevel.Verbose, "TRACE", message);
    }

    public static void Debug(string message)
    {
        Write(LogEventLevel.Debug, "DEBUG", message);
    }

    public static void Info(string message)
    {
        Write(LogEventLevel.Information, "INFO", message);
    }

    public static void Warning(string message)
    {
        Write(LogEventLevel.Warning, "WARN", message);
    }

    public static void Error(string message)
    {
        Write(LogEventLevel.Error, "ERROR", message);
    }

    public static void Error(string message, Exception ex)
    {
        var safeMessage = RedactApiKey(message);

        if (!_isInitialized)
        {
            Console.WriteLine($"[ERROR] {safeMessage}{Environment.NewLine}{RedactApiKey(ex.ToString())}");
            return;
        }

        Log.ForContext("SourceContext", "RimTransAI")
            .Error(ex, "{ErrorMessage}", safeMessage);
    }

    public static string? GetLogFilePath()
    {
        return Volatile.Read(ref _logFilePath);
    }

    public static void Shutdown()
    {
        lock (InitializationLock)
        {
            if (!_isInitialized)
                return;

            try
            {
                Info("RimTransAI 日志服务正在关闭");
                Log.CloseAndFlush();
            }
            finally
            {
                _isInitialized = false;
            }
        }
    }

    private static void Write(LogEventLevel level, string fallbackLevel, string message)
    {
        if (level < LevelSwitch.MinimumLevel)
            return;

        var safeMessage = RedactApiKey(message);
        if (!_isInitialized)
        {
            Console.WriteLine($"[{fallbackLevel}] {safeMessage}");
            return;
        }

        Log.ForContext("SourceContext", "RimTransAI")
            .Write(level, "{LogMessage}", safeMessage);
    }

    private static string RedactApiKey(string value)
    {
        return ApiKeyRedactor.Redact(value, Volatile.Read(ref _apiKey));
    }

    private sealed class ApiKeyRedactingFormatter : ITextFormatter
    {
        private readonly MessageTemplateTextFormatter _formatter =
            new(OutputTemplate, CultureInfo.InvariantCulture);

        public void Format(LogEvent logEvent, TextWriter output)
        {
            using var buffer = new StringWriter(CultureInfo.InvariantCulture);
            _formatter.Format(logEvent, buffer);
            output.Write(RedactApiKey(buffer.ToString()));
        }
    }
}

public static class ApiKeyRedactor
{
    public const string RedactedValue = "[REDACTED_API_KEY]";

    public static string Redact(string value, string? apiKey)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(apiKey))
            return value;

        return value.Replace(apiKey, RedactedValue, StringComparison.Ordinal);
    }
}
