using System;

namespace RimTransAI.Models;

public enum OperationLogLevel
{
    Info,
    Success,
    Warning,
    Error
}

public sealed record OperationLogEntry(
    DateTimeOffset Timestamp,
    OperationLogLevel Level,
    string Message)
{
    public static OperationLogEntry Create(
        string message,
        DateTimeOffset? timestamp = null,
        OperationLogLevel? level = null)
    {
        return new OperationLogEntry(
            timestamp ?? DateTimeOffset.Now,
            level ?? Classify(message),
            message);
    }

    private static OperationLogLevel Classify(string message)
    {
        if (message.Contains("取消", StringComparison.Ordinal) ||
            message.Contains("停止", StringComparison.Ordinal) ||
            message.Contains("跳过", StringComparison.Ordinal) ||
            message.Contains("未发现", StringComparison.Ordinal))
        {
            return OperationLogLevel.Warning;
        }

        if (message.Contains("错误", StringComparison.Ordinal) ||
            message.Contains("失败", StringComparison.Ordinal) ||
            message.Contains("致命", StringComparison.Ordinal) ||
            message.Contains('✗'))
        {
            return OperationLogLevel.Error;
        }

        if (message.Contains("完成", StringComparison.Ordinal) ||
            message.Contains("成功", StringComparison.Ordinal) ||
            message.Contains('✓'))
        {
            return OperationLogLevel.Success;
        }

        return OperationLogLevel.Info;
    }
}
