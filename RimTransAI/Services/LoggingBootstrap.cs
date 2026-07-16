using System;
using System.Collections.Generic;
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
/// 配置和管理全局日志管道。业务代码应使用注入的 Microsoft.Extensions.Logging.ILogger。
/// </summary>
public static class LoggingBootstrap
{
    private const long FileSizeLimitBytes = 10 * 1024 * 1024;
    private const int AsyncBufferSize = 8192;
    private static readonly TimeSpan RetainedFileTimeLimit = TimeSpan.FromDays(14);
    private const string OutputTemplate =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] [{SourceContext}] {Message:lj} {Properties:j}{NewLine}{Exception}";

    private static readonly object InitializationLock = new();
    private static readonly object ApiKeyLock = new();
    private static readonly LoggingLevelSwitch LevelSwitch = new(LogEventLevel.Information);
    private static readonly List<string> ApiKeys = [];
    private static bool _isInitialized;
    private static bool _debugModeEnabled;
    private static string? _logsDirectory;
    private static DebugFileSink? _debugFileSink;
    private static OperationLogSink? _operationLogSink;

    public static bool IsDebugModeEnabled => Volatile.Read(ref _debugModeEnabled);

    public static void SetDebugMode(bool enabled)
    {
        lock (InitializationLock)
        {
            Volatile.Write(ref _debugModeEnabled, enabled);
            _debugFileSink?.SetEnabled(enabled);
            LevelSwitch.MinimumLevel = enabled ? LogEventLevel.Debug : LogEventLevel.Information;

            var logger = Log.ForContext<LoggingBootstrapMarker>();
            logger.Information(
                "调试模式已更新 Enabled={DebugEnabled} MinimumLevel={MinimumLevel}",
                enabled,
                LevelSwitch.MinimumLevel);

            if (enabled)
            {
                logger.Information("========================================");
                logger.Information("RimTransAI 调试日志已启动 StartTime={StartTime}", DateTimeOffset.Now);
                logger.Information("日志文件 LogFilePath={LogFilePath}", GetLogFilePath());
                logger.Information("========================================");

                if (_debugFileSink?.LastError is Exception error)
                {
                    logger.Error(error, "调试日志文件创建失败 LogsDirectory={LogsDirectory}", _logsDirectory);
                }
            }
        }
    }

    /// <summary>
    /// 注册 API Key 的历史值，使异步队列中尚未格式化的旧事件也能安全脱敏。
    /// </summary>
    public static void SetApiKey(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            return;

        lock (ApiKeyLock)
        {
            if (!ApiKeys.Exists(value => string.Equals(value, apiKey, StringComparison.Ordinal)))
            {
                ApiKeys.Add(apiKey);
            }
        }
    }

    public static void Initialize(string? logsDirectory = null)
    {
        lock (InitializationLock)
        {
            if (_isInitialized)
                return;

            _logsDirectory = logsDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            _operationLogSink = new OperationLogSink(
                () => IsDebugModeEnabled,
                RedactApiKeys);
            _debugFileSink = new DebugFileSink(
                _logsDirectory,
                new ApiKeyRedactingFormatter());

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(LevelSwitch)
                .Enrich.FromLogContext()
                .WriteTo.Console(new ApiKeyRedactingFormatter())
                .WriteTo.Sink(_operationLogSink)
                .WriteTo.Sink(_debugFileSink)
                .CreateLogger();

            Volatile.Write(ref _debugModeEnabled, false);
            LevelSwitch.MinimumLevel = LogEventLevel.Information;
            _isInitialized = true;
        }
    }

    public static void AttachOperationLogBuffer(
        OperationLogBuffer buffer,
        Action<Action>? dispatch = null)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _operationLogSink?.Attach(buffer, dispatch);
    }

    public static string? GetLogFilePath() => _debugFileSink?.CurrentLogFilePath;

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
                _debugFileSink = null;
                _operationLogSink = null;
                _logsDirectory = null;
                Volatile.Write(ref _debugModeEnabled, false);
                LevelSwitch.MinimumLevel = LogEventLevel.Information;
                lock (ApiKeyLock)
                {
                    ApiKeys.Clear();
                }

                _isInitialized = false;
            }
        }
    }

    private static string RedactApiKeys(string value)
    {
        string[] keys;
        lock (ApiKeyLock)
        {
            keys = [.. ApiKeys];
        }

        return ApiKeyRedactor.Redact(value, keys);
    }

    private sealed class ApiKeyRedactingFormatter : ITextFormatter
    {
        private readonly MessageTemplateTextFormatter _formatter =
            new(OutputTemplate, CultureInfo.InvariantCulture);

        public void Format(LogEvent logEvent, TextWriter output)
        {
            using var buffer = new StringWriter(CultureInfo.InvariantCulture);
            _formatter.Format(logEvent, buffer);
            output.Write(RedactApiKeys(buffer.ToString()));
        }
    }

    private sealed class DebugFileSink : ILogEventSink, IDisposable
    {
        private readonly object _sync = new();
        private readonly string _logsDirectory;
        private readonly ITextFormatter _formatter;
        private Serilog.ILogger? _fileLogger;
        private bool _enabled;
        private bool _creationFailed;

        public DebugFileSink(
            string logsDirectory,
            ITextFormatter formatter)
        {
            _logsDirectory = logsDirectory;
            _formatter = formatter;
        }

        public string? CurrentLogFilePath { get; private set; }
        public Exception? LastError { get; private set; }

        public void SetEnabled(bool enabled)
        {
            lock (_sync)
            {
                _enabled = enabled;
                if (enabled)
                {
                    _creationFailed = false;
                    LastError = null;
                    return;
                }

                DisposeFileLogger();
            }
        }

        public void Emit(LogEvent logEvent)
        {
            lock (_sync)
            {
                if (!_enabled || _creationFailed)
                    return;

                try
                {
                    EnsureFileLogger();
                    _fileLogger!.Write(logEvent);
                }
                catch (Exception ex)
                {
                    LastError = ex;
                    _creationFailed = true;
                    DisposeFileLogger();
                    Console.Error.WriteLine($"创建调试日志文件失败: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _enabled = false;
                DisposeFileLogger();
            }
        }

        private void EnsureFileLogger()
        {
            if (_fileLogger != null)
                return;

            Directory.CreateDirectory(_logsDirectory);
            DeleteExpiredLegacyLogs();
            var rollingPath = Path.Combine(_logsDirectory, "RimTransAI-.log");
            CurrentLogFilePath = Path.Combine(_logsDirectory, $"RimTransAI-{DateTime.Now:yyyyMMdd}.log");

            _fileLogger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Async(
                    sink => sink.File(
                        _formatter,
                        rollingPath,
                        rollingInterval: RollingInterval.Day,
                        fileSizeLimitBytes: FileSizeLimitBytes,
                        rollOnFileSizeLimit: true,
                        retainedFileCountLimit: null,
                        retainedFileTimeLimit: RetainedFileTimeLimit,
                        encoding: Encoding.UTF8,
                        shared: true,
                        flushToDiskInterval: TimeSpan.FromSeconds(1)),
                    bufferSize: AsyncBufferSize,
                    blockWhenFull: false)
                .CreateLogger();
        }

        private void DisposeFileLogger()
        {
            (_fileLogger as IDisposable)?.Dispose();
            _fileLogger = null;
            CurrentLogFilePath = null;
        }

        private void DeleteExpiredLegacyLogs()
        {
            var cutoff = DateTime.UtcNow - RetainedFileTimeLimit;
            foreach (var path in Directory.EnumerateFiles(
                         _logsDirectory,
                         "RimTransAI*.log",
                         SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(path) < cutoff)
                    {
                        File.Delete(path);
                    }
                }
                catch (IOException)
                {
                    // 单个历史文件被占用时不影响本次调试日志启动。
                }
                catch (UnauthorizedAccessException)
                {
                    // 便携目录内的个别只读历史文件保留即可。
                }
            }
        }
    }

    private sealed class LoggingBootstrapMarker
    {
    }
}

public static class ApiKeyRedactor
{
    public const string RedactedValue = "[REDACTED_API_KEY]";

    public static string Redact(string value, string? apiKey) =>
        Redact(value, string.IsNullOrEmpty(apiKey) ? [] : [apiKey]);

    public static string Redact(string value, IEnumerable<string> apiKeys)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        foreach (var apiKey in apiKeys)
        {
            if (!string.IsNullOrEmpty(apiKey))
            {
                value = value.Replace(apiKey, RedactedValue, StringComparison.Ordinal);
            }
        }

        return value;
    }
}
