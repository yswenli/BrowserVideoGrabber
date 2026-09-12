/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Sniffing
*文件名： VideoUrlMatcher
*版本号： V1.0.0.0
*唯一标识：31757892-817e-4a14-9f2d-fbc04364922c
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:50:00
*描述：视频地址匹配器，依据响应内容类型与 URL 特征识别视频资源并生成去重指纹。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:50:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Text;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Core.Sniffing;

/// <summary>
/// 视频地址匹配器：把「一条原始请求/响应信息」判定为「一个可下载的视频资源」。
/// </summary>
/// <remarks>
/// <para>
/// 它是三条嗅探链路（网络响应监听 / URL 特征匹配 / JS 注入 Hook）共用的收口点，
/// 所有链路产出的原始信息都必须经过本类过滤，才能保证界面列表不会混入图片、脚本等噪声。
/// </para>
/// <para>
/// 判定优先级：<b>先看 Content-Type，再看 URL 后缀</b>。
/// 这样做的原因是：很多流媒体地址伪装成 <c>.jpg</c> 或以无扩展名结尾，
/// 而响应头中的媒体类型是站点自己声明的，可信度更高。
/// </para>
/// </remarks>
public static class VideoUrlMatcher
{
    /// <summary>URL 后缀到格式的映射表。使用不区分大小写的比较器，兼容站点的大小写混用。</summary>
    private static readonly Dictionary<string, VideoFormat> ExtensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".m3u8"] = VideoFormat.M3u8,
        [".m3u"] = VideoFormat.M3u8,
        [".ts"] = VideoFormat.Ts,
        [".m4s"] = VideoFormat.M4s,
        [".mp4"] = VideoFormat.Mp4,
        [".mpd"] = VideoFormat.Mpd
    };

    /// <summary>
    /// Content-Type 到格式的映射表。
    /// </summary>
    /// <remarks>
    /// 刻意不收录 <c>application/octet-stream</c>：它是通用二进制类型，
    /// 图片、压缩包、字体都可能是它，收进来会造成大量误报。
    /// </remarks>
    private static readonly Dictionary<string, VideoFormat> ContentTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/vnd.apple.mpegurl"] = VideoFormat.M3u8,
        ["application/x-mpegurl"] = VideoFormat.M3u8,
        ["audio/mpegurl"] = VideoFormat.M3u8,
        ["audio/x-mpegurl"] = VideoFormat.M3u8,
        ["application/dash+xml"] = VideoFormat.Mpd,
        ["video/mp4"] = VideoFormat.Mp4,
        ["video/mp2t"] = VideoFormat.Ts,
        ["video/iso.segment"] = VideoFormat.M4s
    };

    /// <summary>
    /// 判断给定地址与内容类型是否构成一个可下载的视频资源。
    /// </summary>
    /// <param name="url">资源地址，允许为 null（仅有响应头而无地址的场景）。</param>
    /// <param name="contentType">HTTP 响应头中的 Content-Type，允许为 null。</param>
    /// <param name="format">输出参数：识别到的资源格式；未识别时为 <see cref="VideoFormat.Unknown"/>。</param>
    /// <returns>识别成功返回 true。</returns>
    public static bool TryMatch(string? url, string? contentType, out VideoFormat format)
    {
        format = VideoFormat.Unknown;

        // 第一优先级：响应头声明的媒体类型
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            // Content-Type 可能形如 "application/vnd.apple.mpegurl; charset=utf-8"，需剥离参数部分
            var mediaType = contentType.Split(';')[0].Trim();
            if (ContentTypeMap.TryGetValue(mediaType, out var formatFromContentType))
            {
                format = formatFromContentType;
                return true;
            }
        }

        // 第二优先级：URL 后缀
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        // 只处理 HTTP(S)：data:、blob:、file: 等协议无法直接交给下载器或 ffmpeg
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        // 必须用 AbsolutePath 取后缀：直接用原始地址会把 "?token=1" 或 "#t=10" 当成后缀的一部分
        var extension = Path.GetExtension(uri.AbsolutePath);
        if (string.IsNullOrEmpty(extension))
        {
            return false;
        }

        if (ExtensionMap.TryGetValue(extension, out var formatFromUrl))
        {
            format = formatFromUrl;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 归一化地址，用于界面展示与去重比较。
    /// </summary>
    /// <param name="url">原始地址。</param>
    /// <returns>
    /// 形如 <c>https://host[:port]/path</c> 的归一化结果：协议与主机小写、去除默认端口、
    /// 去掉 query 与 fragment，但<b>保留路径大小写</b>（部分服务端路径区分大小写）。
    /// 无法解析时原样返回去除首尾空白后的文本。
    /// </returns>
    /// <remarks>
    /// 注意：归一化结果<b>不用于发起请求</b>。真实请求必须使用原始地址，
    /// 因为 query 中往往携带必要的动态签名参数。
    /// </remarks>
    public static string Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return trimmed;
        }

        var builder = new StringBuilder();
        builder.Append(uri.Scheme.ToLowerInvariant())
               .Append("://")
               .Append(uri.Host.ToLowerInvariant());

        // 默认端口（http 的 80、https 的 443）不展示，非默认端口必须保留，否则会指向错误服务
        if (!uri.IsDefaultPort)
        {
            builder.Append(':').Append(uri.Port);
        }

        builder.Append(uri.AbsolutePath);
        return builder.ToString();
    }

    /// <summary>
    /// 生成去重指纹。
    /// </summary>
    /// <param name="url">原始地址。</param>
    /// <returns>归一化后整体转小写的字符串。</returns>
    /// <remarks>
    /// 指纹刻意忽略 query：流媒体地址普遍带有一次性 token，
    /// 同一个资源在播放过程中会被上报多次，若不忽略 query，界面列表会被同一条资源刷屏。
    /// 代价是大小写不同的两条路径（如 <c>/A.m3u8</c> 与 <c>/a.m3u8</c>）会被视为同一资源，
    /// 这在实践中极为罕见，可以接受。
    /// </remarks>
    public static string Fingerprint(string? url)
        => Normalize(url).ToLowerInvariant();
}
