/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Sniffing
*文件名： M3u8OwnershipCollector
*版本号： V1.0.0.0
*唯一标识：a47ed03d-fcc6-485e-8ccb-403d9a5f3877
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 03:24:00
*描述：汇总一份 m3u8 清单「拥有」的下级资源，供视频家族索引抑制同一个视频的变体与分片。
*
*=================================================
*修改标记
*修改时间：2026/9/13 03:24:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Downloads;

namespace BrowserVideoGrabber.Core.Sniffing;

/// <summary>
/// 一份 m3u8 清单所声明拥有的下级资源。
/// </summary>
/// <param name="OwnedUrls">精确地址：清晰度变体与 init 段。</param>
/// <param name="OwnedDirectories">目录：清单自身、各变体、各分片所在目录。</param>
public readonly record struct M3u8Ownership(
    IReadOnlyList<string> OwnedUrls,
    IReadOnlyList<string> OwnedDirectories);

/// <summary>
/// m3u8 清单归属收集器。
/// </summary>
/// <remarks>
/// <para>
/// 清单是「谁属于谁」这一关系的唯一权威来源：只有读过主清单的正文，
/// 才知道它下面挂着哪几档清晰度；只有读过媒体清单的正文，才知道分片落在哪个目录。
/// 因此嗅探器在读到正文后调用本类，把这份关系登记到家族索引中，
/// 后续到达的变体与分片就能被识别为「同一个视频的下级资源」而不再单独成行。
/// </para>
/// <para>
/// <b>为何目录也要收进来</b>：一个两小时的视频有上千个分片，逐个登记地址既占内存又无必要；
/// 而分片几乎总是与它所属的清单同目录（或位于清单下的子目录），按目录折叠一次即可覆盖全部。
/// </para>
/// <para>
/// <b>为何同时登记清单自身目录</b>：部分站点的分片与清单同级摆放，而变体清单又指向更深一层，
/// 只登记变体目录会漏掉清单同级的分片。多登记一层目录的代价仅是抑制噪声，
/// 而且只要存在分片，同一目录树里必然存在提供完整下载目标的清单条目，不会误伤可用资源。
/// </para>
/// </remarks>
public static class M3u8OwnershipCollector
{
    /// <summary>
    /// 收集一份清单所拥有的下级资源。
    /// </summary>
    /// <param name="playlist">解析结果。允许为 null 或无效清单。</param>
    /// <param name="playlistUrl">清单自身的地址。提供后会额外把它的所在目录登记为归属目录。</param>
    /// <returns>归属信息；无有效内容时两个集合均为空。</returns>
    public static M3u8Ownership Collect(M3u8Playlist? playlist, string? playlistUrl = null)
    {
        if (playlist is null || !playlist.IsValid)
        {
            return new M3u8Ownership(Array.Empty<string>(), Array.Empty<string>());
        }

        var ownedUrls = new List<string>();
        var ownedDirectories = new HashSet<string>(StringComparer.Ordinal);

        // 清单自身所在目录：分片常与清单同级摆放，只登记变体目录会漏掉这一批
        AddDirectory(ownedDirectories, playlistUrl);

        // 变体清单：必须精确登记，否则它们会各自成行，同一个视频就出现多档清晰度的多个条目
        foreach (var variant in playlist.Variants)
        {
            if (!string.IsNullOrWhiteSpace(variant.Uri))
            {
                ownedUrls.Add(variant.Uri);
                AddDirectory(ownedDirectories, variant.Uri);
            }
        }

        // init 段：fMP4 清单的初始化分片，单独下载毫无意义
        if (!string.IsNullOrWhiteSpace(playlist.InitSegmentUri))
        {
            ownedUrls.Add(playlist.InitSegmentUri!);
            AddDirectory(ownedDirectories, playlist.InitSegmentUri);
        }

        // 分片：按目录折叠
        foreach (var segment in playlist.Segments)
        {
            AddDirectory(ownedDirectories, segment.Uri);
        }

        return new M3u8Ownership(ownedUrls, ownedDirectories.ToList());
    }

    /// <summary>
    /// 把一个地址所在目录加入集合。
    /// </summary>
    /// <param name="directories">目标集合。</param>
    /// <param name="url">资源地址。</param>
    private static void AddDirectory(HashSet<string> directories, string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var normalized = VideoUrlMatcher.Fingerprint(url);
        var slashIndex = normalized.LastIndexOf('/');

        if (slashIndex > 0)
        {
            directories.Add(normalized[..slashIndex]);
        }
    }
}
