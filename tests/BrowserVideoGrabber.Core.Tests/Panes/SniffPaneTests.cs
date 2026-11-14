/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Panes
*文件名： SniffPaneTests
*版本号： V1.0.0.0
*唯一标识：5e2a8c31-9f4d-4b76-8a10-3c7d92e6b4f8
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/11/15 12:00:00
*描述：页面视频面板的列序、行映射、布局转发与快照式排序回归测试。
*
*=================================================
*修改标记
*修改时间：2026/11/15 12:00:00
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

public sealed class SniffPaneTests
{
    [Fact]
    public void Constructor_DefinesColumns_NameFirst()
    {
        RunSta(() =>
        {
            using var pane = new SniffPane();

            var columns = ListOf(pane).Columns.Cast<ColumnHeader>().ToArray();

            // 名称列必须在集合索引 0：新行构造与 ApplyFields 都按集合索引写子项，
            // 列序一旦漂移，整行数据就会错列。
            Assert.Equal(
                new[] { "名称", "格式", "分辨率", "时长", "大小", "来源", "发现时间", "地址" },
                columns.Select(c => c.Text));
            Assert.Equal(
                new[] { 200, 80, 90, 70, 90, 70, 110, 420 },
                columns.Select(c => c.Width));
        });
    }

    [Fact]
    public void AddOrUpdate_NewItem_FillsSubItemsInColumnOrder()
    {
        RunSta(() =>
        {
            using var pane = new SniffPane();
            var list = ListOf(pane);
            var video = CreateVideo(
                pageTitle: "视频A",
                url: "https://example.com/a.mp4",
                format: VideoFormat.Mp4,
                resolution: "1920x1080",
                durationSeconds: 754,
                contentLength: 1_048_576);

            pane.AddOrUpdate(video);

            var item = Assert.Single(list.Items.Cast<ListViewItem>());
            Assert.StartsWith("视频A_", item.SubItems[0].Text);
            Assert.Equal(DisplayText.Format(VideoFormat.Mp4), item.SubItems[1].Text);
            Assert.Equal("1920x1080", item.SubItems[2].Text);
            Assert.Equal("12:34", item.SubItems[3].Text);
            Assert.Equal(DisplayText.Size(1_048_576), item.SubItems[4].Text);
            Assert.Equal("特征", item.SubItems[5].Text);
            Assert.Equal("09:05:07", item.SubItems[6].Text);
            Assert.Equal("https://example.com/a.mp4", item.SubItems[7].Text);
            Assert.Same(video, item.Tag);
        });
    }

    [Fact]
    public void AddOrUpdate_WhenUnsorted_AppendsNewItems()
    {
        RunSta(() =>
        {
            using var pane = new SniffPane();
            var list = ListOf(pane);

            pane.AddOrUpdate(CreateVideo(pageTitle: "A", url: "https://example.com/a.mp4"));
            pane.AddOrUpdate(CreateVideo(pageTitle: "B", url: "https://example.com/b.mp4"));
            pane.AddOrUpdate(CreateVideo(pageTitle: "C", url: "https://example.com/c.mp4"));

            // 未排序时维持现状：新条目追加到末尾，而不是插到最上面
            Assert.Equal(new[] { "A", "B", "C" }, Titles(list));
        });
    }

    [Fact]
    public void AddOrUpdate_DuplicateId_RefreshesTextWithoutMovingRow()
    {
        RunSta(() =>
        {
            using var pane = new SniffPane();
            var list = ListOf(pane);
            var first = CreateVideo(pageTitle: "A", url: "https://example.com/a.mp4", durationSeconds: 60);
            var second = CreateVideo(pageTitle: "B", url: "https://example.com/b.mp4", durationSeconds: 120);

            pane.AddOrUpdate(first);
            pane.AddOrUpdate(second);

            // 快照式排序：重复上报只改文本、不移动已有行（设计文档 spec 明示的取舍）。
            var refreshed = new SniffedVideo
            {
                Id = first.Id,
                Url = first.Url,
                Format = first.Format,
                PageTitle = first.PageTitle,
                DurationSeconds = 300,
                Source = first.Source,
                DetectedAt = first.DetectedAt
            };
            pane.AddOrUpdate(refreshed);

            var items = list.Items.Cast<ListViewItem>().ToArray();
            Assert.Equal(2, items.Length);
            Assert.Equal(new[] { "A", "B" }, Titles(list));
            Assert.Equal("05:00", items[0].SubItems[3].Text);
            Assert.Same(refreshed, items[0].Tag);
        });
    }

    [Fact]
    public void AddOrUpdate_WhenSorted_UsesDurationKeyForInsertPosition()
    {
        RunStaWithForm(form =>
        {
            using var pane = new SniffPane();
            form.Controls.Add(pane);
            var list = ListOf(pane);
            Assert.True(list.IsHandleCreated, "排序插入测试需要有句柄的列表");

            pane.AddOrUpdate(CreateVideo(pageTitle: "H", url: "https://example.com/h.mp4", durationSeconds: 7200));
            pane.AddOrUpdate(CreateVideo(pageTitle: "M", url: "https://example.com/m.mp4", durationSeconds: 754));
            pane.AddOrUpdate(CreateVideo(pageTitle: "S", url: "https://example.com/s.mp4", durationSeconds: 120));

            ClickColumn(list, 3);

            Assert.Equal(new[] { "S", "M", "H" }, Titles(list));
            Assert.Equal("时长 ▲", list.Columns[3].Text);

            // 已排序时新行按当前排序键插入正确位置，而不是追加末尾
            pane.AddOrUpdate(CreateVideo(pageTitle: "X", url: "https://example.com/x.mp4", durationSeconds: 600));

            Assert.Equal(new[] { "S", "X", "M", "H" }, Titles(list));
        });
    }

    [Fact]
    public void AddOrUpdate_WhenSorted_UsesSizeAndClockKeys()
    {
        RunStaWithForm(form =>
        {
            using var pane = new SniffPane();
            form.Controls.Add(pane);
            var list = ListOf(pane);

            pane.AddOrUpdate(CreateVideo(
                pageTitle: "Big", url: "https://example.com/big.mp4", contentLength: 1024L * 1024 * 1024));
            pane.AddOrUpdate(CreateVideo(
                pageTitle: "Small", url: "https://example.com/small.mp4", contentLength: 10L * 1024 * 1024));
            pane.AddOrUpdate(CreateVideo(
                pageTitle: "Mid", url: "https://example.com/mid.mp4", contentLength: 500L * 1024 * 1024));

            ClickColumn(list, 4);

            // 按数值而非文本排序：10 MB < 500 MB < 1 GB
            Assert.Equal(new[] { "Small", "Mid", "Big" }, Titles(list));

            pane.ClearItems();

            pane.AddOrUpdate(CreateVideo(
                pageTitle: "Late", url: "https://example.com/late.mp4", detectedAt: new DateTimeOffset(2026, 11, 15, 23, 59, 59, TimeSpan.FromHours(8))));
            pane.AddOrUpdate(CreateVideo(
                pageTitle: "Early", url: "https://example.com/early.mp4", detectedAt: new DateTimeOffset(2026, 11, 16, 0, 1, 0, TimeSpan.FromHours(8))));
            pane.AddOrUpdate(CreateVideo(
                pageTitle: "Noon", url: "https://example.com/noon.mp4", detectedAt: new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.FromHours(8))));

            ClickColumn(list, 6);

            Assert.Equal(new[] { "Early", "Noon", "Late" }, Titles(list));
        });
    }

    [Fact]
    public void LayoutChanged_ForwardsSniffKey()
    {
        RunStaWithForm(form =>
        {
            using var pane = new SniffPane();
            form.Controls.Add(pane);
            var list = ListOf(pane);

            var received = new List<ColumnLayoutChangedEventArgs>();
            pane.LayoutChanged += (_, e) => received.Add(e);

            list.Columns[0].Width = 300;

            var args = Assert.Single(received);
            Assert.Equal(SniffPane.LayoutKey, args.Key);
            Assert.Equal("sniff", args.Key);
            Assert.Equal(300, args.Layout.Columns[0].Width);
        });
    }

    [Fact]
    public void ApplyColumnLayout_RaisesNoLayoutChanged()
    {
        RunStaWithForm(form =>
        {
            using var pane = new SniffPane();
            form.Controls.Add(pane);

            var count = 0;
            pane.LayoutChanged += (_, _) => count++;

            pane.ApplyColumnLayout(new ColumnLayout
            {
                Columns =
                {
                    new ColumnLayoutEntry { Name = "地址", Width = 260 },
                    new ColumnLayoutEntry { Name = "名称", Width = 180 }
                }
            });

            // 程序化应用布局必须静默，否则会立刻回写一份「刚读出来的布局」
            Assert.Equal(0, count);

            var layout = pane.GetColumnLayout();
            Assert.Equal("地址", layout.Columns[0].Name);
            Assert.Equal("名称", layout.Columns[1].Name);
        });
    }

    [Fact]
    public void ApplyColumnLayout_Null_KeepsCurrentLayout()
    {
        RunSta(() =>
        {
            using var pane = new SniffPane();

            pane.ApplyColumnLayout(new ColumnLayout
            {
                Columns =
                {
                    new ColumnLayoutEntry { Name = "地址", Width = 260 },
                    new ColumnLayoutEntry { Name = "名称", Width = 180 }
                }
            });
            var before = pane.GetColumnLayout();

            pane.ApplyColumnLayout(null);

            var after = pane.GetColumnLayout();
            Assert.Equal(before.Columns.Select(c => c.Name), after.Columns.Select(c => c.Name));
            Assert.Equal(before.Columns.Select(c => c.Width), after.Columns.Select(c => c.Width));
        });
    }

    [Fact]
    public void ClearItems_ResetsCountAndAllowsSameIdAgain()
    {
        RunSta(() =>
        {
            using var pane = new SniffPane();
            var list = ListOf(pane);
            var video = CreateVideo(pageTitle: "A", url: "https://example.com/a.mp4");

            pane.AddOrUpdate(video);
            Assert.Equal(1, pane.ItemCount);

            pane.ClearItems();

            Assert.Equal(0, pane.ItemCount);
            Assert.Empty(list.Items);

            // 清空后去重字典必须同步，否则同一资源重新上报会被当成重复而什么都不做
            pane.AddOrUpdate(video);
            Assert.Equal(1, pane.ItemCount);
            Assert.Single(list.Items.Cast<ListViewItem>());
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ColumnLayoutChangedEventArgs_BlankKey_Throws(string key)
    {
        Assert.Throws<ArgumentException>(() => new ColumnLayoutChangedEventArgs(key, new ColumnLayout()));
    }

    [Fact]
    public void ColumnLayoutChangedEventArgs_NullKey_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ColumnLayoutChangedEventArgs(null!, new ColumnLayout()));
    }

    [Fact]
    public void ColumnLayoutChangedEventArgs_NullLayout_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ColumnLayoutChangedEventArgs("sniff", null!));
    }

    /// <summary>
    /// 取出面板内部的列表控件。
    /// </summary>
    private static BufferedListView ListOf(SniffPane pane)
        => Assert.Single(pane.Controls.OfType<BufferedListView>());

    /// <summary>
    /// 读取列表当前行序（按页面标题）。
    /// </summary>
    private static IEnumerable<string> Titles(BufferedListView list)
        => list.Items.Cast<ListViewItem>()
            .Select(i => Assert.IsType<SniffedVideo>(i.Tag).DisplayTitle);

    /// <summary>
    /// 构造一条嗅探结果，默认值保持与生产链路一致。
    /// </summary>
    private static SniffedVideo CreateVideo(
        string pageTitle,
        string url,
        VideoFormat format = VideoFormat.Mp4,
        string? resolution = null,
        double? durationSeconds = null,
        long? contentLength = null,
        DateTimeOffset? detectedAt = null)
        => new()
        {
            Url = url,
            Format = format,
            PageTitle = pageTitle,
            Resolution = resolution,
            DurationSeconds = durationSeconds,
            ContentLength = contentLength,
            Source = "url",
            DetectedAt = detectedAt ?? new DateTimeOffset(2026, 11, 15, 9, 5, 7, TimeSpan.FromHours(8))
        };

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