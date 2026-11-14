/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Panes
*文件名： DownloadPaneTests
*版本号： V1.0.0.0
*唯一标识：8c41d2e9-7a63-4f5b-9d02-b5e8c1a7f346
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/11/15 13:00:00
*描述：下载列表面板的双列表列定义、布局路由、排序插入与跨页签迁移回归测试。
*
*=================================================
*修改标记
*修改时间：2026/11/15 13:00:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;

using BrowserVideoGrabber.App.Controls;
using BrowserVideoGrabber.App.Formatting;
using BrowserVideoGrabber.App.Panes;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Tests.Panes;

public sealed class DownloadPaneTests
{
    [Fact]
    public void Constructor_DefinesColumnsForBothTabs()
    {
        RunStaWithForm(form =>
        {
            using var pane = new DownloadPane();
            form.Controls.Add(pane);

            var lists = ListsOf(pane);
            Assert.Equal(2, lists.Length);

            var running = ColumnsOf(lists[0]);
            Assert.Equal(new[] { "标题", "进度", "速度", "已下载", "地址" }, running.Select(c => c.Text));
            Assert.Equal(new[] { 200, 80, 90, 130, 360 }, running.Select(c => c.Width));

            var finished = ColumnsOf(lists[1]);
            Assert.Equal(new[] { "标题", "结果", "大小", "完成时间", "说明" }, finished.Select(c => c.Text));
            Assert.Equal(new[] { 200, 70, 90, 110, 360 }, finished.Select(c => c.Width));

            // 页签顺序决定「第一个列表」与「第二个列表」的语义，错误顺序会让布局键错配
            var tabs = pane.Controls.OfType<TabControl>().Single();
            Assert.StartsWith("正在下载", tabs.TabPages[0].Text);
            Assert.StartsWith("已下载", tabs.TabPages[1].Text);
        });
    }

    [Fact]
    public void ApplyColumnLayout_RoutesByLayoutKey()
    {
        RunStaWithForm(form =>
        {
            using var pane = new DownloadPane();
            form.Controls.Add(pane);

            pane.ApplyColumnLayout(
                DownloadPane.RunningLayoutKey,
                Layout(("地址", 300), ("标题", 180)));

            var running = pane.GetColumnLayout(DownloadPane.RunningLayoutKey)!;
            var finished = pane.GetColumnLayout(DownloadPane.FinishedLayoutKey)!;

            // 只有第一个列表被改：键路由串台会让两个页签互相覆盖布局
            Assert.Equal("地址", running.Columns[0].Name);
            Assert.Equal(300, running.Columns[0].Width);
            Assert.Equal("标题", running.Columns[1].Name);
            Assert.Equal(180, running.Columns[1].Width);

            Assert.Equal("标题", finished.Columns[0].Name);
            Assert.Equal(200, finished.Columns[0].Width);

            pane.ApplyColumnLayout(
                DownloadPane.FinishedLayoutKey,
                Layout(("说明", 420), ("标题", 150)));

            var runningAfter = pane.GetColumnLayout(DownloadPane.RunningLayoutKey)!;
            var finishedAfter = pane.GetColumnLayout(DownloadPane.FinishedLayoutKey)!;

            Assert.Equal("说明", finishedAfter.Columns[0].Name);
            Assert.Equal(420, finishedAfter.Columns[0].Width);
            Assert.Equal("标题", finishedAfter.Columns[1].Name);
            Assert.Equal(150, finishedAfter.Columns[1].Width);

            // 第二个列表的布局不得波及第一个列表
            Assert.Equal("地址", runningAfter.Columns[0].Name);
            Assert.Equal(300, runningAfter.Columns[0].Width);
        });
    }

    [Fact]
    public void ApplyColumnLayout_UnknownKey_IsIgnored()
    {
        RunStaWithForm(form =>
        {
            using var pane = new DownloadPane();
            form.Controls.Add(pane);

            var before = pane.GetColumnLayout(DownloadPane.RunningLayoutKey)!;

            // 未知键必须静默忽略：配置文件里可能残留已删除页签的键，绝不能因此让启动崩溃
            var exception = Record.Exception(() =>
                pane.ApplyColumnLayout("nope", Layout(("地址", 300))));
            Assert.Null(exception);

            Assert.Null(pane.GetColumnLayout("nope"));

            var after = pane.GetColumnLayout(DownloadPane.RunningLayoutKey)!;
            Assert.Equal(before.Columns.Select(c => c.Name), after.Columns.Select(c => c.Name));
            Assert.Equal(before.Columns.Select(c => c.Width), after.Columns.Select(c => c.Width));
        });
    }

    [Fact]
    public void LayoutChanged_ForwardsRunningKey()
    {
        RunStaWithForm(form =>
        {
            using var pane = new DownloadPane();
            form.Controls.Add(pane);
            var list = ListsOf(pane)[0];

            var received = new List<ColumnLayoutChangedEventArgs>();
            pane.LayoutChanged += (_, e) => received.Add(e);

            list.Columns[0].Width = 300;

            var args = Assert.Single(received);
            Assert.Equal(DownloadPane.RunningLayoutKey, args.Key);
            Assert.Equal("download-running", args.Key);
            Assert.Equal(300, args.Layout.Columns[0].Width);
        });
    }

    [Fact]
    public void LayoutChanged_ForwardsFinishedKey()
    {
        RunStaWithForm(form =>
        {
            using var pane = new DownloadPane();
            form.Controls.Add(pane);

            // 「已下载」在未选中的页签上，句柄不创建就不会有原生的列宽变化事件
            var list = ActivateTab(pane, 1);
            Assert.True(list.IsHandleCreated, "布局事件测试需要有句柄的列表");

            var received = new List<ColumnLayoutChangedEventArgs>();
            pane.LayoutChanged += (_, e) => received.Add(e);

            list.Columns[2].Width = 150;

            var args = Assert.Single(received);
            Assert.Equal(DownloadPane.FinishedLayoutKey, args.Key);
            Assert.Equal("download-finished", args.Key);
            Assert.Equal(150, args.Layout.Columns[2].Width);
        });
    }

    [Fact]
    public void ApplyColumnLayout_RaisesNoLayoutChanged()
    {
        RunStaWithForm(form =>
        {
            using var pane = new DownloadPane();
            form.Controls.Add(pane);

            var count = 0;
            pane.LayoutChanged += (_, _) => count++;

            pane.ApplyColumnLayout(
                DownloadPane.RunningLayoutKey,
                Layout(("地址", 320), ("标题", 190)));
            pane.ApplyColumnLayout(
                DownloadPane.FinishedLayoutKey,
                Layout(("说明", 400), ("标题", 160)));

            // 程序化应用布局必须静默，否则会立刻回写一份「刚读出来的布局」
            Assert.Equal(0, count);
        });
    }

    [Fact]
    public void ApplyState_WhenUnsorted_InsertsNewItemAtTop()
    {
        RunStaWithForm(form =>
        {
            using var pane = new DownloadPane();
            form.Controls.Add(pane);
            var running = ListsOf(pane)[0];

            pane.ApplyState(CreateTask("A", "https://example.com/a.mp4", DownloadStatus.Running));
            pane.ApplyState(CreateTask("B", "https://example.com/b.mp4", DownloadStatus.Running));
            pane.ApplyState(CreateTask("C", "https://example.com/c.mp4", DownloadStatus.Running));

            // 未排序时维持现状：新任务插到最上面，不必在长列表底部寻找
            Assert.Equal(new[] { "C", "B", "A" }, Titles(running));
        });
    }

    [Fact]
    public void ApplyState_WhenSorted_UsesProgressKeyForInsertPosition()
    {
        RunStaWithForm(form =>
        {
            using var pane = new DownloadPane();
            form.Controls.Add(pane);
            var running = ListsOf(pane)[0];

            // 取值刻意让「数值序」与「文本序」相反：
            // 数值 10 < 90 < 100，文本却是 "10.0%" < "100.0%" < "90.0%"。
            // 若进度列漏绑 ParseProgress，排序结果会变成 A, C, B，本用例就会失败。
            AddRunningTask(pane, "A", 10);
            AddRunningTask(pane, "B", 90);
            AddRunningTask(pane, "C", 100);

            ClickColumn(running, 1);

            Assert.Equal("进度 ▲", running.Columns[1].Text);
            Assert.Equal(new[] { "A", "B", "C" }, Titles(running));

            // 已排序时新行按进度键插入正确位置，而不是插到最上面
            AddRunningTask(pane, "D", 40);

            Assert.Equal(new[] { "A", "D", "B", "C" }, Titles(running));
        });
    }

    [Fact]
    public void ApplyState_FinishedList_SortsBySizeNumerically()
    {
        RunStaWithForm(form =>
        {
            using var pane = new DownloadPane();
            form.Controls.Add(pane);

            // 「已下载」在未选中的页签上，列表没有原生句柄时排序不会真正生效
            var finished = ActivateTab(pane, 1);
            Assert.True(finished.IsHandleCreated, "排序插入测试需要有句柄的列表");

            pane.ApplyState(CreateTask("Big", "https://example.com/big.mp4", DownloadStatus.Completed, 100L * 1024 * 1024));
            pane.ApplyState(CreateTask("Small", "https://example.com/small.mp4", DownloadStatus.Completed, 9L * 1024 * 1024));
            pane.ApplyState(CreateTask("Mid", "https://example.com/mid.mp4", DownloadStatus.Completed, 20L * 1024 * 1024));

            ClickColumn(finished, 2);

            // 按数值而非文本排序：9 MB < 20 MB < 100 MB（文本序会把 100 MB 排在最前）
            Assert.Equal(new[] { "Small", "Mid", "Big" }, Titles(finished));
        });
    }

    [Fact]
    public void ApplyState_CrossTabMigration_MovesRowAndFillsAllColumns()
    {
        RunStaWithForm(form =>
        {
            using var pane = new DownloadPane();
            form.Controls.Add(pane);
            var lists = ListsOf(pane);
            var running = lists[0];
            var finished = lists[1];

            var task = CreateTask("迁移", "https://example.com/m.mp4", DownloadStatus.Running);
            pane.ApplyState(task);

            Assert.Equal(new[] { "迁移" }, Titles(running));
            Assert.Empty(finished.Items);

            task.Status = DownloadStatus.Completed;
            task.OutputPath = @"C:\output\迁移.mp4";
            task.OutputBytes = 1024;
            task.FinishedAt = new DateTimeOffset(2026, 11, 15, 10, 30, 0, TimeSpan.FromHours(8));
            pane.ApplyState(task);

            Assert.Empty(running.Items);
            var item = Assert.Single(finished.Items.Cast<ListViewItem>());

            // 迁移只复用同一个行实例，说明列等后补的列必须真正写上值
            Assert.Equal(5, item.SubItems.Count);
            Assert.Equal(@"C:\output\迁移.mp4", item.SubItems[4].Text);
            Assert.Equal(DisplayText.Size(1024), item.SubItems[2].Text);
            Assert.Same(task, item.Tag);
        });
    }

    [Fact]
    public void ApplyState_SameListRefresh_KeepsRowPosition()
    {
        RunStaWithForm(form =>
        {
            using var pane = new DownloadPane();
            form.Controls.Add(pane);
            var running = ListsOf(pane)[0];

            AddRunningTask(pane, "A", 10);
            AddRunningTask(pane, "B", 50);
            AddRunningTask(pane, "C", 90);

            ClickColumn(running, 1);
            Assert.Equal(new[] { "A", "B", "C" }, Titles(running));

            // 快照式排序（spec:19）：已有行只更新文本，不按新键值重新找位置。
            // B 的进度从 50% 掉到 5%，仍应留在原位，而不是跑到 A 前面。
            var task = Assert.IsType<DownloadTask>(running.Items[1].Tag);
            pane.ApplyProgress(task.Id, new DownloadProgress { Percent = 5, HasTotal = true });
            pane.ApplyState(task);

            Assert.Equal(new[] { "A", "B", "C" }, Titles(running));
            Assert.Equal("5.0%", running.Items[1].SubItems[1].Text);
        });
    }

    [Fact]
    public void ApplyState_WhenSorted_UsesSpeedKeyForInsertPosition()
    {
        RunStaWithForm(form =>
        {
            using var pane = new DownloadPane();
            form.Controls.Add(pane);
            var running = ListsOf(pane)[0];

            // 数值 9 < 10 < 100 MB/s，文本却是 "100 MB/s" < "10 MB/s" < "9 MB/s"
            AddRunningTaskWithProgress(pane, "A", Percent(10), bytesPerSecond: 10d * 1024 * 1024);
            AddRunningTaskWithProgress(pane, "B", Percent(20), bytesPerSecond: 9d * 1024 * 1024);
            AddRunningTaskWithProgress(pane, "C", Percent(30), bytesPerSecond: 100d * 1024 * 1024);

            ClickColumn(running, 2);

            Assert.Equal("速度 ▲", running.Columns[2].Text);
            Assert.Equal(new[] { "B", "A", "C" }, Titles(running));
        });
    }

    [Fact]
    public void ApplyState_WhenSorted_UsesDownloadedKeyForInsertPosition()
    {
        RunStaWithForm(form =>
        {
            using var pane = new DownloadPane();
            form.Controls.Add(pane);
            var running = ListsOf(pane)[0];

            // 「已下载」按比例比较：9% < 10% < 50%；
            // 文本序则会得到 "10 MB…" < "50 MB…" < "9 MB…"，与本断言相反
            AddRunningTaskWithProgress(pane, "A", Percent(10), downloadedBytes: 10L * 1024 * 1024);
            AddRunningTaskWithProgress(pane, "B", Percent(20), downloadedBytes: 9L * 1024 * 1024);
            AddRunningTaskWithProgress(pane, "C", Percent(30), downloadedBytes: 50L * 1024 * 1024);

            ClickColumn(running, 3);

            Assert.Equal("已下载 ▲", running.Columns[3].Text);
            Assert.Equal(new[] { "B", "A", "C" }, Titles(running));
        });
    }

    [Fact]
    public void ApplyState_CrossTabMigration_InsertsIntoSortedTarget()
    {
        RunStaWithForm(form =>
        {
            using var pane = new DownloadPane();
            form.Controls.Add(pane);
            var finished = ActivateTab(pane, 1);
            Assert.True(finished.IsHandleCreated, "排序插入测试需要有句柄的列表");

            pane.ApplyState(CreateTask("Big", "https://example.com/big.mp4", DownloadStatus.Completed, 100L * 1024 * 1024));
            pane.ApplyState(CreateTask("Small", "https://example.com/small.mp4", DownloadStatus.Completed, 9L * 1024 * 1024));
            pane.ApplyState(CreateTask("Mid", "https://example.com/mid.mp4", DownloadStatus.Completed, 20L * 1024 * 1024));

            ClickColumn(finished, 2);
            Assert.Equal(new[] { "Small", "Mid", "Big" }, Titles(finished));

            // 迁移分支同样必须先填满子项再按目标列表的排序键插入，否则空行会落到错误位置
            var task = CreateTask("迁移", "https://example.com/m.mp4", DownloadStatus.Running);
            pane.ApplyState(task);

            task.Status = DownloadStatus.Completed;
            task.OutputBytes = 30L * 1024 * 1024;
            task.OutputPath = @"C:\output\迁移.mp4";
            task.FinishedAt = new DateTimeOffset(2026, 11, 15, 10, 30, 0, TimeSpan.FromHours(8));
            pane.ApplyState(task);

            Assert.Equal(new[] { "Small", "Mid", "迁移", "Big" }, Titles(finished));
        });
    }

    /// <summary>
    /// 取出面板内部两个页签的列表控件，顺序与页签一致。
    /// </summary>
    private static BufferedListView[] ListsOf(DownloadPane pane)
        => pane.Controls.OfType<TabControl>()
            .Single()
            .TabPages.Cast<TabPage>()
            .SelectMany(p => p.Controls.OfType<BufferedListView>())
            .ToArray();

    /// <summary>
    /// 切换到指定页签，并返回该页签上的列表控件。
    /// </summary>
    /// <remarks>未选中的页签不会创建子控件句柄，原生排序与列宽事件都不会生效。</remarks>
    private static BufferedListView ActivateTab(DownloadPane pane, int index)
    {
        var tabs = pane.Controls.OfType<TabControl>().Single();
        tabs.SelectedIndex = index;
        Application.DoEvents();

        var list = tabs.TabPages[index].Controls.OfType<BufferedListView>().Single();

        // 页签切换后子控件句柄可能仍要等布局消息处理完才建立，这里显式取一次
        _ = list.Handle;
        return list;
    }

    /// <summary>
    /// 读取列表的表头集合。
    /// </summary>
    private static ColumnHeader[] ColumnsOf(BufferedListView list)
        => list.Columns.Cast<ColumnHeader>().ToArray();

    /// <summary>
    /// 读取列表当前行序（按任务标题）。
    /// </summary>
    private static string[] Titles(BufferedListView list)
        => list.Items.Cast<ListViewItem>()
            .Select(i => Assert.IsType<DownloadTask>(i.Tag).Title)
            .ToArray();

    /// <summary>
    /// 构造一个布局快照。
    /// </summary>
    private static ColumnLayout Layout(params (string Name, int Width)[] columns)
    {
        var layout = new ColumnLayout();

        foreach (var (name, width) in columns)
        {
            layout.Columns.Add(new ColumnLayoutEntry { Name = name, Width = width });
        }

        return layout;
    }

    /// <summary>
    /// 构造一条下载任务，默认值与生产链路保持一致。
    /// </summary>
    private static DownloadTask CreateTask(
        string title,
        string url,
        DownloadStatus status,
        long outputBytes = 0,
        string outputPath = "")
        => new()
        {
            Url = url,
            Format = VideoFormat.Mp4,
            Title = title,
            Status = status,
            OutputBytes = outputBytes,
            OutputPath = outputPath,
            FinishedAt = status is DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Canceled
                ? new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.FromHours(8))
                : null
        };

    /// <summary>
    /// 构造一个「总量已知」的进度快照，文本形如 <c>50.0%</c>。
    /// </summary>
    private static DownloadProgress Percent(double percent)
        => new() { Percent = percent, HasTotal = true };

    /// <summary>
    /// 追加一条「下载中」任务，并附带速度/已下载所需的自定义进度字段。
    /// </summary>
    /// <param name="pane">目标面板。</param>
    /// <param name="title">任务标题。</param>
    /// <param name="progress">基础进度（百分比）。</param>
    /// <param name="downloadedBytes">已下载字节数；大于 0 时总量固定为 100 MB。</param>
    /// <param name="bytesPerSecond">瞬时速度（字节/秒）。</param>
    private static void AddRunningTaskWithProgress(
        DownloadPane pane,
        string title,
        DownloadProgress progress,
        long downloadedBytes = 0,
        double bytesPerSecond = 0)
    {
        var task = CreateTask(title, $"https://example.com/{title.ToLowerInvariant()}.mp4", DownloadStatus.Running);

        pane.ApplyProgress(task.Id, progress with
        {
            DownloadedBytes = downloadedBytes,
            TotalBytes = downloadedBytes > 0 ? 100L * 1024 * 1024 : null,
            BytesPerSecond = bytesPerSecond
        });
        pane.ApplyState(task);
    }

    /// <summary>
    /// 追加一条「下载中」任务，并写入可解析的百分比进度文本。
    /// </summary>
    private static void AddRunningTask(DownloadPane pane, string title, double percent)
    {
        var task = CreateTask(title, $"https://example.com/{title.ToLowerInvariant()}.mp4", DownloadStatus.Running);

        // 进度列显示的是 DisplayText.Percent(percent, hasTotal)：
        // hasTotal 为 false 时会输出「计算中…」，解析器返回 null 而排到末尾，测不出排序位置
        pane.ApplyProgress(task.Id, new DownloadProgress { Percent = percent, HasTotal = true });
        pane.ApplyState(task);
    }

    /// <summary>
    /// 通过反射触发私有的表头点击处理，模拟用户点击列头排序。
    /// </summary>
    private static void ClickColumn(BufferedListView list, int columnIndex)
    {
        // ListView 自身也有单参数的 OnColumnClick，必须按签名精确取，否则 AmbiguousMatchException
        var method = typeof(BufferedListView).GetMethod(
            "OnColumnClick",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(object), typeof(ColumnClickEventArgs)],
            modifiers: null)!;
        method.Invoke(list, [null, new ColumnClickEventArgs(columnIndex)]);
    }

    /// <summary>
    /// 在 STA 线程上执行 WinForms 相关操作，并把线程内异常原样抛回测试线程。
    /// </summary>
    private static void RunSta(Action action)
    {
        Exception? captured = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);

        // 超时用的是 Assert.Fail，此时线程仍在运行；前台线程会拖住 testhost 无法退出，
        // 表现为「整个测试运行挂起」而不是「单个用例失败」。
        thread.IsBackground = true;
        thread.Start();

        if (!thread.Join(TimeSpan.FromSeconds(30)))
        {
            Assert.Fail("STA 线程超时");
        }

        if (captured is not null)
        {
            ExceptionDispatchInfo.Capture(captured).Throw();
        }
    }

    /// <summary>
    /// 在 STA 线程上创建并显示一个窗体，再执行需要真实句柄的面板操作。
    /// </summary>
    private static void RunStaWithForm(Action<Form> action)
    {
        RunSta(() =>
        {
            var form = new Form();
            form.Show();
            try
            {
                action(form);
            }
            finally
            {
                form.Close();
                form.Dispose();
            }
        });
    }
}