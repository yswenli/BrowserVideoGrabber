/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Models
*文件名： FavoriteEntry
*版本号： V1.0.0.0
*唯一标识：ffb980a9-3f43-4534-89d0-3032a3354be3
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 01:45:00
*描述：地址收藏条目，表示用户手动收藏的一个站点（名称 + 网址 + 添加时间）。
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
/// 地址收藏条目。
/// </summary>
/// <remarks>
/// 单级结构（不做分组文件夹）：每条记录一个站点，含可改名的标题与网址。
/// 收藏纯由用户手动增删，无容量上限；JSON 持久化时按 <see cref="AddedAt"/> 倒序展示。
/// </remarks>
public sealed class FavoriteEntry
{
    /// <summary>稳定标识，用于列表定位与改名/删除。</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>展示标题，初始为收藏时的页面标题，用户可在对话框中改名。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>收藏的网址。</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>添加时间。</summary>
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;
}
