/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.App.Binding
*文件名： DownloadListBinder
*版本号： V1.0.0.0
*唯一标识：5f2c8b14-9e7a-4d36-b8c2-4a1f6d3e7b95
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 02:03:00
*描述：把下载队列的后台事件桥接到下载列表面板的绑定器，含进度节流。
*
*=================================================
*修改标记
*修改时间：2026/9/13 02:03:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Collections.Concurrent;

using BrowserVideoGrabber.App;
using BrowserVideoGrabber.App.Panes;
using BrowserVideoGrabber.Core.Downloads;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.App.Binding;

/// <summary>
/// 下载队列到下载列表面板的绑定器。
/// </summary>
/// <remarks>
/// <para>
/// <b>两类事件走两条路径</b>：
/// <list type="bullet">
///   <item><description>
///     <b>状态变更</b>（低频，每个任务仅数次）：立即封送到 UI 线程刷新，
///     保证用户点下按钮后立刻看到行在页签之间移动。
///   </description></item>
///   <item><description>
///     <b>进度变更</b>（高频，原生下载器可达每秒数十次）：先写入并发字典，
///     再由 200 毫秒的定时器批量冲洗。这是「界面卡顿」这一风险项的直接对策 ——
///     若逐条封送，UI 线程会被海量 <c>BeginInvoke</c> 淹没而无法绘制。
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <b>为什么用字典而不是队列</b>：同一任务的旧进度在被冲洗前就已过时，
/// 按任务标识覆盖写入可以保证「只刷新最新值」，天然去重。
/// </para>
/// </remarks>
public sealed class DownloadListBinder : IDisposable
{
    /// <summary>进度刷新节流间隔。默认 200 毫秒。</summary>
    private const int ThrottleIntervalMilliseconds = 200;

    private readonly AppHost _host;
    private readonly DownloadPane _pane;
    private readonly ConcurrentDictionary<Guid, DownloadProgress> _pendingProgress = new();
    private readonly System.Windows.Forms.Timer _throttleTimer;

    private bool _disposed;

    /// <summary>
    /// 初始化绑定器并订阅队列事件。
    /// </summary>
    /// <param name="host">应用宿主，提供队列与再广播后的事件。</param>
    /// <param name="pane">目标面板。</param>
    public DownloadListBinder(AppHost host, DownloadPane pane)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _pane = pane ?? throw new ArgumentNullException(nameof(pane));

        _host.TaskStateChanged += OnTaskStateChanged;
        _host.TaskProgressChanged += OnTaskProgressChanged;

        _throttleTimer = new System.Windows.Forms.Timer { Interval = ThrottleIntervalMilliseconds };
        _throttleTimer.Tick += OnThrottleTick;
        _throttleTimer.Start();
    }

    /// <summary>
    /// 用队列中的完整任务列表重建面板（启动载入或设置变更后调用）。
    /// </summary>
    public void RefreshAll()
    {
        var tasks = _host.Queue.Tasks;
        Dispatch(() => _pane.Rebuild(tasks));
    }

    /// <summary>
    /// 从面板移除一个任务。
    /// </summary>
    /// <param name="taskId">任务标识。</param>
    /// <remarks>
    /// 队列的 <c>Remove</c> 不触发状态事件（任务已消失，无从通知），
    /// 因此这里需要由调用方显式同步一次界面。
    /// </remarks>
    public void RemoveTask(Guid taskId) => Dispatch(() => _pane.RemoveTask(taskId));

    /// <summary>
    /// 立即冲洗所有待处理的进度（用于关闭前确保界面状态与队列一致）。
    /// </summary>
    public void FlushProgress()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var taskId in _pendingProgress.Keys.ToList())
        {
            if (_pendingProgress.TryRemove(taskId, out var progress))
            {
                Dispatch(() => _pane.ApplyProgress(taskId, progress));
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _throttleTimer.Stop();
        _throttleTimer.Tick -= OnThrottleTick;
        _throttleTimer.Dispose();

        _host.TaskStateChanged -= OnTaskStateChanged;
        _host.TaskProgressChanged -= OnTaskProgressChanged;

        _pendingProgress.Clear();
    }

    /// <summary>
    /// 状态变更回调：立即封送到 UI 线程刷新。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnTaskStateChanged(object? sender, DownloadTaskEventArgs e) => Dispatch(() => _pane.ApplyState(e.Task));

    /// <summary>
    /// 进度变更回调：仅记录最新值，等待定时器批量冲洗。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnTaskProgressChanged(object? sender, DownloadProgressEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _pendingProgress[e.Task.Id] = e.Progress;
    }

    /// <summary>
    /// 节流定时器回调。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnThrottleTick(object? sender, EventArgs e)
    {
        if (_pendingProgress.IsEmpty)
        {
            return;
        }

        FlushProgress();
    }

    /// <summary>
    /// 把动作封送到 UI 线程执行。
    /// </summary>
    /// <param name="action">待执行动作。</param>
    private void Dispatch(Action action)
    {
        if (_disposed || _pane.IsDisposed)
        {
            return;
        }

        try
        {
            if (_pane.InvokeRequired)
            {
                if (!_pane.IsHandleCreated)
                {
                    return;
                }

                _pane.BeginInvoke(action);
                return;
            }

            action();
        }
        catch (ObjectDisposedException)
        {
            // 窗体正在关闭，忽略
        }
        catch (InvalidOperationException)
        {
            // 句柄尚未创建或已销毁，忽略
        }
    }
}
