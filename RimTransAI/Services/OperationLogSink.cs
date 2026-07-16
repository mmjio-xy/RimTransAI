using System;
using System.Globalization;
using RimTransAI.Models;
using Serilog.Core;
using Serilog.Events;

namespace RimTransAI.Services;

/// <summary>
/// 将诊断日志中的用户可见事件投递到 UI 操作日志。
/// 非调试模式只显示显式标记的关键步骤和 Error/Fatal；调试模式显示全部事件。
/// </summary>
public sealed class OperationLogSink : ILogEventSink
{
    public const string UiVisibleProperty = "UiVisible";
    public const string UiLevelProperty = "UiLogLevel";

    private readonly Func<bool> _isDebugModeEnabled;
    private readonly Func<string, string> _redact;
    private readonly object _attachmentLock = new();
    private OperationLogBuffer? _buffer;
    private Action<Action> _dispatch = action => action();

    public OperationLogSink(
        Func<bool> isDebugModeEnabled,
        Func<string, string>? redact = null)
    {
        _isDebugModeEnabled = isDebugModeEnabled ?? throw new ArgumentNullException(nameof(isDebugModeEnabled));
        _redact = redact ?? (value => value);
    }

    public void Attach(OperationLogBuffer buffer, Action<Action>? dispatch = null)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        lock (_attachmentLock)
        {
            _buffer = buffer;
            _dispatch = dispatch ?? (action => action());
        }
    }

    public void Emit(LogEvent logEvent)
    {
        var debugMode = _isDebugModeEnabled();
        var uiVisible = IsUiVisible(logEvent);
        if (!debugMode && !uiVisible && logEvent.Level < LogEventLevel.Error)
            return;

        OperationLogBuffer? buffer;
        Action<Action> dispatch;
        lock (_attachmentLock)
        {
            buffer = _buffer;
            dispatch = _dispatch;
        }

        if (buffer == null)
            return;

        var message = _redact(logEvent.RenderMessage(CultureInfo.InvariantCulture));
        var level = ResolveLevel(logEvent);
        if (debugMode && !uiVisible)
        {
            message = $"[{ToShortLevel(logEvent.Level)}] {message}";
        }

        dispatch(() => buffer.Append(message, level, logEvent.Timestamp));
    }

    private static bool IsUiVisible(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue(UiVisibleProperty, out var value) &&
        value is ScalarValue { Value: true };

    private static OperationLogLevel ResolveLevel(LogEvent logEvent)
    {
        if (logEvent.Properties.TryGetValue(UiLevelProperty, out var value) &&
            value is ScalarValue { Value: string name } &&
            Enum.TryParse<OperationLogLevel>(name, ignoreCase: true, out var explicitLevel))
        {
            return explicitLevel;
        }

        return logEvent.Level switch
        {
            LogEventLevel.Warning => OperationLogLevel.Warning,
            LogEventLevel.Error or LogEventLevel.Fatal => OperationLogLevel.Error,
            _ => OperationLogLevel.Info
        };
    }

    private static string ToShortLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => "VRB",
        LogEventLevel.Debug => "DBG",
        LogEventLevel.Information => "INF",
        LogEventLevel.Warning => "WRN",
        LogEventLevel.Error => "ERR",
        LogEventLevel.Fatal => "FTL",
        _ => "LOG"
    };
}
