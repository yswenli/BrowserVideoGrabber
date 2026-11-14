/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Controls
*文件名： BufferedListViewSortingTests
*版本号： V1.0.0.0
*唯一标识：e4a7c2b9-3f61-4d08-a2c5-7b91d6f30e84
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/11/15 02:30:00
*描述：列表排序比较逻辑的回归测试，保证键选择器或 CompareTo 抛异常时退化为文本比较而不崩溃。
*
*=================================================
*修改标记
*修改时间：2026/11/15 02:30:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;

using BrowserVideoGrabber.App.Controls;
using BrowserVideoGrabber.Core.Lists;

namespace BrowserVideoGrabber.Tests.Controls;

public sealed class BufferedListViewSortingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompareTexts_KeySelectorThrows_FallsBackToText(bool ascending)
    {
        static IComparable? ThrowingSelector(string _)
            => throw new InvalidOperationException("键选择器故意抛异常");

        var result = BufferedListView.CompareTexts("b", "a", ThrowingSelector, ascending);

        // "b" > "a"，升序为正、降序为负
        Assert.Equal(ascending ? 1 : -1, Math.Sign(result));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompareTexts_MixedKeyTypes_CompareToThrows_FallsBackToText(bool ascending)
    {
        // 同一列混入 double 与 string，double.CompareTo(string) 会抛 ArgumentException
        static IComparable? MixedSelector(string text) => text == "x" ? 1d : "abc";

        var result = BufferedListView.CompareTexts("x", "abc", MixedSelector, ascending);

        // 退化为 "x" 与 "abc" 的文本比较："x" > "abc"
        Assert.Equal(ascending ? 1 : -1, Math.Sign(result));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompareTexts_CompareToThrowsNonArgumentException_FallsBackToText(bool ascending)
    {
        var result = BufferedListView.CompareTexts("b", "a", _ => new ThrowingComparable(), ascending);

        Assert.Equal(ascending ? 1 : -1, Math.Sign(result));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompareTexts_NullKey_AlwaysSortsLast(bool ascending)
    {
        // "x" 解析为 null，其余按文本作为键
        static IComparable? NullForX(string text) => text == "x" ? null : text;

        var nullOnRight = BufferedListView.CompareTexts("x", "abc", NullForX, ascending);
        var nullOnLeft = BufferedListView.CompareTexts("abc", "x", NullForX, ascending);
        var bothNull = BufferedListView.CompareTexts("x", "x", NullForX, ascending);

        // null 永远排末尾，不随升降序翻转
        Assert.True(nullOnRight > 0, $"null 应排末尾，实际 {nullOnRight}");
        Assert.True(nullOnLeft < 0, $"null 应排末尾，实际 {nullOnLeft}");
        Assert.Equal(0, bothNull);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompareTexts_NullKeySelector_ComparesText(bool ascending)
    {
        var result = BufferedListView.CompareTexts("a", "B", null, ascending);

        // OrdinalIgnoreCase 下 "a" < "B"
        Assert.Equal(ascending ? -1 : 1, Math.Sign(result));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompareTexts_SizeKeys_ComparesNumerically(bool ascending)
    {
        var result = BufferedListView.CompareTexts("2 MB", "10 MB", ColumnValueParser.ParseSize, ascending);

        // 按字节 2 MB < 10 MB；若按文本比较则 "10 MB" 会排在 "2 MB" 前面
        Assert.Equal(ascending ? -1 : 1, Math.Sign(result));
        Assert.True(result != 0, "体积键不应被当作相等");
    }

    private sealed class ThrowingComparable : IComparable
    {
        public int CompareTo(object? obj)
            => throw new InvalidOperationException("CompareTo 故意抛异常");
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public void InsertItemSorted_WhenUnsorted_InsertsAtTopOrBottom(bool insertAtTop, int expectedIndex)
    {
        RunSta(() =>
        {
            using var list = new BufferedListView();
            list.AddColumns(("名称", 100, null));
            list.Items.Add(new ListViewItem("existing"));

            var inserted = new ListViewItem("new");
            list.InsertItemSorted(inserted, insertAtTop);

            Assert.Equal(expectedIndex, list.Items.IndexOf(inserted));
        });
    }

    [Fact]
    public void InsertItemSorted_Ascending_KeepsNewItemAtFrontOfEqualSegment()
    {
        RunSta(() =>
        {
            using var list = new BufferedListView();
            list.AddColumns(("名称", 100, null));
            list.Items.Add(new ListViewItem("a"));
            list.Items.Add(new ListViewItem("b"));
            list.Items.Add(new ListViewItem("c"));

            ClickColumn(list, 0); // 第一次点击进入升序

            var inserted = new ListViewItem("b");
            list.InsertItemSorted(inserted);

            // a, [新 b], 旧 b, c —— 相等段里新行在最前
            Assert.Equal(1, list.Items.IndexOf(inserted));
            Assert.Equal("b", list.Items[2].Text);
        });
    }

    [Fact]
    public void InsertItemSorted_Descending_KeepsNewItemAtFrontOfEqualSegment()
    {
        RunSta(() =>
        {
            using var list = new BufferedListView();
            list.AddColumns(("名称", 100, null));
            list.Items.Add(new ListViewItem("c"));
            list.Items.Add(new ListViewItem("b"));
            list.Items.Add(new ListViewItem("a"));

            ClickColumn(list, 0); // 升序
            ClickColumn(list, 0); // 再点一次转降序

            var inserted = new ListViewItem("b");
            list.InsertItemSorted(inserted);

            // c, [新 b], 旧 b, a —— 相等段里新行仍在前
            Assert.Equal(1, list.Items.IndexOf(inserted));
            Assert.Equal("b", list.Items[2].Text);
        });
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void InsertItemSorted_NullKey_GoesLastInBothDirections(bool descending, int expectedIndex)
    {
        RunSta(() =>
        {
            using var list = new BufferedListView();
            list.AddColumns(("名称", 100, text => string.IsNullOrEmpty(text) ? null : text));
            list.Items.Add(new ListViewItem(descending ? "b" : "a"));
            list.Items.Add(new ListViewItem(descending ? "a" : "b"));

            ClickColumn(list, 0);
            if (descending)
            {
                ClickColumn(list, 0);
            }

            var inserted = new ListViewItem(string.Empty);
            list.InsertItemSorted(inserted);

            // 空文本解析为 null，无论升序还是降序都必须沉底
            Assert.Equal(expectedIndex, list.Items.IndexOf(inserted));
        });
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public void InsertItemSorted_SortColumnMissing_FallsBackToUnsortedPosition(
        bool insertAtTop,
        int expectedIndex)
    {
        RunSta(() =>
        {
            using var list = new BufferedListView();
            list.AddColumns(("名称", 100, null), ("大小", 100, null));
            list.Items.Add(new ListViewItem(new[] { "a", "1" }));

            ClickColumn(list, 0);
            list.Columns.RemoveAt(0); // 排序列被运行期移除

            var inserted = new ListViewItem("z");
            list.InsertItemSorted(inserted, insertAtTop);

            // 不能因为比较恒为 0 而永远插到最前，必须按未排序规则落位
            Assert.Equal(expectedIndex, list.Items.IndexOf(inserted));
        });
    }

    [Fact]
    public void ApplySort_ClearsSorter()
    {
        RunSta(() =>
        {
            using var list = new BufferedListView();
            list.AddColumns(("名称", 100, null));
            list.Items.Add(new ListViewItem("a"));

            ClickColumn(list, 0);

            // 排序完成后必须清空 sorter，否则后续 Items.Add 会被自动排序插入
            Assert.Null(list.ListViewItemSorter);
        });
    }

    [Fact]
    public void CompareItems_WithThrowingSelector_DoesNotThrow()
    {
        RunSta(() =>
        {
            using var list = new BufferedListView();
            list.AddColumns(
                ("名称", 100, _ => throw new InvalidOperationException("键选择器故意抛异常")));
            list.Items.Add(new ListViewItem("a"));
            list.Items.Add(new ListViewItem("b"));

            ClickColumn(list, 0);

            var result = list.CompareItems(list.Items[0], list.Items[1]);

            // 兜底后返回任意数值都可以，关键是绝不向原生比较回调抛异常
            Assert.IsType<int>(result);
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