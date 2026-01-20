using System;
using System.Threading;

namespace RimTransAI.Services;

/// <summary>
/// 翻译进度信息
/// </summary>
public class TranslationProgress
{
    public int ProcessedBatches { get; set; }
    public int TotalBatches { get; set; }
    public int ActiveThreads { get; set; }
    public string? CurrentBatchInfo { get; set; }
}

/// <summary>
/// 线程安全的进度报告器
/// </summary>
public class ThreadSafeProgressReporter : IDisposable
{
    private readonly Action<TranslationProgress>? _onProgress;
    private readonly Action<string>? _onLog;
    private readonly Lock _lock = new Lock();
    private bool _disposed;

    /// <summary>
    /// 初始化进度报告器
    /// </summary>
    /// <param name="onProgress">进度更新回调</param>
    /// <param name="onLog">日志输出回调</param>
    public ThreadSafeProgressReporter(
        Action<TranslationProgress>? onProgress = null,
        Action<string>? onLog = null)
    {
        _onProgress = onProgress;
        _onLog = onLog;
    }

    /// <summary>
    /// 线程安全地报告进度
    /// </summary>
    public void ReportProgress(int processedBatches, int totalBatches, int activeThreads, string? currentBatchInfo = null)
    {
        if (_disposed) return;

        lock (_lock)
        {
            _onProgress?.Invoke(new TranslationProgress
            {
                ProcessedBatches = processedBatches,
                TotalBatches = totalBatches,
                ActiveThreads = activeThreads,
                CurrentBatchInfo = currentBatchInfo
            });
        }
    }

    /// <summary>
    /// 线程安全地报告日志
    /// </summary>
    public void ReportLog(string message)
    {
        if (_disposed) return;

        lock (_lock)
        {
            _onLog?.Invoke(message);
        }
    }

    /// <summary>
    /// 线程安全地报告格式化日志
    /// </summary>
    public void ReportLog(string format, params object[] args)
    {
        if (_disposed) return;

        lock (_lock)
        {
            _onLog?.Invoke(string.Format(format, args));
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
