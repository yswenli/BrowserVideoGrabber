/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.App.Panes
*文件名： DownloadPane
*版本号： V1.0.0.0
*唯一标识：270c4dcc-3e8f-4b61-a2d9-5c7f1b4e8a26
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 01:46:00
*描述：右下侧下载列表面板，以「正在下载 / 已下载」两个页签组织任务。
*
*=================================================
*修改标记
*修改时间：2026/9/13 01:46:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.App.Controls;
using BrowserVideoGrabber.App.Formatting;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.App.Panes;

/// <summary>
/// 下载列表面板。
/// </summary>
/// <remarks>
/// <para>
/// <b>状态到页签的映射</b>：六个任务状态归入两个页签 ——
/// 正在下载（等待中 / 下载中 / 已暂停）、已下载（已完成 / 失败 / 已取消）。
/// 等待中与已暂停本质上也是「排队中、迟早会下载」，和下载中放在同一页签更符合直觉，
/// 也避免界面在多数时候空着一个「待下载」标签。
/// 后三类都是终态，落在同一页签并用「结果」列区分，避免为失败与取消各开一个页签
/// 而让界面在多数时候空着两个标签。
/// </para>
/// <para>
/// <b>线程约定</b>：与 <c>SniffPane</c> 相同，所有公开方法都要求在 UI 线程调用，
/// 跨线程封送与节流由 <c>DownloadListBinder</c> 负责。
/// </para>
/// <para>
/// <b>行的复用</b>：同一任务在状态流转时会跨页签迁移，此处直接复用同一个
/// <see cref="ListViewItem"/> 实例并记录其当前宿主列表。
/// 若每次都新建行，正在下载页签上积累的进度信息会在迁移后丢失，用户会看到进度归零。
/// </para>
/// </remarks>
public sealed class DownloadPane : UserControl
{
    private readonly TabControl _tabs;
    private readonly BufferedListView _runningList;
    private readonly BufferedListView _finishedList;
    private readonly ToolStripLabel _summaryLabel;
    private readonly ContextMenuStrip _contextMenu;

    private readonly Dictionary<Guid, DownloadTask> _tasksById = new();
    private readonly Dictionary<Guid, ListViewItem> _itemsById = new();
    private readonly Dictionary<Guid, BufferedListView> _hostListById = new();
    private readonly Dictionary<Guid, DownloadProgress> _progressById = new();

    private readonly TabPage _runningPage;
    private readonly TabPage _finishedPage;

    /// <summary>
    /// 初始化下载列表面板。
    /// </summary>
    public DownloadPane()
    {
        BackColor = Color.FromArgb(250, 250, 250);

        _runningList = new BufferedListView { Dock = DockStyle.Fill };
        _runningList.AddColumns(("标题", 200), ("进度", 80), ("速度", 90), ("已下载", 130), ("地址", 360));

        _finishedList = new BufferedListView { Dock = DockStyle.Fill };
        _finishedList.AddColumns(("标题", 200), ("结果", 70), ("大小", 90), ("完成时间", 110), ("说明", 360));

        // 「正在下载」页签同时承载等待中 / 下载中 / 已暂停三种状态，
        // 这样被暂停或排队中的任务也能一眼看到，无需切换到另一个空标签
        _runningPage = new TabPage("正在下载 (0)");
        _runningPage.Controls.Add(_runningList);

        _finishedPage = new TabPage("已下载 (0)");
        _finishedPage.Controls.Add(_finishedList);

        _tabs = new TabControl { Dock = DockStyle.Fill };
        _tabs.TabPages.AddRange([_runningPage, _finishedPage]);

        _summaryLabel = new ToolStripLabel("0 个任务")
        {
            // 靠右对齐，让计数显示在工具栏右端而非紧贴「清空已完成」按钮
            Alignment = ToolStripItemAlignment.Right
        };

        var clearFinishedButton = new ToolStripButton
        {
            Text = "清空已完成",
            Image = CapsuleToolStripRenderer.CreateGlyphIcon("🗑"),
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            Margin = new Padding(4, 0, 4, 0),
            Padding = new Padding(6, 2, 6, 2),
            ToolTipText = "从列表中移除全部已完成、失败与已取消的任务"
        };
        clearFinishedButton.Click += (_, _) =>
            ActionRequested?.Invoke(this, new DownloadActionEventArgs { Action = DownloadAction.ClearFinished });

        var toolbar = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.Professional,
            Renderer = new CapsuleToolStripRenderer(),
            Dock = DockStyle.Top,
            Padding = new Padding(4, 2, 4, 2)
        };
        toolbar.Items.AddRange(
        [
            new ToolStripLabel("下载列表"),
            new ToolStripSeparator(),
            clearFinishedButton,
            _summaryLabel
        ]);

        _contextMenu = BuildContextMenu();

        Controls.Add(_tabs);
        Controls.Add(toolbar);

        // 列表与页签的右键都要能弹出菜单，否则只有点在行上才有反应
        foreach (var list in new[] { _runningList, _finishedList })
        {
            list.MouseUp += OnListMouseUp;
            list.DoubleClick += OnListDoubleClick;
        }

        _tabs.MouseUp += OnTabsMouseUp;

        UpdateSummary();
    }

    /// <summary>列表或页签上发起操作时触发。</summary>
    public event EventHandler<DownloadActionEventArgs>? ActionRequested;

    /// <summary>
    /// 用完整任务列表重建三个页签。
    /// </summary>
    /// <param name="tasks">任务快照。</param>
    /// <remarks>用于启动时载入历史任务，或设置变更后整体刷新。</remarks>
    public void Rebuild(IReadOnlyList<DownloadTask> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        _runningList.Items.Clear();
        _finishedList.Items.Clear();

        _tasksById.Clear();
        _itemsById.Clear();
        _hostListById.Clear();
        _progressById.Clear();

        foreach (var task in tasks)
        {
            ApplyState(task);
        }

        UpdateSummary();
    }

    /// <summary>
    /// 同步单个任务的状态：必要时创建行、跨页签迁移并刷新列。
    /// </summary>
    /// <param name="task">任务。</param>
    public void ApplyState(DownloadTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        _tasksById[task.Id] = task;

        var targetList = GetListView(task.Status);

        if (!_itemsById.TryGetValue(task.Id, out var item))
        {
            item = CreateItem(targetList);
            _itemsById[task.Id] = item;
            _hostListById[task.Id] = targetList;
            targetList.Items.Add(item);
        }
        else if (!ReferenceEquals(_hostListById[task.Id], targetList))
        {
            // 跨页签迁移：ListViewItem 同一时刻只能属于一个列表，必须先摘除再挂载。
            // 三个页签的列数不同，目标页签更宽时必须先补齐子项，否则多出来的那一列写不进去
            // （例如「已下载」页签的说明列会永远空白，用户看不到失败原因与输出路径）
            _hostListById[task.Id].Items.Remove(item);
            BufferedListView.EnsureSubItems(item, targetList);
            targetList.Items.Add(item);
            _hostListById[task.Id] = targetList;
        }

        FillItem(item, task, _progressById.TryGetValue(task.Id, out var progress) ? progress : null);

        // 回写任务引用：右键菜单与双击都靠行上的 Tag 还原任务，跨状态迁移后必须保持最新
        item.Tag = task;
        UpdateSummary();
    }

    /// <summary>
    /// 刷新单个任务的进度。
    /// </summary>
    /// <param name="taskId">任务标识。</param>
    /// <param name="progress">进度快照。</param>
    public void ApplyProgress(Guid taskId, DownloadProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        _progressById[taskId] = progress;

        // 任务可能已被移除，或已迁移到终态页签，此时无需刷新进度列
        if (!_itemsById.TryGetValue(taskId, out var item) || !_tasksById.TryGetValue(taskId, out var task))
        {
            return;
        }

        if (task.Status != DownloadStatus.Running)
        {
            return;
        }

        FillRunningColumns(item, task, progress);
    }

    /// <summary>
    /// 从列表中移除一个任务。
    /// </summary>
    /// <param name="taskId">任务标识。</param>
    /// <returns>移除成功返回 true。</returns>
    public bool RemoveTask(Guid taskId)
    {
        if (!_itemsById.TryGetValue(taskId, out var item))
        {
            return false;
        }

        _hostListById[taskId].Items.Remove(item);
        _hostListById.Remove(taskId);
        _itemsById.Remove(taskId);
        _tasksById.Remove(taskId);
        _progressById.Remove(taskId);

        UpdateSummary();
        return true;
    }

    /// <summary>
    /// 按任务状态选择应显示在哪个页签的列表。
    /// </summary>
    /// <param name="status">任务状态。</param>
    /// <returns>目标列表。</returns>
    private BufferedListView GetListView(DownloadStatus status) => status switch
    {
        // 下载中 / 等待中 / 已暂停都归入「正在下载」页签
        DownloadStatus.Running or DownloadStatus.Pending or DownloadStatus.Paused => _runningList,
        _ => _finishedList
    };

    /// <summary>
    /// 创建属于指定列表的空行。
    /// </summary>
    /// <param name="list">宿主列表。</param>
    /// <returns>空行实例。</returns>
    /// <remarks>子项数量必须一次补齐：WinForms 不会为新增的列自动补占位。</remarks>
    private static ListViewItem CreateItem(BufferedListView list)
    {
        var item = new ListViewItem(string.Empty);
        BufferedListView.EnsureSubItems(item, list);
        return item;
    }

    /// <summary>
    /// 按任务当前所属页签填充各列。
    /// </summary>
    /// <param name="item">目标行。</param>
    /// <param name="task">任务。</param>
    /// <param name="progress">最新进度；无则为 null。</param>
    private static void FillItem(ListViewItem item, DownloadTask task, DownloadProgress? progress)
    {
        BufferedListView.SetSubItemText(item, 0, ToTitle(task));

        switch (task.Status)
        {
            case DownloadStatus.Running:
                FillRunningColumns(item, task, progress);
                break;

            case DownloadStatus.Pending:
            case DownloadStatus.Paused:
                // 这两个状态现在和「下载中」共用同一页签（5 列），因此按该页签的列含义填充：
                // 进度列显示状态文字，速度/已下载留空，地址列放下载链接
                BufferedListView.SetSubItemText(item, 1, DisplayText.Status(task.Status));
                BufferedListView.SetSubItemText(item, 2, "-");
                BufferedListView.SetSubItemText(item, 3, "-");
                BufferedListView.SetSubItemText(item, 4, task.Url);
                break;

            default:
                var isPartial = task.IsPartial;
                BufferedListView.SetSubItemText(item, 1, isPartial ? "部分完成" : DisplayText.Status(task.Status));
                BufferedListView.SetSubItemText(item, 2, DisplayText.Size(task.OutputBytes));
                BufferedListView.SetSubItemText(item, 3, DisplayText.Time(task.FinishedAt));
                // 部分成功时「说明」列展示缺失时段，而非输出路径，让用户立刻知道产物不完整
                BufferedListView.SetSubItemText(item, 4, isPartial ? (task.PartialDetail ?? string.Empty) : ToResultDetail(task));
                break;
        }

        item.ToolTipText = task.Url;
    }

    /// <summary>
    /// 填充「正在下载」页签的进度相关列。
    /// </summary>
    /// <param name="item">目标行。</param>
    /// <param name="task">任务。</param>
    /// <param name="progress">最新进度；无则为 null。</param>
    private static void FillRunningColumns(ListViewItem item, DownloadTask task, DownloadProgress? progress)
    {
        var percent = progress?.Percent ?? task.LastProgressPercent;
        var hasTotal = progress?.HasTotal ?? false;

        BufferedListView.SetSubItemText(item, 1, DisplayText.Percent(percent, hasTotal));

        // 两条链路的速度表达方式不同：ffmpeg 报处理倍率，原生 HTTP 报字节速率
        BufferedListView.SetSubItemText(item, 2, FormatSpeed(progress));

        BufferedListView.SetSubItemText(item, 3, FormatDownloaded(progress, task));

        BufferedListView.SetSubItemText(item, 4, task.Url);
    }

    /// <summary>
    /// 格式化速度列。
    /// </summary>
    /// <param name="progress">进度快照；为 null 时返回 <c>-</c>。</param>
    /// <returns>
    /// ffmpeg 链路（有处理倍率）返回 <c>1.2x</c>；原生 HTTP 链路返回 <c>3.4 MB/s</c>；
    /// 都未知时返回 <c>-</c>。
    /// </returns>
    /// <remarks>
    /// 两条链路产出的进度字段互斥：ffmpeg 只填 <see cref="DownloadProgress.SpeedMultiplier"/>，
    /// 原生 HTTP 只填 <see cref="DownloadProgress.BytesPerSecond"/>，据此判断用哪种呈现。
    /// </remarks>
    private static string FormatSpeed(DownloadProgress? progress)
    {
        if (progress is null)
        {
            return "-";
        }

        if (progress.SpeedMultiplier > 0)
        {
            return $"{progress.SpeedMultiplier:0.0}x";
        }

        return DisplayText.Speed(progress.BytesPerSecond);
    }

    /// <summary>
    /// 格式化「已下载」列。
    /// </summary>
    /// <param name="progress">进度快照；可为 null。</param>
    /// <param name="task">任务，提供 progress 缺失时的字节快照。</param>
    /// <returns>
    /// ffmpeg 链路（有已处理时长）返回 <c>01:20 / 02:00</c>；
    /// 原生 HTTP 链路返回 <c>12.5 MB / 100 MB</c> 或 <c>12.5 MB / 未知</c>；
    /// 两者皆无时返回 <c>- / 未知</c>。
    /// </returns>
    /// <remarks>
    /// <para>
    /// progress 为 null 的情况真实存在：任务状态先变为 Running（触发 <see cref="ApplyState"/>），
    /// 而第一条进度回调尚未冲洗到界面。此时不应把已下载字节显示成 0，
    /// 而是回退到 <see cref="DownloadTask.DownloadedBytes"/> —— 队列在每次进度回调里都把
    /// <see cref="DownloadProgress.DownloadedBytes"/> 回写到了该字段，因此它是「最近一次」的可靠快照。
    /// </para>
    /// <para>
    /// ffmpeg 链路上报的是媒体处理时长而非字节数（<see cref="DownloadProgress.DownloadedBytes"/> 恒为 0），
    /// 硬套字节格式会把真实进度显示成 <c>- / 未知</c>，故优先识别 <see cref="DownloadProgress.Processed"/>。
    /// </para>
    /// </remarks>
    private static string FormatDownloaded(DownloadProgress? progress, DownloadTask task)
    {
        // ffmpeg 链路：用媒体时长表达进度
        if (progress?.Processed is { } processed)
        {
            var total = progress.Total;
            var totalText = total is { } totalValue
                ? DisplayText.Duration(totalValue.TotalSeconds)
                : "未知";
            return $"{DisplayText.Duration(processed.TotalSeconds)} / {totalText}";
        }

        // 原生 HTTP 链路：字节进度，progress 缺失时回退任务快照
        var downloaded = progress?.DownloadedBytes ?? task.DownloadedBytes;
        var totalText2 = progress?.TotalBytes is > 0
            ? DisplayText.Size(progress.TotalBytes.Value)
            : "未知";
        return $"{DisplayText.Size(downloaded)} / {totalText2}";
    }

    /// <summary>
    /// 生成终态任务的说明列文本。
    /// </summary>
    /// <param name="task">任务。</param>
    /// <returns>已完成时给出输出路径，失败/取消时给出原因。</returns>
    private static string ToResultDetail(DownloadTask task) => task.Status switch
    {
        DownloadStatus.Completed => task.OutputPath,
        DownloadStatus.Failed => string.IsNullOrWhiteSpace(task.LastError) ? "（无错误详情）" : task.LastError,
        DownloadStatus.Canceled => "用户已取消",
        _ => string.Empty
    };

    /// <summary>
    /// 生成任务标题。
    /// </summary>
    /// <param name="task">任务。</param>
    /// <returns>展示标题。</returns>
    private static string ToTitle(DownloadTask task)
        => string.IsNullOrWhiteSpace(task.Title) ? "（未命名）" : task.Title;

    /// <summary>
    /// 刷新页签标题与汇总标签上的计数。
    /// </summary>
    private void UpdateSummary()
    {
        _runningPage.Text = $"正在下载 ({_runningList.Items.Count})";
        _finishedPage.Text = $"已下载 ({_finishedList.Items.Count})";

        _summaryLabel.Text =
            $"{_runningList.Items.Count} 个下载中 · {_finishedList.Items.Count} 个已结束";
    }

    /// <summary>
    /// 构建右键菜单。
    /// </summary>
    /// <returns>右键菜单实例。</returns>
    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add(CreateMenuItem("暂停(&P)", DownloadAction.Pause));
        menu.Items.Add(CreateMenuItem("恢复(&R)", DownloadAction.Resume));
        menu.Items.Add(CreateMenuItem("取消(&C)", DownloadAction.Cancel));
        menu.Items.Add(CreateMenuItem("重试(&T)", DownloadAction.Retry));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateMenuItem("打开文件(&O)", DownloadAction.OpenFile));
        menu.Items.Add(CreateMenuItem("打开所在文件夹(&F)", DownloadAction.OpenFolder));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateMenuItem("复制下载地址(&U)", DownloadAction.CopyUrl));
        menu.Items.Add(CreateMenuItem("从列表移除(&X)", DownloadAction.Remove));

        // 弹出前按当前选中任务的状态裁剪可用项，避免用户点了没反应
        menu.Opening += OnContextMenuOpening;
        return menu;
    }

    /// <summary>
    /// 创建一个操作菜单项。
    /// </summary>
    /// <param name="text">显示文本。</param>
    /// <param name="action">对应的操作意图。</param>
    /// <returns>菜单项实例。</returns>
    private ToolStripMenuItem CreateMenuItem(string text, DownloadAction action)
    {
        var item = new ToolStripMenuItem(text) { Tag = action };
        item.Click += (_, _) => RaiseAction(action);
        return item;
    }

    /// <summary>
    /// 弹出菜单前根据选中任务的状态启用/禁用各菜单项。
    /// </summary>
    private void OnContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is not ContextMenuStrip menu)
        {
            return;
        }

        var task = GetSelectedTask();

        foreach (ToolStripItem item in menu.Items)
        {
            if (item is not ToolStripMenuItem menuItem || menuItem.Tag is not DownloadAction action)
            {
                continue;
            }

            menuItem.Enabled = IsActionAvailable(action, task);
        }
    }

    /// <summary>
    /// 判断某个操作在当前任务状态下是否可用。
    /// </summary>
    /// <param name="action">操作意图。</param>
    /// <param name="task">当前选中的任务；无选中时为 null。</param>
    /// <returns>可用返回 true。</returns>
    /// <remarks>
    /// 菜单项在不可用时会被禁用，因此这里的判定必须与队列侧的实际能力一致：
    /// 参数说明里出现「点了没反应」的项，比不显示这一项更让人困惑。
    /// </remarks>
    private static bool IsActionAvailable(DownloadAction action, DownloadTask? task)
    {
        if (task is null)
        {
            return action == DownloadAction.ClearFinished;
        }

        return action switch
        {
            // 下载中 / 等待中都可以暂停：排队中的任务也需要「先不跑了」的能力
            DownloadAction.Pause => task.Status is DownloadStatus.Running or DownloadStatus.Pending,
            DownloadAction.Resume => task.Status == DownloadStatus.Paused,
            DownloadAction.Cancel => task.Status is DownloadStatus.Running or DownloadStatus.Pending or DownloadStatus.Paused,
            DownloadAction.Retry => task.Status is DownloadStatus.Failed or DownloadStatus.Canceled,

            // 除「正在下载」外一律可移除：待下载与已暂停的任务尚未落盘，直接移除即可；
            // 运行中的任务必须先取消，否则文件仍在增长，抹掉这一行会让用户误以为下载已停止
            DownloadAction.Remove => task.Status != DownloadStatus.Running,

            DownloadAction.OpenFile => task.Status == DownloadStatus.Completed,
            DownloadAction.OpenFolder => task.Status == DownloadStatus.Completed,
            DownloadAction.CopyUrl => true,
            DownloadAction.ClearFinished => true,
            _ => false
        };
    }

    /// <summary>
    /// 取得当前选中的任务。
    /// </summary>
    /// <returns>选中任务；无选中时返回 null。</returns>
    private DownloadTask? GetSelectedTask()
    {
        var list = GetActiveList();
        if (list.SelectedItems.Count == 0)
        {
            return null;
        }

        return list.SelectedItems[0].Tag as DownloadTask;
    }

    /// <summary>
    /// 取得当前页签对应的列表。
    /// </summary>
    /// <returns>列表控件。</returns>
    private BufferedListView GetActiveList() => _tabs.SelectedIndex switch
    {
        1 => _finishedList,
        _ => _runningList
    };

    /// <summary>
    /// 发起一个针对当前选中任务的操作。
    /// </summary>
    /// <param name="action">操作意图。</param>
    private void RaiseAction(DownloadAction action)
    {
        var task = action == DownloadAction.ClearFinished ? null : GetSelectedTask();

        if (action != DownloadAction.ClearFinished && task is null)
        {
            return;
        }

        ActionRequested?.Invoke(this, new DownloadActionEventArgs { Task = task, Action = action });
    }

    /// <summary>
    /// 列表右键：选中行并弹出菜单。
    /// </summary>
    private void OnListMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || sender is not BufferedListView list)
        {
            return;
        }

        var item = list.GetItemAt(e.X, e.Y);
        if (item is null)
        {
            return;
        }

        item.Selected = true;
        _contextMenu.Show(list, e.Location);
    }

    /// <summary>
    /// 页签空白区右键：弹出菜单（此时多数项因无选中而禁用）。
    /// </summary>
    private void OnTabsMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
        {
            return;
        }

        _contextMenu.Show(_tabs, e.Location);
    }

    /// <summary>
    /// 列表双击：已完成的任务直接打开文件，其余不处理。
    /// </summary>
    private void OnListDoubleClick(object? sender, EventArgs e)
    {
        var task = GetSelectedTask();
        if (task is not null && task.Status == DownloadStatus.Completed)
        {
            ActionRequested?.Invoke(this, new DownloadActionEventArgs { Task = task, Action = DownloadAction.OpenFile });
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _contextMenu.Dispose();
            _tabs.Dispose();
        }

        base.Dispose(disposing);
    }
}
