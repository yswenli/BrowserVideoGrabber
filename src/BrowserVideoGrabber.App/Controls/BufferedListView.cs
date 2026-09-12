/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
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
    /// <remarks>文本未变化时不赋值，避免触发无谓的重绘。</remarks>
    public static void SetSubItemText(ListViewItem item, int index, string text)
    {
        if (index < 0 || index >= item.SubItems.Count)
        {
            return;
        }

        if (!string.Equals(item.SubItems[index].Text, text, StringComparison.Ordinal))
        {
            item.SubItems[index].Text = text;
        }
    }
}
