/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Downloads
*文件名： DownloadQueue
*版本号： V1.0.0.0
*唯一标识：d40ad55c-8343-4fa9-8e07-bb449a1e19a0
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 23:08:00
*描述：下载队列调度器，负责并发控制、状态流转、失败重试、暂停恢复与取消清理。
*
*=================================================
*修改标记
*修改时间：2026/9/12 23:08:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Abstractions;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Core.Downloads;

/// <summary>
/// 下载队列：整个下载子系统的调度中枢。
/// </summary>
/// <remarks>
/// <para>
/// 本类是<b>唯一</b>允许改写 <see cref="DownloadTask.Status"/> 的位置，
/// 下载处理器只负责执行与上报进度。把状态机收口到一处，
/// 可以避免「谁改了状态」这类难以排查的并发问题。
/// </para>
/// <para>
/// 调度采用单泵轮询模型：一个后台任务持续寻找「待下载」任务，
/// 取得并发槽位后以「即发即忘」方式启动执行，执行完毕后释放槽位。
/// 选择轮询而非信号量唤醒，是为了让新入队、失败重试、暂停恢复三种触发源
/// 共用同一条判定路径，逻辑上不会出现漏唤醒。
/// </para>
/// <para>
/// <b>暂停的语义</b>：暂停会取消当前处理器（ffmpeg 进程树随之终止），恢复时从头重新执行。
/// 这是因为 HLS / DASH 的下载进度由 ffmpeg 内部维护、无法从外部断点续传；
/// 原生 MP4 下载器则通过 <c>.part</c> 文件自行支持断点续传。
/// </para>
/// </remarks>
public sealed class DownloadQueue : IDisposable
{
    private readonly DownloadHandlerFactory _factory;
    private readonly DownloadQueueOptions _options;
    private readonly ITaskRepository? _repository;

    /// <summary>保护任务列表、取消源字典与退避时间字典的统一锁。</summary>
    private readonly object _gate = new();

    private readonly List<DownloadTask> _tasks = new();

    /// <summary>正在运行任务对应的取消源。用于实现暂停与取消。</summary>
    private readonly Dictionary<Guid, CancellationTokenSource> _cancellations = new();

    /// <summary>重试任务的解禁时间。早于该时间的任务不会被调度器拾取。</summary>
    private readonly Dictionary<Guid, DateTimeOffset> _retryNotBefore = new();

    private readonly SemaphoreSlim _slots;

    private CancellationTokenSource? _lifetimeCts;
    private Task? _pump;
    private bool _disposed;

    /// <summary>
    /// 任务状态发生变更时触发。
    /// </summary>
    /// <remarks>事件在后台线程触发，界面层必须切换到 UI 线程后再刷新控件。</remarks>
    public event EventHandler<DownloadTaskEventArgs>? TaskStateChanged;

    /// <summary>
    /// 任务进度更新时触发。
    /// </summary>
    /// <remarks>同一任务的进度按发生顺序触发且单调不减；事件在后台线程触发。</remarks>
    public event EventHandler<DownloadProgressEventArgs>? TaskProgressChanged;

    /// <summary>
    /// 初始化下载队列。
    /// </summary>
    /// <param name="factory">下载处理器工厂。</param>
    /// <param name="options">队列配置。为空时使用默认配置。</param>
    /// <param name="repository">任务持久化仓储。为空表示不做持久化。</param>
    /// <exception cref="ArgumentNullException">工厂为 null 时抛出。</exception>
    public DownloadQueue(
        DownloadHandlerFactory factory,
        DownloadQueueOptions? options = null,
        ITaskRepository? repository = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _options = options ?? new DownloadQueueOptions();
        _repository = repository;

        // 并发上限至少为 1，否则信号量会永久阻塞，任务永远不会开始
        if (_options.MaxConcurrency < 1)
        {
            _options.MaxConcurrency = 1;
        }

        _slots = new SemaphoreSlim(_options.MaxConcurrency, _options.MaxConcurrency);
    }

    /// <summary>并发下载数上限。</summary>
    public int MaxConcurrency => _options.MaxConcurrency;

    /// <summary>
    /// 当前任务列表快照。
    /// </summary>
    /// <remarks>返回副本而非内部集合，避免界面遍历时被后台线程修改而抛出异常。</remarks>
    public IReadOnlyList<DownloadTask> Tasks
    {
        get
        {
            lock (_gate)
            {
                return _tasks.ToList();
            }
        }
    }

    /// <summary>
    /// 载入持久化的历史任务。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步载入的任务。</returns>
    /// <remarks>
    /// 上次退出时处于「正在下载」的任务会被降级为「待下载」：
    /// 进程已不存在，若保持运行态会让界面永久卡在「正在下载」页签。
    /// </remarks>
    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_repository is null)
        {
            return;
        }

        var loaded = await _repository.LoadAsync(cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            foreach (var task in loaded)
            {
                if (task.Status == DownloadStatus.Running)
                {
                    task.Status = DownloadStatus.Pending;
                }

                _tasks.Add(task);
            }
        }
    }

    /// <summary>
    /// 把当前任务列表落盘。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步写入的任务。</returns>
    public async Task PersistAsync(CancellationToken cancellationToken = default)
    {
        if (_repository is null)
        {
            return;
        }

        await _repository.SaveAsync(Tasks, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 把一个已构造好的任务加入队列。
    /// </summary>
    /// <param name="task">下载任务。</param>
    /// <returns>入队后的同一个任务实例，便于调用方继续操作其状态。</returns>
    /// <remarks>本方法<b>不会</b>自动启动调度，需显式调用 <see cref="Start"/>。</remarks>
    public DownloadTask Enqueue(DownloadTask task)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(task);

        lock (_gate)
        {
            _tasks.Add(task);
        }

        NotifyStateChanged(task, task.Status);
        return task;
    }

    /// <summary>
    /// 由嗅探结果创建并加入队列。
    /// </summary>
    /// <param name="video">嗅探到的视频资源。</param>
    /// <param name="outputPath">输出文件完整路径。</param>
    /// <returns>入队后的下载任务实例。</returns>
    public DownloadTask Enqueue(SniffedVideo video, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(video);

        return Enqueue(new DownloadTask
        {
            Url = video.Url,
            Format = video.Format,
            OutputPath = outputPath,
            Title = video.DisplayTitle
        });
    }

    /// <summary>
    /// 启动调度泵。重复调用是安全的。
    /// </summary>
    public void Start()
    {
        ThrowIfDisposed();

        lock (_gate)
        {
            if (_lifetimeCts is null || _lifetimeCts.IsCancellationRequested)
            {
                _lifetimeCts = new CancellationTokenSource();
            }

            if (_pump is null || _pump.IsCompleted)
            {
                var token = _lifetimeCts.Token;
                _pump = Task.Run(() => PumpAsync(token));
            }
        }
    }

    /// <summary>
    /// 暂停指定任务。
    /// </summary>
    /// <param name="taskId">任务标识。</param>
    /// <remarks>已处于终态的任务不受影响；暂停会终止其底层处理器（含 ffmpeg 进程树）。</remarks>
    public void Pause(Guid taskId)
    {
        DownloadTask? task;
        DownloadStatus previousStatus;
        CancellationTokenSource? cancellation = null;

        lock (_gate)
        {
            task = FindTask(taskId);
            if (task is null || IsTerminal(task.Status))
            {
                return;
            }

            previousStatus = task.Status;
            task.Status = DownloadStatus.Paused;
            _cancellations.TryGetValue(taskId, out cancellation);
        }

        NotifyStateChanged(task, previousStatus);

        // 取消动作必须放在锁外：取消回调可能反向申请锁，锁内取消存在死锁风险
        CancelSafely(cancellation);
    }

    /// <summary>
    /// 恢复指定任务。
    /// </summary>
    /// <param name="taskId">任务标识。</param>
    /// <remarks>恢复会从头重新执行下载（详见类型注释中关于暂停语义的说明）。</remarks>
    public void Resume(Guid taskId)
    {
        DownloadTask? task;

        lock (_gate)
        {
            task = FindTask(taskId);
            if (task is null || task.Status != DownloadStatus.Paused)
            {
                return;
            }

            task.Status = DownloadStatus.Pending;
            _retryNotBefore.Remove(taskId);
        }

        // 从 Paused → Pending，previousStatus 是 Paused
        NotifyStateChanged(task, DownloadStatus.Paused);

        // 调度泵可能已因「全部落定」而退出，恢复后需要重新拉起
        Start();
    }

    /// <summary>
    /// 取消指定任务。
    /// </summary>
    /// <param name="taskId">任务标识。</param>
    /// <remarks>已处于终态的任务不受影响。取消会终止底层处理器（含 ffmpeg 进程树）。</remarks>
    public void Cancel(Guid taskId)
    {
        DownloadTask? task;
        DownloadStatus previousStatus;
        CancellationTokenSource? cancellation = null;

        lock (_gate)
        {
            task = FindTask(taskId);
            if (task is null || IsTerminal(task.Status))
            {
                return;
            }

            previousStatus = task.Status;
            task.Status = DownloadStatus.Canceled;
            task.FinishedAt = DateTimeOffset.Now;
            _cancellations.TryGetValue(taskId, out cancellation);
            _retryNotBefore.Remove(taskId);
        }

        NotifyStateChanged(task, previousStatus);
        CancelSafely(cancellation);
    }

    /// <summary>
    /// 从队列中移除指定任务。
    /// </summary>
    /// <param name="taskId">任务标识。</param>
    /// <returns>移除成功返回 true；任务不存在或正在下载而无法安全移除时返回 false。</returns>
    /// <remarks>
    /// <para>
    /// <b>为什么允许移除尚未开始的任务</b>：用户加错地址、或在整档清晰度里选错了条目，
    /// 最需要的操作就是「把这一条撤掉」。早期实现只允许移除终态任务，
    /// 待下载页签上的任务因此无法删除，只能先取消再删，是明显的可用性缺陷。
    /// </para>
    /// <para>
    /// <b>为什么运行中的任务不允许移除</b>：处理器正在写盘，直接把行抹掉会让用户误以为下载已停止，
    /// 而实际上文件仍在增长。此类任务必须先取消 —— 取消会终止 ffmpeg 进程树并清理临时文件。
    /// </para>
    /// <para>
    /// 本方法不触发状态变更事件：任务已从队列消失，任何后续事件都会让界面把这一行重新建出来。
    /// 因此调用方需要自行同步界面，并在必要时安排一次任务列表落盘。
    /// </para>
    /// </remarks>
    public bool Remove(Guid taskId)
    {
        lock (_gate)
        {
            var task = FindTask(taskId);
            if (task is null || task.Status == DownloadStatus.Running)
            {
                return false;
            }

            // 尚未开始的任务可能已被调度泵选中、正在等待并发槽位。
            // 先把它置为取消终态，泵在认领时校验状态就会失败并归还槽位，
            // 否则会出现「任务已从列表移除却仍然被启动」的诡异情况。
            if (task.Status is DownloadStatus.Pending or DownloadStatus.Paused)
            {
                task.Status = DownloadStatus.Canceled;
                task.FinishedAt ??= DateTimeOffset.Now;
            }

            _retryNotBefore.Remove(taskId);
            return _tasks.Remove(task);
        }
    }

    /// <summary>
    /// 清空所有已处于终态的任务。
    /// </summary>
    /// <returns>被移除的任务数量。</returns>
    public int ClearFinished()
    {
        lock (_gate)
        {
            var removed = _tasks.RemoveAll(t => IsTerminal(t.Status));
            if (removed > 0)
            {
                // 同步清理退避记录，避免字典随任务反复增删而无界增长
                foreach (var key in _retryNotBefore.Keys.Where(key => _tasks.All(t => t.Id != key)).ToList())
                {
                    _retryNotBefore.Remove(key);
                }
            }

            return removed;
        }
    }

    /// <summary>
    /// 等待队列中所有任务落定（完成 / 失败 / 取消）。
    /// </summary>
    /// <param name="timeout">等待超时。为空表示一直等待。</param>
    /// <returns>表示异步等待的任务。</returns>
    /// <exception cref="TimeoutException">超时仍未落定时抛出。</exception>
    /// <remarks>处于暂停状态的任务不算落定，会导致本方法一直等待；调用方需自行避免该情况。</remarks>
    public async Task WaitForCompletionAsync(TimeSpan? timeout = null)
    {
        var deadline = timeout.HasValue ? DateTimeOffset.Now + timeout.Value : DateTimeOffset.MaxValue;

        while (true)
        {
            bool settled;

            lock (_gate)
            {
                settled = _tasks.All(t => t.Status != DownloadStatus.Pending && t.Status != DownloadStatus.Running);
            }

            if (settled)
            {
                return;
            }

            if (DateTimeOffset.Now >= deadline)
            {
                throw new TimeoutException("等待下载队列全部落定超时。");
            }

            await Task.Delay(_options.PollInterval).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 释放队列资源：取消全部正在进行的工作。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        List<CancellationTokenSource> pendingCancellations;

        lock (_gate)
        {
            _lifetimeCts?.Cancel();
            pendingCancellations = _cancellations.Values.ToList();
            _cancellations.Clear();
        }

        foreach (var cancellation in pendingCancellations)
        {
            CancelSafely(cancellation);
        }

        _lifetimeCts?.Dispose();
        _lifetimeCts = null;

        // 刻意不释放 _slots：调度泵可能正阻塞在 WaitAsync 上，
        // 释放信号量会令其抛出 ObjectDisposedException；SemaphoreSlim 未创建等待句柄时由 GC 回收即可。
    }

    /// <summary>
    /// 调度泵主循环。
    /// </summary>
    /// <param name="cancellationToken">队列生命周期取消令牌。</param>
    /// <returns>表示调度循环的任务。</returns>
    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                DownloadTask? next = null;
                var allSettled = true;

                lock (_gate)
                {
                    var now = DateTimeOffset.Now;

                    foreach (var task in _tasks)
                    {
                        if (task.Status == DownloadStatus.Running)
                        {
                            allSettled = false;
                            continue;
                        }

                        if (task.Status != DownloadStatus.Pending)
                        {
                            continue;
                        }

                        allSettled = false;

                        // 处于退避等待窗口内的任务本轮跳过
                        if (_retryNotBefore.TryGetValue(task.Id, out var notBefore) && notBefore > now)
                        {
                            continue;
                        }

                        next = task;
                        break;
                    }
                }

                // 已无待处理任务且无运行中任务，调度泵退出，等下次 Start 再拉起
                if (allSettled)
                {
                    return;
                }

                if (next is null)
                {
                    await Task.Delay(_options.PollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // 先占槽位再认领任务，确保同时在运行的任务数不超过上限
                await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);

                var claimed = false;

                lock (_gate)
                {
                    if (next.Status == DownloadStatus.Pending)
                    {
                        next.Status = DownloadStatus.Running;
                        claimed = true;
                    }
                }

                if (!claimed)
                {
                    // 任务在等待槽位期间被暂停或取消，归还槽位即可
                    ReleaseSlot();
                    continue;
                }

                NotifyStateChanged(next, DownloadStatus.Pending);

                // 即发即忘：执行体自行负责槽位归还与状态改写
                _ = RunTaskAsync(next, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // 队列停止属于正常流程
        }
        catch (ObjectDisposedException)
        {
            // 释放过程中退出，属正常情况
        }
    }

    /// <summary>
    /// 执行单个任务并在结束后归还并发槽位。
    /// </summary>
    /// <param name="task">待执行任务（已被置为运行中）。</param>
    /// <param name="lifetimeToken">队列生命周期取消令牌。</param>
    /// <returns>表示任务执行的任务。</returns>
    private async Task RunTaskAsync(DownloadTask task, CancellationToken lifetimeToken)
    {
        CancellationTokenSource? taskCancellation = null;

        try
        {
            IDownloadHandler handler;

            try
            {
                handler = _factory.Resolve(task);
            }
            catch (Exception exception)
            {
                // 没有处理器能处理该格式，属于配置性错误，重试也不会成功
                MarkFailed(task, exception.Message, isRetryable: false);
                return;
            }

            taskCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);

            lock (_gate)
            {
                _cancellations[task.Id] = taskCancellation;
            }

            // 使用同步进度桥接而非框架自带的 Progress<T>：
            // Progress<T> 会把回调投递到线程池，导致进度事件乱序、可能落到状态变更之后
            var progress = new SynchronousProgress<DownloadProgress>(value => OnProgressReported(task, value));

            var result = await handler.DownloadAsync(task, progress, taskCancellation.Token).ConfigureAwait(false);

            if (result.Success)
            {
                lock (_gate)
                {
                    task.Status = DownloadStatus.Completed;
                    task.OutputBytes = result.OutputBytes;
                    task.IsPartial = result.IsPartial;
                    task.PartialDetail = result.PartialDetail;
                    task.LastError = null;
                    task.FinishedAt = DateTimeOffset.Now;
                }

                // 从 Running → Completed
                NotifyStateChanged(task, DownloadStatus.Running);
                return;
            }

            MarkFailed(task, result.Error ?? "未知错误", result.IsRetryable);
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                // 暂停语义：状态已被置为暂停，此处保持不动，等待用户恢复
                if (task.Status != DownloadStatus.Paused && !IsTerminal(task.Status))
                {
                    task.Status = DownloadStatus.Canceled;
                    task.FinishedAt = DateTimeOffset.Now;
                }
            }

            // 取消前的状态必然是 Running（本方法开始时就会被 PumpAsync 设为 Running），
            // 暂停/取消操作虽可能先改状态，但事件语义上都以 "从运行中中断" 表达
            NotifyStateChanged(task, DownloadStatus.Running);
        }
        catch (Exception exception)
        {
            MarkFailed(task, exception.Message, isRetryable: true);
        }
        finally
        {
            if (taskCancellation is not null)
            {
                lock (_gate)
                {
                    _cancellations.Remove(task.Id);
                }

                taskCancellation.Dispose();
            }

            ReleaseSlot();
        }
    }

    /// <summary>
    /// 标记任务失败：在重试额度内则回到待下载并设置退避时间，否则落入失败终态。
    /// </summary>
    /// <param name="task">失败的任务。</param>
    /// <param name="error">失败原因（中文描述）。</param>
    /// <param name="isRetryable">该错误是否值得重试。</param>
    private void MarkFailed(DownloadTask task, string error, bool isRetryable)
    {
        lock (_gate)
        {
            task.LastError = error;

            // 双重约束：既要没超过任务自身的重试上限，也要满足队列级策略的尝试次数限制
            if (isRetryable
                && task.RetryCount < task.MaxRetryCount
                && _options.RetryPolicy.ShouldRetry(task.RetryCount + 1))
            {
                task.RetryCount++;
                task.Status = DownloadStatus.Pending;

                // 指数退避：拉长重试间隔以避开瞬时限流与 CDN 抖动
                _retryNotBefore[task.Id] = DateTimeOffset.Now + _options.RetryPolicy.GetDelay(task.RetryCount);
            }
            else
            {
                task.Status = DownloadStatus.Failed;
                task.FinishedAt = DateTimeOffset.Now;
                _retryNotBefore.Remove(task.Id);
            }
        }

        NotifyStateChanged(task, DownloadStatus.Running);
    }

    /// <summary>
    /// 上报进度：更新任务上的最近进度并转发事件。
    /// </summary>
    /// <param name="task">产生进度的任务。</param>
    /// <param name="progress">进度快照。</param>
    private void OnProgressReported(DownloadTask task, DownloadProgress progress)
    {
        // 已下载字节是「下载中」的进度快照，写入 DownloadedBytes 而非 OutputBytes：
        // 后者是终态回填的成品大小，若在此处被覆盖，失败任务的「大小」列会显示残缺的部分字节
        if (progress.DownloadedBytes > 0)
        {
            task.DownloadedBytes = progress.DownloadedBytes;
        }

        // 进度百分比允许在重试时回退（新一轮从 0 开始），因此这里不做单调性裁剪，
        // 单调性由「同一轮执行内」的处理器保证
        task.LastProgressPercent = progress.Percent;

        var handler = TaskProgressChanged;
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(this, new DownloadProgressEventArgs { Task = task, Progress = progress });
        }
        catch
        {
            // 订阅方异常不得影响下载主流程
        }
    }

    /// <summary>
    /// 触发状态变更事件。
    /// </summary>
    /// <param name="task">状态变更的任务。</param>
    /// <param name="previousStatus">变更前的状态。</param>
    private void NotifyStateChanged(DownloadTask task, DownloadStatus previousStatus)
    {
        var handler = TaskStateChanged;
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(this, new DownloadTaskEventArgs { Task = task, PreviousStatus = previousStatus });
        }
        catch
        {
            // 订阅方异常不得影响下载主流程
        }
    }

    /// <summary>
    /// 在锁内查找任务。
    /// </summary>
    /// <param name="taskId">任务标识。</param>
    /// <returns>任务；不存在时返回 null。</returns>
    private DownloadTask? FindTask(Guid taskId)
        => _tasks.FirstOrDefault(t => t.Id == taskId);

    /// <summary>
    /// 判断状态是否为终态。
    /// </summary>
    /// <param name="status">待判断状态。</param>
    /// <returns>终态返回 true。</returns>
    private static bool IsTerminal(DownloadStatus status)
        => status is DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Canceled;

    /// <summary>
    /// 安全地归还一个并发槽位。
    /// </summary>
    private void ReleaseSlot()
    {
        try
        {
            _slots.Release();
        }
        catch (ObjectDisposedException)
        {
            // 队列已释放，忽略
        }
        catch (SemaphoreFullException)
        {
            // 重复归还属于实现缺陷，但不应让下载流程崩溃
        }
    }

    /// <summary>
    /// 安全地触发取消。
    /// </summary>
    /// <param name="cancellation">取消源，允许为 null。</param>
    private static void CancelSafely(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 任务已结束并释放了取消源，无需处理
        }
        catch (AggregateException)
        {
            // 取消回调抛出的异常不应向上传播
        }
    }

    /// <summary>
    /// 校验对象是否已释放。
    /// </summary>
    /// <exception cref="ObjectDisposedException">对象已释放时抛出。</exception>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// 同步执行的进度桥接器。
    /// </summary>
    /// <typeparam name="T">进度数据类型。</typeparam>
    /// <remarks>
    /// 框架自带的 <see cref="Progress{T}"/> 会把回调投递到捕获的同步上下文或线程池，
    /// 在无 UI 上下文的场景下会造成事件乱序（进度事件晚于状态变更事件），
    /// 且测试无法确定性断言。改为同步调用可以彻底消除这两个问题。
    /// </remarks>
    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _callback;

        /// <summary>
        /// 初始化同步进度桥接器。
        /// </summary>
        /// <param name="callback">进度回调。</param>
        public SynchronousProgress(Action<T> callback) => _callback = callback;

        /// <inheritdoc />
        public void Report(T value) => _callback(value);
    }
}
