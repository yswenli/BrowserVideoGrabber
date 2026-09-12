/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Abstractions
*文件名： IFavoritesRepository
*版本号： V1.0.0.0
*唯一标识：8d40e8f6-522a-41b5-aaa6-794105abe20b
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 01:45:00
*描述：地址收藏仓储接口，隔离 JSON 持久化以便单元测试。
*
*=================================================
*修改标记
*修改时间：2026/9/13 01:45:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Core.Abstractions;

/// <summary>
/// 地址收藏仓储。
/// </summary>
/// <remarks>
/// 收藏纯由用户手动增删，接口同时提供「整体读写」与「单条增删改」两类方法，
/// 单条操作内部会先读后写（原子写入），保证并发安全。
/// </remarks>
public interface IFavoritesRepository
{
    /// <summary>载入全部收藏（按添加时间倒序）。</summary>
    /// <returns>收藏列表；文件损坏或不存在时返回空列表。</returns>
    IReadOnlyList<FavoriteEntry> Load();

    /// <summary>整体覆盖保存。</summary>
    /// <param name="items">待保存的收藏列表。</param>
    void Save(IReadOnlyList<FavoriteEntry> items);

    /// <summary>新增一条收藏（已存在相同网址则不重复添加）。</summary>
    /// <param name="item">待添加的收藏。</param>
    /// <returns>添加成功返回 true；网址已存在返回 false。</returns>
    bool Add(FavoriteEntry item);

    /// <summary>按标识删除收藏。</summary>
    /// <param name="id">收藏标识。</param>
    /// <returns>存在并删除返回 true。</returns>
    bool Remove(Guid id);

    /// <summary>按标识改名。</summary>
    /// <param name="id">收藏标识。</param>
    /// <param name="title">新标题。</param>
    /// <returns>存在并改名返回 true。</returns>
    bool Rename(Guid id, string title);

    /// <summary>是否存在相同网址的收藏。</summary>
    /// <param name="url">待查网址。</param>
    /// <returns>存在返回 true。</returns>
    bool ContainsUrl(string url);
}
