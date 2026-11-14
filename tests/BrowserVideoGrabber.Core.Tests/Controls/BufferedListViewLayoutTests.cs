/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Controls
*文件名： BufferedListViewLayoutTests
*版本号： V1.0.0.0
*唯一标识：c8d5f1a2-6b47-4e93-9a30-2f87b5c1d6e4
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/11/15 03:10:00
*描述：列表列布局的回归测试，覆盖重名列、损坏布局、宽度夹取与往返正确性。
*
*=================================================
*修改标记
*修改时间：2026/11/15 03:10:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;

using BrowserVideoGrabber.App.Controls;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Tests.Controls;

public sealed class BufferedListViewLayoutTests
{
    [Fact]
    public void AddColumns_DuplicateWithinOneCall_ThrowsAndAddsNothing()
    {
        RunSta(() =>
        {
            using var list = new BufferedListView();

            Assert.Throws<ArgumentException>(() =>
                list.AddColumns(("A", 50, null), ("A", 60, null)));

            Assert.Empty(list.Columns.Cast<ColumnHeader>());
        });
    }

    [Fact]
    public void AddColumns_DuplicateAcrossCalls_ThrowsAndKeepsExistingColumns()
    {
        RunSta(() =>
        {
            using var list = new BufferedListView();
            list.AddColumns(("A", 50, null));

            Assert.Throws<ArgumentException>(() =>
                list.AddColumns(("B", 60, null), ("A", 70, null)));

            // 校验先于添加：第二个调用里的 B 也不应留下半截列
            Assert.Single(list.Columns.Cast<ColumnHeader>());
            Assert.Equal("A", list.Columns[0].Text);
        });
    }

    [Fact]
    public void ApplyColumnLayout_NullEntry_IsSkippedWithoutThrowing()
    {
        RunSta(() =>
        {
            using var list = new BufferedListView();
            list.AddColumns(("A", 50, null), ("B", 60, null), ("C", 70, null));

            var layout = new ColumnLayout();
            layout.Columns.Add(null!);
            layout.Columns.Add(new ColumnLayoutEntry { Name = "C", Width = 70 });
            layout.Columns.Add(new ColumnLayoutEntry { Name = "A", Width = 50 });

            var exception = Record.Exception(() => list.ApplyColumnLayout(layout));

            Assert.Null(exception);
            Assert.Equal(
                new[] { "C", "A", "B" },
                list.GetColumnLayout().Columns.Select(c => c.Name));
        });
    }

    [Fact]
    public void ApplyColumnLayout_OversizedWidth_IsClampedToMax()
    {
        RunSta(() =>
        {
            using var list = new BufferedListView();
            list.AddColumns(("A", 50, null));

            list.ApplyColumnLayout(new ColumnLayout
            {
                Columns =
                {
                    new ColumnLayoutEntry { Name = "A", Width = 1_000_000 }
                }
            });

            // LVM_SETCOLUMNWIDTH 的低 16 位按有符号解释，32767 才是有效上界
            Assert.Equal(32767, list.Columns[0].Width);
        });
    }

    [Fact]
    public void ApplyColumnLayout_WidthAtOrBelowMax_IsPreserved()
    {
        RunSta(() =>
        {
            using var list = new BufferedListView();
            list.AddColumns(("A", 50, null));

            list.ApplyColumnLayout(new ColumnLayout
            {
                Columns =
                {
                    new ColumnLayoutEntry { Name = "A", Width = 32767 }
                }
            });

            Assert.Equal(32767, list.Columns[0].Width);
        });
    }

    [Fact]
    public void ApplyColumnLayout_Width32768_IsClampedTo32767()
    {
        RunSta(() =>
        {
            using var list = new BufferedListView();
            list.AddColumns(("A", 50, null));

            list.ApplyColumnLayout(new ColumnLayout
            {
                Columns =
                {
                    new ColumnLayoutEntry { Name = "A", Width = 32768 }
                }
            });

            Assert.Equal(32767, list.Columns[0].Width);
        });
    }

    [Fact]
    public void ApplyColumnLayout_DuplicateTag_DoesNotThrow()
    {
        RunSta(() =>
        {
            using var list = new BufferedListView();
            list.AddColumns(("A", 50, null), ("B", 60, null));

            // 人为制造重复 Tag（损坏的列定义），ToDictionary 会抛异常，首个胜出才符合「绝不阻断启动」
            list.Columns[1].Tag = "A";

            var exception = Record.Exception(() =>
                list.ApplyColumnLayout(new ColumnLayout
                {
                    Columns =
                    {
                        new ColumnLayoutEntry { Name = "A", Width = 100 }
                    }
                }));

            Assert.Null(exception);
            Assert.Equal(100, list.Columns[0].Width);
        });
    }

    [Fact]
    public void AddColumns_WithNullName_ThrowsAndAddsNothing()
    {
        RunSta(() =>
        {
            using var list = new BufferedListView();

            var exception = Record.Exception(() => list.AddColumns(((string)null!, 50, null)));

            Assert.IsType<ArgumentException>(exception);
            Assert.Empty(list.Columns.Cast<ColumnHeader>());
        });
    }

    [Fact]
    public void AddColumns_WithWhitespaceName_ThrowsAndAddsNothing()
    {
        RunSta(() =>
        {
            using var list = new BufferedListView();

            var exception = Record.Exception(() => list.AddColumns(("  ", 50, null)));

            Assert.IsType<ArgumentException>(exception);
            Assert.Empty(list.Columns.Cast<ColumnHeader>());
        });
    }

    [Fact]
    public void AddColumns_WithWidthOutOfRange_ThrowsAndAddsNothing()
    {
        RunSta(() =>
        {
            using var list = new BufferedListView();

            var zero = Record.Exception(() => list.AddColumns(("A", 0, null)));
            Assert.IsType<ArgumentOutOfRangeException>(zero);
            Assert.Empty(list.Columns.Cast<ColumnHeader>());

            var tooWide = Record.Exception(() => list.AddColumns(("A", 32768, null)));
            Assert.IsType<ArgumentOutOfRangeException>(tooWide);
            Assert.Empty(list.Columns.Cast<ColumnHeader>());
        });
    }

    [Fact]
    public void AddColumns_WithHandle_RaisesNoLayoutChanged()
    {
        RunStaWithForm(form =>
        {
            using var list = new BufferedListView();
            form.Controls.Add(list);
            Assert.True(list.IsHandleCreated, "列宽事件抑制测试需要有句柄的列表");

            var count = 0;
            list.LayoutChanged += (_, _) => count++;

            list.AddColumns(("A", 50, null), ("B", 60, null), ("C", 70, null));

            Assert.Equal(3, list.Columns.Count);
            Assert.Equal(0, count);
        });
    }

    [Fact]
    public void ColumnWidthChanged_RealChange_RaisesLayoutChangedOnce()
    {
        RunStaWithForm(form =>
        {
            using var list = new BufferedListView();
            form.Controls.Add(list);
            list.AddColumns(("A", 50, null), ("B", 60, null));

            var count = 0;
            list.LayoutChanged += (_, _) => count++;

            list.Columns[0].Width = 150;

            Assert.Equal(1, count);
            Assert.Equal(150, list.Columns[0].Width);
        });
    }

    [Fact]
    public void ApplySort_DoesNotRaiseLayoutChanged()
    {
        RunStaWithForm(form =>
        {
            using var list = new BufferedListView();
            form.Controls.Add(list);
            list.AddColumns(("名称", 100, null), ("大小", 100, null));
            list.Items.Add(new ListViewItem(new[] { "b", "1" }));
            list.Items.Add(new ListViewItem(new[] { "a", "2" }));

            var count = 0;
            list.LayoutChanged += (_, _) => count++;

            ClickColumn(list, 0);

            // 排序会改表头文本（▲），由此连带的 HDN_ITEMCHANGED 不应被当成布局变化
            Assert.Equal(0, count);
            Assert.Null(list.ListViewItemSorter);
        });
    }

    [Fact]
    public void ApplyColumnLayout_HugeWidth_ClampsTo32767WithHandle()
    {
        RunStaWithForm(form =>
        {
            using var list = new BufferedListView();
            form.Controls.Add(list);
            list.AddColumns(("A", 50, null));

            list.ApplyColumnLayout(new ColumnLayout
            {
                Columns =
                {
                    new ColumnLayoutEntry { Name = "A", Width = 1_000_000 }
                }
            });

            Assert.True(list.IsHandleCreated);
            Assert.Equal(32767, list.Columns[0].Width);
        });
    }

    [Fact]
    public void GetColumnLayout_UsesTagInsteadOfHeaderText()
    {
        RunSta(() =>
        {
            using var list = new BufferedListView();
            list.AddColumns(("名称", 100, null));

            // 排序箭头会污染表头文本，持久化必须使用 Tag 里的原始列名
            list.Columns[0].Text = "名称 ▲";

            var layout = list.GetColumnLayout();

            Assert.Equal("名称", Assert.Single(layout.Columns).Name);
        });
    }

    [Fact]
    public void ApplyColumnLayout_ThenGetColumnLayout_RoundTripsOrderAndWidths()
    {
        RunSta(() =>
        {
            using var list = new BufferedListView();
            list.AddColumns(("A", 50, null), ("B", 60, null), ("C", 70, null));

            list.ApplyColumnLayout(new ColumnLayout
            {
                Columns =
                {
                    new ColumnLayoutEntry { Name = "C", Width = 100 },
                    new ColumnLayoutEntry { Name = "A", Width = 40 }
                }
            });

            var result = list.GetColumnLayout();

            Assert.Equal(
                new[] { "C", "A", "B" },
                result.Columns.Select(c => c.Name));
            Assert.Equal(
                new[] { 100, 40, 60 },
                result.Columns.Select(c => c.Width));
        });
    }

    [Fact]
    public void GetColumnLayout_OutOfRangeWidth_IsClampedToMax()
    {
        RunSta(() =>
        {
            using var list = new BufferedListView();
            list.AddColumns(("A", 50, null));

            // 无句柄时 ColumnHeader.Width 只是托管字段，可以塞进越界值；
            // 快照必须与 ApplyColumnLayout 对称，只输出合法宽度。
            list.Columns[0].Width = 40000;

            Assert.Equal(32767, Assert.Single(list.GetColumnLayout().Columns).Width);
        });
    }

    [Fact]
    public void ApplyColumnLayout_WithHandle_RaisesNoLayoutChanged()
    {
        RunStaWithForm(form =>
        {
            using var list = new BufferedListView();
            form.Controls.Add(list);
            list.AddColumns(("A", 50, null), ("B", 60, null), ("C", 70, null));

            var count = 0;
            list.LayoutChanged += (_, _) => count++;

            list.ApplyColumnLayout(new ColumnLayout
            {
                Columns =
                {
                    new ColumnLayoutEntry { Name = "C", Width = 200 },
                    new ColumnLayoutEntry { Name = "A", Width = 150 }
                }
            });

            // 程序化应用布局期间必须完全静默，否则会立刻回写一份「刚读出来的布局」
            Assert.Equal(0, count);
            Assert.Equal(200, list.Columns[2].Width);
        });
    }

    [Fact]
    public void ColumnClick_UsesCollectionIndexNotDisplayIndex()
    {
        RunStaWithForm(form =>
        {
            using var list = new BufferedListView();
            form.Controls.Add(list);
            list.AddColumns(("A", 60, null), ("B", 60, null), ("C", 60, null));
            list.Items.Add(new ListViewItem(new[] { "b", "1", "y" }));
            list.Items.Add(new ListViewItem(new[] { "a", "2", "x" }));

            // 恢复持久化布局：视觉顺序变成 C、A、B，集合顺序仍是 A、B、C。
            // ColumnClickEventArgs.Column 是集合索引（原生 LVN_COLUMNCLICK 的 iSubItem），
            // 不是显示位置；此处点击视觉最左列（C，集合索引 2）必须按 C 排序。
            list.Columns[2].DisplayIndex = 0;

            ClickColumn(list, 2);

            Assert.Equal("C ▲", list.Columns[2].Text);
            Assert.Equal("A", list.Columns[0].Text);
            Assert.Equal("B", list.Columns[1].Text);
            Assert.Equal(new[] { "a", "b" }, list.Items.Cast<ListViewItem>().Select(i => i.Text));
        });
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
    /// 在 STA 线程上创建并显示一个窗体，再执行需要真实句柄的列表操作。
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
}