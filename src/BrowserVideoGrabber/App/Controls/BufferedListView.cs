/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.App.Controls
*文件名： BufferedListView
*版本号： V1.0.0.0
*唯一标识：2b9f4c71-8d3e-4a95-b6f2-1c7e5a8d3b40
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 01:10:00
*描述：开启双缓冲的列表控件，用于消除高频刷新时的闪烁。
*
*=================================================
*修改标记
*修改时间：2026/9/13 01:10:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Collections;
using System.Diagnostics;

using BrowserVideoGrabber.Core.Lists;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.App.Controls;

/// <summary>
/// 开启双缓冲的 <see cref="ListView"/>，并统一提供列拖动、点击排序与列布局持久化能力。
/// </summary>
internal sealed class BufferedListView : ListView
{
    private readonly Dictionary<string, Func<string, IComparable?>> _keySelectors = new(StringComparer.Ordinal);

    private string? _sortColumnName;
    private int _sortColumnIndex = -1;
    private bool _sortAscending = true;
    private bool _suppressLayoutEvents;

    /// <summary>
    /// 初始化列表控件并开启双缓冲。
    /// </summary>
    public BufferedListView()
    {
        DoubleBuffered = true;
        View = View.Details;
        FullRowSelect = true;
        HideSelection = false;
        MultiSelect = false;
        GridLines = false;
        HeaderStyle = ColumnHeaderStyle.Clickable;
        AllowColumnReorder = true;

        ColumnClick += OnColumnClick;
        ColumnWidthChanged += OnColumnWidthChanged;
        ColumnReordered += OnColumnReordered;
    }

    /// <summary>
    /// 列顺序或列宽发生变化。
    /// </summary>
    /// <remarks>事件携带变化后的布局快照，订阅方负责防抖与持久化。</remarks>
    public event EventHandler<ColumnLayout>? LayoutChanged;

    /// <summary>
    /// 按「名称 + 宽度 + 可选键选择器」批量创建列。
    /// </summary>
    /// <param name="columns">
    /// 列定义。键选择器为 null 时按 <see cref="ColumnValueParser.ParseText"/> 排序。
    /// </param>
    public void AddColumns(params (string Name, int Width, Func<string, IComparable?>? Key)[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        foreach (var (name, width, key) in columns)
        {
            var header = Columns.Add(name, width);
            header.Tag = name;
            _keySelectors[name] = key ?? ColumnValueParser.ParseText;
        }
    }

    /// <summary>
    /// 读取当前列布局（按显示顺序）。
    /// </summary>
    /// <returns>列布局快照。</returns>
    public ColumnLayout GetColumnLayout()
    {
        var layout = new ColumnLayout();

        foreach (var column in Columns.Cast<ColumnHeader>().OrderBy(c => c.DisplayIndex))
        {
            layout.Columns.Add(new ColumnLayoutEntry
            {
                Name = column.Tag as string ?? column.Text,
                Width = column.Width
            });
        }

        return layout;
    }

    /// <summary>
    /// 应用列布局：按列名匹配，文件多出的列忽略，文件缺失的列补在末尾。
    /// </summary>
    /// <param name="layout">布局；为 null 时不做任何事。</param>
    public void ApplyColumnLayout(ColumnLayout? layout)
    {
        if (layout?.Columns is not { Count: > 0 })
        {
            return;
        }

        _suppressLayoutEvents = true;
        try
        {
            var byName = Columns.Cast<ColumnHeader>()
                .Where(c => c.Tag is string)
                .ToDictionary(c => (string)c.Tag!, StringComparer.Ordinal);

            var ordered = new List<ColumnHeader>();
            var used = new HashSet<ColumnHeader>();

            foreach (var entry in layout.Columns)
            {
                if (string.IsNullOrWhiteSpace(entry.Name)
                    || !byName.TryGetValue(entry.Name, out var column)
                    || !used.Add(column))
                {
                    continue;
                }

                if (entry.Width > 0)
                {
                    column.Width = entry.Width;
                }

                ordered.Add(column);
            }

            // 布局文件里没有的列（版本升级新增）按当前集合顺序补在末尾
            foreach (var column in Columns.Cast<ColumnHeader>())
            {
                if (used.Add(column))
                {
                    ordered.Add(column);
                }
            }

            for (var i = 0; i < ordered.Count; i++)
            {
                ordered[i].DisplayIndex = i;
            }
        }
        finally
        {
            _suppressLayoutEvents = false;
        }
    }

    /// <summary>
    /// 按当前排序规则插入一行；未排序时按调用方指定的默认位置插入。
    /// </summary>
    /// <param name="item">已填好子项文本的行。</param>
    /// <param name="insertAtTopWhenUnsorted">
    /// 未排序时是否插到最上面。下载列表传 true（新任务在上），页面视频传 false（保持追加）。
    /// </param>
    public void InsertItemSorted(ListViewItem item, bool insertAtTopWhenUnsorted = true)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (_sortColumnName is null)
        {
            if (insertAtTopWhenUnsorted)
            {
                Items.Insert(0, item);
            }
            else
            {
                Items.Add(item);
            }

            return;
        }

        var index = 0;
        for (; index < Items.Count; index++)
        {
            // 相等键插到相等段最前，保持「新在上」
            if (CompareItems(item, Items[index]) <= 0)
            {
                break;
            }
        }

        Items.Insert(index, item);
    }

    /// <summary>
    /// 刷新指定行中某个子项的文字。
    /// </summary>
    /// <param name="item">目标行。</param>
    /// <param name="index">子项索引。</param>
    /// <param name="text">新文本。</param>
    /// <remarks>
    /// <para>文本未变化时不赋值，避免触发无谓的重绘。</para>
    /// <para>
    /// 索引越界时不做任何事：<see cref="ListViewItem.SubItems"/> 的越界赋值会被静默丢弃，
    /// 而视图层绝不能因为一列对不上就让程序崩溃。但「静默」正是这类问题的可怕之处 ——
    /// 曾出现过任务从「待下载」（4 列）迁移到「已下载」（5 列）后，
    /// 第 5 列的失败原因永远写不进去、用户完全看不到下载为何失败的情况。
    /// 因此调试期在这里直接断言失败，让列数不匹配尽早暴露；发布版仍保持静默。
    /// </para>
    /// </remarks>
    public static void SetSubItemText(ListViewItem item, int index, string text)
    {
        if (index < 0 || index >= item.SubItems.Count)
        {
            Debug.Fail($"子项索引 {index} 越界（该行当前只有 {item.SubItems.Count} 个子项）。请检查行的列数是否与所在列表一致。");
            return;
        }

        if (!string.Equals(item.SubItems[index].Text, text, StringComparison.Ordinal))
        {
            item.SubItems[index].Text = text;
        }
    }

    /// <summary>
    /// 补齐一行的子项数量，使其与目标列表的列数一致。
    /// </summary>
    /// <param name="item">目标行。</param>
    /// <param name="list">行将要挂载到的列表。</param>
    /// <remarks>
    /// 行是在首次出现时按「当时所在页签」的列数创建的，而三个页签的列数并不相同
    /// （待下载 4 列，正在下载与已下载各 5 列）。任务跨页签迁移时必须先补齐，
    /// 否则写入多出来的那一列会被静默丢弃。
    /// </remarks>
    public static void EnsureSubItems(ListViewItem item, ListView list)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(list);

        while (item.SubItems.Count < list.Columns.Count)
        {
            item.SubItems.Add(string.Empty);
        }
    }

    private void OnColumnClick(object? sender, ColumnClickEventArgs e)
    {
        if (e.Column < 0 || e.Column >= Columns.Count)
        {
            return;
        }

        var column = Columns[e.Column];
        if (column.Tag is not string name || !_keySelectors.ContainsKey(name))
        {
            return;
        }

        if (string.Equals(_sortColumnName, name, StringComparison.Ordinal))
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortColumnName = name;
            _sortAscending = true;
        }

        ApplySort();
    }

    private void ApplySort()
    {
        if (_sortColumnName is null)
        {
            return;
        }

        _sortColumnIndex = FindColumnIndex(_sortColumnName);
        UpdateSortIndicators();

        // 设置 sorter 会立即排序；排完立刻清空，避免后续 Items.Add 被自动排序插入
        ListViewItemSorter = new ItemComparer(this);
        Sort();
        ListViewItemSorter = null;
    }

    private int FindColumnIndex(string name)
    {
        for (var i = 0; i < Columns.Count; i++)
        {
            if (string.Equals(Columns[i].Tag as string, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private int CompareItems(ListViewItem left, ListViewItem right)
    {
        if (_sortColumnName is null || _sortColumnIndex < 0)
        {
            return 0;
        }

        var keySelector = _keySelectors[_sortColumnName];
        var leftKey = keySelector(GetSubItemText(left, _sortColumnIndex));
        var rightKey = keySelector(GetSubItemText(right, _sortColumnIndex));

        if (leftKey is null || rightKey is null)
        {
            // 解析失败一律排末尾，不随升降序翻转
            if (leftKey is null && rightKey is null)
            {
                return 0;
            }

            return leftKey is null ? 1 : -1;
        }

        var result = CompareKeys(leftKey, rightKey);
        return _sortAscending ? result : -result;
    }

    private static string GetSubItemText(ListViewItem item, int index)
        => index < item.SubItems.Count ? item.SubItems[index].Text : string.Empty;

    private static int CompareKeys(IComparable left, IComparable right)
    {
        if (left is string leftText && right is string rightText)
        {
            return StringComparer.OrdinalIgnoreCase.Compare(leftText, rightText);
        }

        try
        {
            return left.CompareTo(right);
        }
        catch (ArgumentException)
        {
            // 极端情况下同一列混入不同类型（例如手工改坏的持久化数据），退化为文本比较
            return StringComparer.OrdinalIgnoreCase.Compare(left.ToString(), right.ToString());
        }
    }

    private void UpdateSortIndicators()
    {
        foreach (var column in Columns.Cast<ColumnHeader>())
        {
            if (column.Tag is not string name)
            {
                continue;
            }

            var arrow = string.Equals(name, _sortColumnName, StringComparison.Ordinal)
                ? _sortAscending ? " ▲" : " ▼"
                : string.Empty;

            var text = name + arrow;
            if (!string.Equals(column.Text, text, StringComparison.Ordinal))
            {
                column.Text = text;
            }
        }
    }

    private void OnColumnWidthChanged(object? sender, ColumnWidthChangedEventArgs e)
    {
        if (!_suppressLayoutEvents)
        {
            LayoutChanged?.Invoke(this, GetColumnLayout());
        }
    }

    private void OnColumnReordered(object? sender, ColumnReorderedEventArgs e)
    {
        if (_suppressLayoutEvents)
        {
            return;
        }

        // 该事件在重排生效前触发，DisplayIndex 尚未更新；等消息循环处理完再读布局
        if (IsHandleCreated)
        {
            BeginInvoke(() =>
            {
                if (!IsDisposed && IsHandleCreated)
                {
                    LayoutChanged?.Invoke(this, GetColumnLayout());
                }
            });
        }
    }

    private sealed class ItemComparer : IComparer
    {
        private readonly BufferedListView _owner;

        public ItemComparer(BufferedListView owner) => _owner = owner;

        public int Compare(object? x, object? y)
            => x is ListViewItem left && y is ListViewItem right
                ? _owner.CompareItems(left, right)
                : 0;
    }
}