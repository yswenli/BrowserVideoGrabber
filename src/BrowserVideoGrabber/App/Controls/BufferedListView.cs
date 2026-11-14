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
    // ListView 设置列宽走 LVM_SETCOLUMNWIDTH，cx 经 MAKELPARAM 只保留低 16 位且按有符号解释：
    // 有效范围 1..32767。超过 32767 的低 16 位为负数，会被原生按 autosize 处理
    // （宽度随内容/表头变化而非固定值），65534/65535 更是 LVSCW_AUTOSIZE_USEHEADER/LVSCW_AUTOSIZE 哨兵值。
    // 因此上界必须是 32767，否则持久化的宽度既不是用户设置的值，读回时还会被原生改写。
    private const int MaxColumnWidth = 32767;

    private readonly Dictionary<string, Func<string, IComparable?>> _keySelectors = new(StringComparer.Ordinal);

    // 记录上次已知的列宽：排序时会改表头文本，WinForms 可能顺带触发 ColumnWidthChanged，
    // 用缓存识别这类宽度未真正变化的伪事件，避免误报 LayoutChanged。
    private readonly Dictionary<ColumnHeader, int> _knownColumnWidths = new();

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
    /// <exception cref="ArgumentException">列名为空或与已有列重名。</exception>
    /// <exception cref="ArgumentOutOfRangeException">列宽不在 1..<see cref="MaxColumnWidth"/> 之间。</exception>
    public void AddColumns(params (string Name, int Width, Func<string, IComparable?>? Key)[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        // 先校验全部列名、宽度与重名，全部通过后再真正添加，
        // 避免中途抛异常时留下半截列与不一致的键选择器缓存。
        // 去重集合从现有列的 Tag 收集（而不是 _keySelectors），这样 Columns.Clear() 之后仍可重建同名列。
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in Columns.Cast<ColumnHeader>())
        {
            if (column.Tag is string existing)
            {
                seen.Add(existing);
            }
        }

        foreach (var (name, width, _) in columns)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("列名不能为空。", nameof(columns));
            }

            if (width is < 1 or > MaxColumnWidth)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(columns),
                    width,
                    $"列宽必须在 1..{MaxColumnWidth} 之间。");
            }

            if (!seen.Add(name))
            {
                throw new ArgumentException($"列名重复：{name}。", nameof(columns));
            }
        }

        // 已建句柄时 Columns.Add 会触发 ColumnWidthChanged，而缓存里还没有新列的宽度，
        // 会连发 N 次只含部分列的残缺布局快照；这里整体抑制，结束后重建宽度缓存。
        var previous = _suppressLayoutEvents;
        _suppressLayoutEvents = true;
        try
        {
            foreach (var (name, width, key) in columns)
            {
                var header = Columns.Add(name, width);
                header.Tag = name;
                _keySelectors[name] = key ?? ColumnValueParser.ParseText;
            }
        }
        finally
        {
            _suppressLayoutEvents = previous;
            RefreshKnownColumnWidths();
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
                // 与 ApplyColumnLayout 对称：只写出合法宽度，避免无句柄时把越界值持久化，
                // 下次启动再被夹取，造成「存的值 ≠ 读回的值」。
                Width = Math.Clamp(column.Width, 1, MaxColumnWidth)
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

        // 保存并恢复原值，避免嵌套调用（例如批量加列期间应用布局）时提前解除抑制
        var previous = _suppressLayoutEvents;
        _suppressLayoutEvents = true;
        try
        {
            var byName = new Dictionary<string, ColumnHeader>(StringComparer.Ordinal);
            foreach (var column in Columns.Cast<ColumnHeader>())
            {
                // 损坏的布局/重复 Tag 也不能抛异常：首个同名列胜出
                if (column.Tag is string name)
                {
                    byName.TryAdd(name, column);
                }
            }

            var ordered = new List<ColumnHeader>();
            var used = new HashSet<ColumnHeader>();

            foreach (var entry in layout.Columns)
            {
                // 持久化文件可能被手工改坏（例如数组里混入 null），跳过即可，绝不能阻断启动
                if (entry is null
                    || string.IsNullOrWhiteSpace(entry.Name)
                    || !byName.TryGetValue(entry.Name, out var column)
                    || !used.Add(column))
                {
                    continue;
                }

                if (entry.Width > 0)
                {
                    // 0/负数视为未记录宽度，保留默认值
                    column.Width = Math.Clamp(entry.Width, 1, MaxColumnWidth);
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
            _suppressLayoutEvents = previous;

            // 程序化应用布局后同步宽度缓存，避免后续真实改宽被误判为伪事件
            RefreshKnownColumnWidths();
        }
    }

    /// <summary>
    /// 重建「已知列宽」缓存。
    /// </summary>
    /// <remarks>
    /// 批量加列、程序化应用布局之后必须整体刷新：这两个场景会连续触发多列宽度变化，
    /// 逐列缓存会漏掉尚未写入的新列，从而发出只含部分列的残缺布局快照。
    /// </remarks>
    private void RefreshKnownColumnWidths()
    {
        _knownColumnWidths.Clear();
        foreach (var column in Columns.Cast<ColumnHeader>())
        {
            _knownColumnWidths[column] = column.Width;
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
            InsertUnsortedItem(item, insertAtTopWhenUnsorted);
            return;
        }

        // 列集合可能在运行期变化，扫描前重新定位排序列，避免沿用失效的缓存索引
        _sortColumnIndex = FindColumnIndex(_sortColumnName);
        if (_sortColumnIndex < 0)
        {
            // 排序列已被移除：退化为未排序插入，而不是因为 CompareItems 恒为 0 而永远插到最前
            InsertUnsortedItem(item, insertAtTopWhenUnsorted);
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

    private void InsertUnsortedItem(ListViewItem item, bool insertAtTopWhenUnsorted)
    {
        if (insertAtTopWhenUnsorted)
        {
            Items.Insert(0, item);
        }
        else
        {
            Items.Add(item);
        }
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

        // 设置 sorter 会立即排序；排完立刻清空，避免后续 Items.Add 被自动排序插入。
        // 用 try/finally 保证排序抛异常时也不会把 sorter 留在控件上。
        try
        {
            // ListViewItemSorter 的 setter 内部已经调用 Sort()，无需再显式排序
            ListViewItemSorter = new ItemComparer(this);
        }
        finally
        {
            ListViewItemSorter = null;
        }
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

    /// <summary>
    /// 比较两段列文本。全函数：无论键选择器或 <see cref="IComparable.CompareTo"/> 抛出什么异常，
    /// 都退化为文本比较，绝不向调用方抛异常。
    /// </summary>
    /// <param name="leftText">左行文本。</param>
    /// <param name="rightText">右行文本。</param>
    /// <param name="keySelector">键选择器；为 null 时直接按文本比较。</param>
    /// <param name="ascending">是否升序。</param>
    /// <returns>负数表示 left 在前，正数表示 right 在前，0 表示相等。</returns>
    internal static int CompareTexts(
        string leftText,
        string rightText,
        Func<string, IComparable?>? keySelector,
        bool ascending)
    {
        try
        {
            if (keySelector is null)
            {
                return CompareText(leftText, rightText, ascending);
            }

            var leftKey = keySelector(leftText);
            var rightKey = keySelector(rightText);

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
            return ApplyDirection(result, ascending);
        }
        catch (Exception ex)
        {
            // 键选择器、CompareTo、ToString 等任意异常都退化为原始文本比较，
            // 因为本方法最终会被 ListView 原生比较回调调用，异常逃逸会终止进程。
            Debug.WriteLine($"列排序键比较失败，退化为文本比较：{ex}");
            return CompareText(leftText, rightText, ascending);
        }
    }

    /// <summary>
    /// 按升降序调整比较结果的方向。
    /// </summary>
    /// <remarks>
    /// 用 <see cref="Math.Sign"/> 归一化，避免对 <see cref="int.MinValue"/> 取负时溢出，
    /// 破坏比较函数的反对称性（可能导致排序抛「比较函数不一致」异常）。
    /// </remarks>
    private static int ApplyDirection(int result, bool ascending)
    {
        var sign = Math.Sign(result);
        return ascending ? sign : -sign;
    }

    private static int CompareText(string leftText, string rightText, bool ascending)
    {
        var result = StringComparer.OrdinalIgnoreCase.Compare(leftText, rightText);
        return ApplyDirection(result, ascending);
    }

    internal int CompareItems(ListViewItem left, ListViewItem right)
    {
        if (_sortColumnName is null || _sortColumnIndex < 0)
        {
            return 0;
        }

        try
        {
            // 取不到选择器时传 null，退化为文本比较，避免索引器抛 KeyNotFoundException
            _keySelectors.TryGetValue(_sortColumnName, out var keySelector);

            var leftText = GetSubItemText(left, _sortColumnIndex);
            var rightText = GetSubItemText(right, _sortColumnIndex);
            return CompareTexts(leftText, rightText, keySelector, _sortAscending);
        }
        catch (Exception ex)
        {
            // 该方法会被 ListView 原生比较回调（CompareFunc → CallWindowProc）调用，
            // 异常一旦逃逸到原生代码就会直接终止整个进程。这里是最后兜底。
            Debug.WriteLine($"行比较失败，按相等处理：{ex}");
            return 0;
        }
    }

    private static string GetSubItemText(ListViewItem item, int index)
        => index >= 0 && index < item.SubItems.Count ? item.SubItems[index].Text : string.Empty;

    private static int CompareKeys(IComparable left, IComparable right)
    {
        if (left is string leftText && right is string rightText)
        {
            return StringComparer.OrdinalIgnoreCase.Compare(leftText, rightText);
        }

        // 不同类型混入时 CompareTo 会抛异常，由 CompareTexts 的外层 catch 统一兜底
        return left.CompareTo(right);
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
        // 批量加列或程序化应用布局期间不处理单列事件：
        // 后者会在 finally 里统一 RefreshKnownColumnWidths，逐列处理只会做无用的整表刷新。
        if (_suppressLayoutEvents)
        {
            return;
        }

        if (e.ColumnIndex < 0 || e.ColumnIndex >= Columns.Count)
        {
            return;
        }

        var column = Columns[e.ColumnIndex];
        if (_knownColumnWidths.TryGetValue(column, out var known) && known == column.Width)
        {
            // 排序改表头文本时 WinForms 也会发 HDN_ITEMCHANGED，宽度没变就是伪事件
            return;
        }

        if (_knownColumnWidths.Count != Columns.Count)
        {
            RefreshKnownColumnWidths();
        }
        else
        {
            _knownColumnWidths[column] = column.Width;
        }

        LayoutChanged?.Invoke(this, GetColumnLayout());
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
            try
            {
                BeginInvoke(() =>
                {
                    // 重排事件已投递、回调尚未执行期间，调用方可能刚好应用了一份布局；
                    // 此时抑制标志已恢复，快照也已过期，直接丢弃这次通知。
                    if (!_suppressLayoutEvents && !IsDisposed && !Disposing && IsHandleCreated)
                    {
                        LayoutChanged?.Invoke(this, GetColumnLayout());
                    }
                });
            }
            catch (InvalidOperationException ex)
            {
                // IsHandleCreated 与 BeginInvoke 之间句柄可能刚好销毁，忽略这次通知即可
                Debug.WriteLine($"列重排后投递布局通知失败（句柄已销毁？）：{ex.Message}");
            }
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