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

using System.Diagnostics;

namespace BrowserVideoGrabber.App.Controls;

/// <summary>
/// 开启双缓冲的 <see cref="ListView"/>。
/// </summary>
/// <remarks>
/// <para>
/// WinForms 的 <c>ListView</c> 默认不启用双缓冲，而下载进度列每 200 毫秒就会改写一次文本，
/// 高频重绘会产生肉眼可见的闪烁，长时间盯着会非常疲劳。
/// </para>
/// <para>
/// <c>DoubleBuffered</c> 是受保护的属性，无法直接从外部设置，
/// 因此通过派生类在构造函数中打开它，这是官方推荐的绕行方式。
/// </para>
/// </remarks>
internal sealed class BufferedListView : ListView
{
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
        HeaderStyle = ColumnHeaderStyle.Nonclickable;
    }

    /// <summary>
    /// 按「名称 + 宽度」批量创建列。
    /// </summary>
    /// <param name="columns">列定义：名称与宽度。</param>
    public void AddColumns(params (string Name, int Width)[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        foreach (var (name, width) in columns)
        {
            Columns.Add(name, width);
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
}
