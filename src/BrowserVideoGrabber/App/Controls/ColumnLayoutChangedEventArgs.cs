/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.App.Controls
*文件名： ColumnLayoutChangedEventArgs
*版本号： V1.0.0.0
*唯一标识：0092f471-3d7f-4fe4-93af-49513b07d765
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/11/15 11:00:00
*描述：列布局变化事件参数：标明哪个列表发生了变化。
*
*=================================================
*修改标记
*修改时间：2026/11/15 11:00:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.App.Controls;

/// <summary>
/// 列布局变化事件参数：标明哪个列表发生了变化。
/// </summary>
/// <remarks>
/// 面板把底层列表的布局变化原样转发给宿主，同时补上本列表在
/// <c>column-layouts.json</c> 中的键，宿主据此决定写入哪个条目。
/// </remarks>
public sealed class ColumnLayoutChangedEventArgs : EventArgs
{
    /// <summary>
    /// 初始化事件参数。
    /// </summary>
    /// <param name="key">列表在 <c>column-layouts.json</c> 中的键。</param>
    /// <param name="layout">变化后的布局快照。</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> 或 <paramref name="layout"/> 为 null。</exception>
    /// <exception cref="ArgumentException"><paramref name="key"/> 为空或空白。</exception>
    public ColumnLayoutChangedEventArgs(string key, ColumnLayout layout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(layout);

        Key = key;
        Layout = layout;
    }

    /// <summary>列表在 <c>column-layouts.json</c> 中的键。</summary>
    public string Key { get; }

    /// <summary>变化后的布局快照。</summary>
    public ColumnLayout Layout { get; }
}