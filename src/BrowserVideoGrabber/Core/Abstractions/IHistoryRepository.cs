/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Abstractions
*文件名： IHistoryRepository
*版本号： V1.0.0.0
*唯一标识：ad024054-1c5b-4da1-8980-38cb09413aea
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 01:45:00
*描述：历史记录仓储接口，隔离 JSON 持久化以便单元测试。
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
/// 历史记录仓储。
/// </summary>
/// <remarks>
/// 历史由浏览器在导航成功时自动写入；<see cref="Record"/> 内部完成「连续同网址去重 +
/// 容量上限环形淘汰」，调用方无需关心这些细节。
/// </remarks>
public interface IHistoryRepository
{
    /// <summary>载入全部历史（按访问时间倒序）。</summary>
    /// <returns>历史列表；文件损坏或不存在时返回空列表。</returns>
    IReadOnlyList<HistoryEntry> Load();

    /// <summary>整体覆盖保存。</summary>
    /// <param name="items">待保存的历史列表（倒序）。</param>
    void Save(IReadOnlyList<HistoryEntry> items);

    /// <summary>
    /// 记录一次访问：与最新一条相同网址时只更新时间；否则插入到最前；超出容量时淘汰最旧。
    /// </summary>
    /// <param name="title">页面标题。</param>
    /// <param name="url">页面网址。</param>
    void Record(string title, string url);

    /// <summary>按标识删除单条。</summary>
    /// <param name="id">历史标识。</param>
    /// <returns>存在并删除返回 true。</returns>
    bool Remove(Guid id);

    /// <summary>清空全部历史。</summary>
    void Clear();
}
