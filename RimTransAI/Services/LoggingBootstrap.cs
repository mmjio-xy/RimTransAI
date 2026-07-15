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

/// <summary>
/// 仅负责配置和管理全局日志管道。业务代码应使用注入的 Microsoft.Extensions.Logging.ILogger。
/// </summary>
public static class LoggingBootstrap
{
    private const long FileSizeLimitBytes = 10 * 1024 * 1024;
    private const int RetainedFileCountLimit = 14;
    private const int AsyncBufferSize = 8192;
    private const string OutputTemplate =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] [{SourceContext}] {Message:lj} {Properties:j}{NewLine}{Exception}";

    private static readonly object InitializationLock = new();
    private static readonly LoggingLevelSwitch LevelSwitch = new(LogEventLevel.Information);
    private static bool _isInitialized;
    private static string? _logFilePath;
    private static string? _apiKey;

    public static void SetDebugMode(bool enabled)
    {
        LevelSwitch.MinimumLevel = enabled ? LogEventLevel.Debug : LogEventLevel.Information;
        Log.ForContext<LoggingBootstrapMarker>().Information(
            "调试模式已更新 Enabled={DebugEnabled} MinimumLevel={MinimumLevel}",
            enabled,
            LevelSwitch.MinimumLevel);
    }

    /// <summary>
    /// 注册当前 API Key，仅用于所有输出 Sink 的最终文本脱敏。
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

                // 使用连字符区分旧版文件名，避免滚动器误判旧文件序号。
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

                var startupLogger = Log.ForContext<LoggingBootstrapMarker>();
                startupLogger.Information("========================================");
                startupLogger.Information("RimTransAI 启动 StartTime={StartTime}", DateTimeOffset.Now);
                startupLogger.Information("日志文件 LogFilePath={LogFilePath}", _logFilePath);
                startupLogger.Information("========================================");
            }
            catch (Exception ex)
            {
                _isInitialized = false;
                _logFilePath = null;
                Console.WriteLine($"初始化日志服务失败: {ex.Message}");
            }
        }
    }

    public static string? GetLogFilePath() => Volatile.Read(ref _logFilePath);

    public static void Shutdown()
    {
        lock (InitializationLock)
        {
            if (!_isInitialized)
                return;

            try
            {
                Log.ForContext<LoggingBootstrapMarker>().Information("RimTransAI 日志服务正在关闭");
                Log.CloseAndFlush();
            }
            finally
            {
                _isInitialized = false;
            }
        }
    }

    private static string RedactApiKey(string value) =>
        ApiKeyRedactor.Redact(value, Volatile.Read(ref _apiKey));

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

    private sealed class LoggingBootstrapMarker
    {
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
