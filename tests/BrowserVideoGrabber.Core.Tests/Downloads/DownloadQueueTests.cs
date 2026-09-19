/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Downloads
*文件名： DownloadQueueTests
*版本号： V1.0.0.0
*唯一标识：04d24ae0-8a81-4da1-b1c9-edd366123350
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:42:00
*描述：DownloadQueue 的单元测试，覆盖并发上限、状态流转、重试、取消、暂停恢复、任务移除与失败隔离。
*
*=================================================
*修改标记
*修改时间：2026/9/13 03:58:00
*修改人： yswenli
*版本号： V1.0.1.0
*描述：补充待下载 / 已暂停 / 运行中 / 终态四类任务的移除行为验证。
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Common;
using BrowserVideoGrabber.Core.Downloads;
using BrowserVideoGrabber.Core.Models;
using BrowserVideoGrabber.Tests.Fakes;

namespace BrowserVideoGrabber.Tests.Downloads;

/// <summary>
/// <see cref="DownloadQueue"/> 的行为验证。
/// </summary>
public sealed class DownloadQueueTests
{
    /// <summary>
    /// 队列必须严格遵守并发上限，否则会同时发起过多连接把带宽打满、也容易触发站点限流。
    /// </summary>
    [Fact]
    public async Task Should_NotExceedMaxConcurrency()
    {
        var handler = new FakeDownloadHandler { Delay = TimeSpan.FromMilliseconds(60) };
        using var queue = CreateQueue(handler, maxConcurrency: 3);

        for (var i = 0; i < 6; i++)
        {
            queue.Enqueue(CreateTask($"concurrency-{i}"));
        }

        queue.Start();
        await queue.WaitForCompletionAsync(TimeSpan.FromSeconds(10));

        Assert.True(handler.MaxObservedConcurrency <= 3, $"实际并发达到 {handler.MaxObservedConcurrency}，超过上限 3");
        Assert.Equal(6, queue.Tasks.Count(t => t.Status == DownloadStatus.Completed));
    }

    /// <summary>
    /// 下载成功后任务应进入已完成终态，并回填输出体积与完成时间。
    /// </summary>
    [Fact]
    public async Task Should_MarkTaskCompleted_OnSuccess()
    {
        var handler = new FakeDownloadHandler { Delay = TimeSpan.Zero };
        using var queue = CreateQueue(handler);
        var task = CreateTask("success");

        queue.Enqueue(task);
        queue.Start();
        await queue.WaitForCompletionAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(DownloadStatus.Completed, task.Status);
        Assert.Equal(1024, task.OutputBytes);
        Assert.NotNull(task.FinishedAt);
        Assert.Null(task.LastError);
    }

    /// <summary>
    /// 可重试的失败必须按 MaxRetryCount 重试，次数耗尽后落到失败终态。
    /// </summary>
    [Fact]
    public async Task Should_RetryUntilMaxRetryCount_ThenFail()
    {
        var attempts = 0;
        var handler = new FakeDownloadHandler
        {
            Delay = TimeSpan.Zero,
            ResultFactory = _ =>
            {
                Interlocked.Increment(ref attempts);
                return DownloadResult.Fail("网络中断");
            }
        };

        using var queue = CreateQueue(handler);
        var task = CreateTask("retry", maxRetryCount: 2);

        queue.Enqueue(task);
        queue.Start();
        await queue.WaitForCompletionAsync(TimeSpan.FromSeconds(10));

        // 首次 + 2 次重试 = 3 次尝试
        Assert.Equal(3, attempts);
        Assert.Equal(2, task.RetryCount);
        Assert.Equal(DownloadStatus.Failed, task.Status);
        Assert.Equal("网络中断", task.LastError);
    }

    /// <summary>
    /// 不可重试的失败（如 DRM 保护、404）必须立即落终态，不做无意义的重复请求。
    /// </summary>
    [Fact]
    public async Task Should_FailImmediately_WhenErrorIsNotRetryable()
    {
        var attempts = 0;
        var handler = new FakeDownloadHandler
        {
            Delay = TimeSpan.Zero,
            ResultFactory = _ =>
            {
                Interlocked.Increment(ref attempts);
                return DownloadResult.Fail("受保护内容，本工具不支持 DRM", isRetryable: false, isDrmProtected: true);
            }
        };

        using var queue = CreateQueue(handler);
        queue.Enqueue(CreateTask("drm"));
        queue.Start();
        await queue.WaitForCompletionAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, attempts);
        Assert.Equal(DownloadStatus.Failed, queue.Tasks[0].Status);
    }

    /// <summary>
    /// 取消运行中的任务后，任务必须落到已取消终态，并且不再占用并发槽位。
    /// </summary>
    [Fact]
    public async Task Should_CancelRunningTask()
    {
        var handler = new FakeDownloadHandler { Delay = TimeSpan.FromSeconds(10) };
        using var queue = CreateQueue(handler);
        var task = CreateTask("cancel");

        queue.Enqueue(task);
        queue.Start();

        await WaitUntilAsync(() => task.Status == DownloadStatus.Running, TimeSpan.FromSeconds(5));

        queue.Cancel(task.Id);
        await queue.WaitForCompletionAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(DownloadStatus.Canceled, task.Status);
        Assert.NotNull(task.FinishedAt);
    }

    /// <summary>
    /// 单个任务失败不得影响队列中的其它任务，否则一个坏链接会拖垮整批下载。
    /// </summary>
    [Fact]
    public async Task Should_IsolateFailuresBetweenTasks()
    {
        var badTask = CreateTask("bad");

        var handler = new FakeDownloadHandler
        {
            Delay = TimeSpan.FromMilliseconds(10),
            ResultFactory = task => task.Id == badTask.Id
                ? DownloadResult.Fail("403 Forbidden：缺少 Referer", isRetryable: false)
                : DownloadResult.Ok($"{task.Title}.mp4", 2048)
        };

        using var queue = CreateQueue(handler);
        var goodTask1 = CreateTask("good-1");
        var goodTask2 = CreateTask("good-2");

        queue.Enqueue(badTask);
        queue.Enqueue(goodTask1);
        queue.Enqueue(goodTask2);
        queue.Start();

        await queue.WaitForCompletionAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(DownloadStatus.Failed, badTask.Status);
        Assert.Equal(DownloadStatus.Completed, goodTask1.Status);
        Assert.Equal(DownloadStatus.Completed, goodTask2.Status);
    }

    /// <summary>
    /// 暂停后任务不得继续推进，恢复后应能重新开始并最终完成。
    /// </summary>
    [Fact]
    public async Task Should_PauseThenResumeTask()
    {
        var handler = new FakeDownloadHandler { Delay = TimeSpan.FromMilliseconds(400) };
        using var queue = CreateQueue(handler);
        var task = CreateTask("pause");

        queue.Enqueue(task);
        queue.Start();
        await WaitUntilAsync(() => task.Status == DownloadStatus.Running, TimeSpan.FromSeconds(5));

        queue.Pause(task.Id);
        Assert.Equal(DownloadStatus.Paused, task.Status);

        // 等待时间超过单次执行耗时，确认暂停期间确实没有跑完
        await Task.Delay(600);
        Assert.Equal(DownloadStatus.Paused, task.Status);

        queue.Resume(task.Id);
        Assert.Equal(DownloadStatus.Pending, task.Status);

        await queue.WaitForCompletionAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(DownloadStatus.Completed, task.Status);
    }

    /// <summary>
    /// 进度事件必须单调不减，否则界面进度条会出现倒退。
    /// </summary>
    [Fact]
    public async Task Should_ReportMonotonicProgress()
    {
        var handler = new FakeDownloadHandler { Delay = TimeSpan.FromMilliseconds(30) };
        using var queue = CreateQueue(handler);

        var percents = new List<double>();
        queue.TaskProgressChanged += (_, e) =>
        {
            lock (percents)
            {
                percents.Add(e.Progress.Percent);
            }
        };

        queue.Enqueue(CreateTask("progress"));
        queue.Start();
        await queue.WaitForCompletionAsync(TimeSpan.FromSeconds(5));

        Assert.NotEmpty(percents);
        for (var i = 1; i < percents.Count; i++)
        {
            Assert.True(percents[i] >= percents[i - 1], $"进度出现回退：{percents[i - 1]} -> {percents[i]}");
        }
    }

    /// <summary>
    /// 状态变更事件必须被触发，界面依赖它刷新三个页签。
    /// </summary>
    [Fact]
    public async Task Should_RaiseStateChangedEvent()
    {
        var handler = new FakeDownloadHandler { Delay = TimeSpan.Zero };
        using var queue = CreateQueue(handler);

        var observed = new List<DownloadStatus>();
        queue.TaskStateChanged += (_, e) =>
        {
            lock (observed)
            {
                observed.Add(e.Task.Status);
            }
        };

        queue.Enqueue(CreateTask("events"));
        queue.Start();
        await queue.WaitForCompletionAsync(TimeSpan.FromSeconds(5));

        lock (observed)
        {
            Assert.Contains(DownloadStatus.Running, observed);
            Assert.Contains(DownloadStatus.Completed, observed);
        }
    }

    /// <summary>
    /// 未调用 Start 时队列不得自行启动，保证调用方对时序有完全控制权。
    /// </summary>
    [Fact]
    public async Task Should_NotStartAutomatically()
    {
        var handler = new FakeDownloadHandler { Delay = TimeSpan.Zero };
        using var queue = CreateQueue(handler);

        var task = CreateTask("idle");
        queue.Enqueue(task);

        await Task.Delay(150);

        Assert.Equal(DownloadStatus.Pending, task.Status);
        Assert.Equal(0, handler.AttemptCount);
    }

    /// <summary>
    /// 待下载的任务必须可以被移除：加错地址或选错清晰度时，用户最需要的就是把这一条撤掉。
    /// </summary>
    [Fact]
    public async Task Should_RemovePendingTask_BeforeStart()
    {
        var handler = new FakeDownloadHandler { Delay = TimeSpan.Zero };
        using var queue = CreateQueue(handler);

        var task = CreateTask("removed");
        queue.Enqueue(task);

        Assert.True(queue.Remove(task.Id));
        Assert.Empty(queue.Tasks);

        queue.Start();
        await Task.Delay(150);

        Assert.Equal(0, handler.AttemptCount);
        Assert.Equal(DownloadStatus.Canceled, task.Status);
    }

    /// <summary>
    /// 等待并发槽位期间被移除的任务不得再被执行。
    /// </summary>
    /// <remarks>
    /// 这是移除待下载任务时最容易出错的地方：调度泵可能已经把该任务挑成「下一个要跑的」，
    /// 只是卡在并发槽位上。若移除时不做处理，用户会看到「任务已经从列表删掉，文件却开始下载了」。
    /// </remarks>
    [Fact]
    public async Task Should_NotStartTaskRemovedWhileWaitingForSlot()
    {
        var handler = new FakeDownloadHandler { Delay = TimeSpan.FromMilliseconds(200) };
        using var queue = CreateQueue(handler, maxConcurrency: 1);

        var running = CreateTask("running");
        var waiting = CreateTask("waiting");

        queue.Enqueue(running);
        queue.Enqueue(waiting);
        queue.Start();

        // 等第一个任务真正开跑：此时唯一槽位被占满，第二个任务必然滞留在待下载状态
        await WaitUntilAsync(() => running.Status == DownloadStatus.Running, TimeSpan.FromSeconds(5));

        Assert.True(queue.Remove(waiting.Id));
        Assert.DoesNotContain(queue.Tasks, task => task.Id == waiting.Id);

        await queue.WaitForCompletionAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, handler.AttemptCount);
        Assert.Equal(DownloadStatus.Completed, running.Status);
    }

    /// <summary>
    /// 已暂停的任务应可移除。
    /// </summary>
    [Fact]
    public async Task Should_RemovePausedTask()
    {
        var handler = new FakeDownloadHandler { Delay = TimeSpan.FromSeconds(5) };
        using var queue = CreateQueue(handler, maxConcurrency: 1);

        var task = CreateTask("paused");
        queue.Enqueue(task);
        queue.Start();

        await WaitUntilAsync(() => task.Status == DownloadStatus.Running, TimeSpan.FromSeconds(5));

        queue.Pause(task.Id);
        Assert.Equal(DownloadStatus.Paused, task.Status);

        Assert.True(queue.Remove(task.Id));
        Assert.Empty(queue.Tasks);
    }

    /// <summary>
    /// 正在下载的任务不得被移除。
    /// </summary>
    /// <remarks>
    /// 处理器仍在写盘，若允许直接移除，文件会继续增长而列表上已看不到这一行，
    /// 用户会误以为下载已经停止。此类任务必须先取消。
    /// </remarks>
    [Fact]
    public async Task Should_RefuseRemoveOfRunningTask()
    {
        var handler = new FakeDownloadHandler { Delay = TimeSpan.FromMilliseconds(300) };
        using var queue = CreateQueue(handler, maxConcurrency: 1);

        var task = CreateTask("running");
        queue.Enqueue(task);
        queue.Start();

        await WaitUntilAsync(() => task.Status == DownloadStatus.Running, TimeSpan.FromSeconds(5));

        Assert.False(queue.Remove(task.Id));
        Assert.Contains(queue.Tasks, item => item.Id == task.Id);

        await queue.WaitForCompletionAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(DownloadStatus.Completed, task.Status);
    }

    /// <summary>
    /// 终态任务仍可移除，且移除后不再出现在任务列表中。
    /// </summary>
    [Fact]
    public async Task Should_RemoveTerminalTask()
    {
        var handler = new FakeDownloadHandler { Delay = TimeSpan.Zero };
        using var queue = CreateQueue(handler);

        var task = CreateTask("done");
        queue.Enqueue(task);
        queue.Start();
        await queue.WaitForCompletionAsync(TimeSpan.FromSeconds(5));

        Assert.True(queue.Remove(task.Id));
        Assert.Empty(queue.Tasks);
    }

    /// <summary>
    /// 创建带默认配置的下载队列。
    /// </summary>
    /// <param name="handler">下载处理器假实现。</param>
    /// <param name="maxConcurrency">并发上限。</param>
    /// <returns>下载队列实例，由调用方负责释放。</returns>
    private static DownloadQueue CreateQueue(FakeDownloadHandler handler, int maxConcurrency = 3)
    {
        var factory = new DownloadHandlerFactory(new[] { (Core.Abstractions.IDownloadHandler)handler });
        var options = new DownloadQueueOptions
        {
            MaxConcurrency = maxConcurrency,
            PollInterval = TimeSpan.FromMilliseconds(5),
            // 测试中把退避间隔压到毫秒级，避免整个用例要等好几秒
            RetryPolicy = new RetryPolicy(3, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20))
        };

        return new DownloadQueue(factory, options);
    }

    /// <summary>
    /// 构造一个测试用下载任务。
    /// </summary>
    /// <param name="name">任务名称，用于生成地址与输出路径。</param>
    /// <param name="maxRetryCount">最大重试次数。</param>
    /// <returns>下载任务实例。</returns>
    private static DownloadTask CreateTask(string name, int maxRetryCount = 3)
        => new()
        {
            Url = $"https://a.com/{name}.m3u8",
            Format = VideoFormat.M3u8,
            OutputPath = $@"D:\out\{name}.mp4",
            Title = name,
            MaxRetryCount = maxRetryCount
        };

    /// <summary>
    /// 轮询等待条件成立。
    /// </summary>
    /// <param name="condition">等待条件。</param>
    /// <param name="timeout">超时时间。</param>
    /// <returns>表示异步等待的任务。</returns>
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"等待条件成立超时（{timeout.TotalSeconds:0.#} 秒）。");
    }
}
