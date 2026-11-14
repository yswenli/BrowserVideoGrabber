/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Models
*文件名： ColumnLayout
*版本号： V1.0.0.0
*唯一标识：8b1f6d4e-2c7a-4e93-b5d8-1a3c6f9e2d47
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/11/15 10:00:00
*描述：列布局 DTO，持久化单个列表的列顺序与列宽。
*
*=================================================
*修改标记
*修改时间：2026/11/15 10:00:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

namespace BrowserVideoGrabber.Core.Models;

/// <summary>
/// 单个列表的列布局（列顺序 + 列宽）。
/// </summary>
/// <remarks>
/// <see cref="Columns"/> 的数组顺序即显示顺序；列名使用 <c>ColumnHeader.Tag</c>
/// 中保存的稳定中文常量，不受排序箭头文本影响。
/// </remarks>
public sealed class ColumnLayout
{
    /// <summary>列定义，数组顺序即显示顺序。</summary>
    public List<ColumnLayoutEntry> Columns { get; set; } = [];
}

/// <summary>
/// 列布局中的一列。
/// </summary>
public sealed class ColumnLayoutEntry
{
    /// <summary>稳定列名（对应 <c>ColumnHeader.Tag</c>）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>列宽（像素）。</summary>
    public int Width { get; set; }
}