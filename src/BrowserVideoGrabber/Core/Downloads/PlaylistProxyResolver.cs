/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Downloads
*文件名： PlaylistProxyResolver
*版本号： V1.0.0.0
*唯一标识：9b1d4c62-3e57-4a8f-b0c1-6d2e7f8a9b3c
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/11/21 01:05:00
*描述：把 `?url=<编码后的 m3u8>` 形式的包装播放地址还原为内层真实清单地址。
*
*=================================================
*修改标记
*修改时间：2026/11/21 01:05:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

namespace BrowserVideoGrabber.Core.Downloads;

/// <summary>
/// 播放地址代理解析：识别 <c>https://站点/?url=&lt;编码后的 m3u8&gt;</c> 这类包装地址。
/// </summary>
/// <remarks>
/// <para>
/// 部分站点把真实清单地址塞进自己的播放页/代理接口的查询参数里，嗅探到的也是这个包装地址。
/// 包装层通常会被 CDN 整段缓存若干小时，缓存副本里的 <c>auth_key</c> 等签名早已过期，
/// 而内层地址每次访问都会现签新地址。因此下载与预览都应优先使用内层真实清单；
/// 内层不可用时再退回包装地址（某些站点的包装层才是唯一带鉴权的入口）。
/// </para>
/// <para>
/// 仅在「外层本身不是 m3u8、内层是 http(s) 且路径以 <c>.m3u8</c> 结尾」时改写，
/// 避免把普通站点里恰好叫 <c>url</c> 的查询参数误当成媒体地址。
/// </para>
/// </remarks>
public static class PlaylistProxyResolver
{
    private const string UrlParameterName = "url";
    private const string PlaylistExtension = ".m3u8";

    /// <summary>
    /// 解析包装播放地址。
    /// </summary>
    /// <param name="url">原始地址。</param>
    /// <returns>内层真实清单地址；无法识别为包装地址时原样返回。</returns>
    public static string Resolve(string url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var outer)
            || !IsHttp(outer)
            || outer.AbsolutePath.EndsWith(PlaylistExtension, StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        var encoded = FindQueryValue(outer.Query, UrlParameterName);
        if (string.IsNullOrEmpty(encoded))
        {
            return url;
        }

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(encoded);
        }
        catch (UriFormatException)
        {
            return url;
        }

        if (!Uri.TryCreate(decoded, UriKind.Absolute, out var inner)
            || !IsHttp(inner)
            || !inner.AbsolutePath.EndsWith(PlaylistExtension, StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        return decoded;
    }

    /// <summary>
    /// 判断地址是否为 http/https。
    /// </summary>
    private static bool IsHttp(Uri uri)
        => uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;

    /// <summary>
    /// 在查询串中查找指定参数（参数名不区分大小写，取第一个非空值）。
    /// </summary>
    /// <param name="query">含前导 <c>?</c> 的查询串。</param>
    /// <param name="name">参数名。</param>
    /// <returns>未编码的参数值；不存在时返回 null。</returns>
    private static string? FindQueryValue(string query, string name)
    {
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator < 0 ? pair : pair[..separator];
            if (!key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return separator < 0 ? null : pair[(separator + 1)..];
        }

        return null;
    }
}