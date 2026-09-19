/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Fakes
*文件名： FakeDownloadHandler
*版本号： V1.0.0.0
*唯一标识：eab2c939-3e94-4da2-88bf-7127bda8ebe6
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:32:00
*描述：下载处理器的假实现，用于验证下载队列的并发控制、重试与状态流转。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:32:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Abstractions;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Tests.Fakes;

/// <summary>
/// <see cref="IDownloadHandler"/> 的假实现。
/// </summary>
/// <remarks>
/// 通过它把「下载过程」压缩成一次可控延时的模拟执行，
/// 使得队列的并发上限、重试次数、取消传播等行为可以在毫秒级内被稳定断言。
/// 同时它会记录观察到的最大并发数，这是验证并发上限的关键证据。
/// </remarks>
public sealed class FakeDownloadHandler : IDownloadHandler
{
    private readonly object _concurrencyGate = new();
    private readonly object _startedGate = new();
    private readonly List<Guid> _startedTaskIds = new();
    private int _currentConcurrency;

    /// <summary>单次下载的模拟耗时，默认 30 毫秒。</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(30);

    /// <summary>根据任务生成下载结果的工厂。默认返回成功。可在此返回失败以模拟各类错误。</summary>
    public Func<DownloadTask, DownloadResult> ResultFactory { get; set; }
        = task => DownloadResult.Ok($"{task.Id}.mp4", 1024);

    /// <summary>是否响应取消。为 true 时在延时过程中被取消会抛出 <see cref="OperationCanceledException"/>。</summary>
    public bool HonorCancellation { get; set; } = true;

    /// <summary>观察到的最大并发执行数，用于断言队列的并发上限是否生效。</summary>
    public int MaxObservedConcurrency { get; private set; }

    /// <summary>已开始执行过的任务标识列表，按开始顺序排列。</summary>
    public IReadOnlyList<Guid> StartedTaskIds
    {
        get
        {
            lock (_startedGate)
            {
                return _startedTaskIds.ToList();
            }
        }
    }

    /// <summary>已开始执行的总次数（含重试），用于断言重试次数是否符合预期。</summary>
    public int AttemptCount => StartedTaskIds.Count;

    /// <inheritdoc />
    public bool CanHandle(DownloadTask task) => true;

    /// <inheritdoc />
    public async Task<DownloadResult> DownloadAsync(
        DownloadTask task,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        var nowRunning = Interlocked.Increment(ref _currentConcurrency);

        lock (_concurrencyGate)
        {
            if (nowRunning > MaxObservedConcurrency)
            {
                MaxObservedConcurrency = nowRunning;
            }
        }

        lock (_startedGate)
        {
            _startedTaskIds.Add(task.Id);
        }

        try
        {
            progress.Report(new DownloadProgress { Percent = 10, HasTotal = true });

            await Task.Delay(Delay, HonorCancellation ? cancellationToken : CancellationToken.None)
                .ConfigureAwait(false);

            progress.Report(new DownloadProgress { Percent = 100, HasTotal = true });

            return ResultFactory(task);
        }
        finally
        {
            Interlocked.Decrement(ref _currentConcurrency);
        }
    }
}
