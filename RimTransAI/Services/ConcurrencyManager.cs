using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace RimTransAI.Services;

/// <summary>
/// 并发控制器 - 控制并发请求数，防止 API 平台拒绝
/// </summary>
public class ConcurrencyManager : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly SemaphoreSlim _requestStartGate = new(1, 1);
    private readonly CancellationTokenSource _cts;
    private readonly int _maxConcurrentRequests;
    private readonly int _intervalMs;
    private long _lastRequestStartTimestamp;
    private bool _disposed;

    /// <summary>
    /// 初始化并发控制器
    /// </summary>
    /// <param name="maxConcurrentRequests">最大并发请求数</param>
    /// <param name="intervalMs">请求间隔（毫秒），防止请求过于密集</param>
    public ConcurrencyManager(int maxConcurrentRequests, int intervalMs = 0)
    {
        if (maxConcurrentRequests < 1)
            throw new ArgumentException("最大并发数必须大于 0", nameof(maxConcurrentRequests));

        _maxConcurrentRequests = maxConcurrentRequests;
        _intervalMs = Math.Max(0, intervalMs);
        _semaphore = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);
        _cts = new CancellationTokenSource();
    }

    /// <summary>
    /// 执行并发操作（带限流和间隔控制）
    /// </summary>
    /// <typeparam name="T">返回类型</typeparam>
    /// <param name="operation">要执行的操作</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ConcurrencyManager));

        // 组合取消令牌
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _cts.Token);

        try
        {
            // 获取信号量（限流）
            await _semaphore.WaitAsync(linkedCts.Token);
            Logger.Debug($"并发槽已获取 | 剩余: {_semaphore.CurrentCount}/{_maxConcurrentRequests}");

            try
            {
                // 所有并发任务共享同一个启动门，确保请求开始时间真正错开。
                await WaitForRequestIntervalAsync(linkedCts.Token);

                // 执行操作
                return await operation(linkedCts.Token);
            }
            finally
            {
                // 释放信号量
                _semaphore.Release();
                Logger.Debug($"并发槽已释放 | 剩余: {_semaphore.CurrentCount}/{_maxConcurrentRequests}");
            }
        }
        catch (OperationCanceledException) when (linkedCts.Token.IsCancellationRequested)
        {
            throw new OperationCanceledException("操作已取消", linkedCts.Token);
        }
    }

    private async Task WaitForRequestIntervalAsync(CancellationToken cancellationToken)
    {
        if (_intervalMs == 0)
            return;

        await _requestStartGate.WaitAsync(cancellationToken);
        try
        {
            if (_lastRequestStartTimestamp != 0)
            {
                var elapsed = Stopwatch.GetElapsedTime(_lastRequestStartTimestamp);
                var remaining = TimeSpan.FromMilliseconds(_intervalMs) - elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, cancellationToken);
                }
            }

            _lastRequestStartTimestamp = Stopwatch.GetTimestamp();
        }
        finally
        {
            _requestStartGate.Release();
        }
    }

    /// <summary>
    /// 取消所有正在执行的操作
    /// </summary>
    public void CancelAll()
    {
        _cts.Cancel();
    }

    /// <summary>
    /// 获取当前可用的并发数
    /// </summary>
    public int AvailableCount => _semaphore.CurrentCount;

    /// <summary>
    /// 获取当前正在执行的操作数
    /// </summary>
    public int RunningCount => _maxConcurrentRequests - _semaphore.CurrentCount;

    /// <summary>
    /// 最大并发请求数。
    /// </summary>
    public int MaxConcurrentRequests => _maxConcurrentRequests;

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _cts.Cancel();
        _cts.Dispose();
        _requestStartGate.Dispose();
        _semaphore.Dispose();
        _disposed = true;
    }
}
