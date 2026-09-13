/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Models
*文件名： HistoryEntry
*版本号： V1.0.0.0
*唯一标识：cc343484-4055-4543-ba45-9c55be2bbc38
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 01:45:00
*描述：历史记录条目，表示用户访问过的一个页面（标题 + 网址 + 访问时间）。
*
*=================================================
*修改标记
*修改时间：2026/9/13 01:45:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

namespace BrowserVideoGrabber.Core.Models;

/// <summary>
/// 历史记录条目。
/// </summary>
/// <remarks>
/// 由浏览器在导航成功时自动写入；同一网址连续重复访问（刷新）只更新时间而不重复记，
/// 避免一次刷新刷爆历史。列表按 <see cref="VisitedAt"/> 倒序展示，并受容量上限（默认 500）环形淘汰。
/// </remarks>
public sealed class HistoryEntry
{
    /// <summary>稳定标识，用于单条删除。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>页面标题（来自标签的 DocumentTitle）。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>页面网址。</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>最近一次访问时间。</summary>
    public DateTimeOffset VisitedAt { get; set; } = DateTimeOffset.Now;
}
