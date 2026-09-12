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
/// 判定优先级：<b>URL 明确指向视频格式 → URL 明确是图片/音频（仅协议专属 MIME 可翻案）→ Content-Type</b>。
/// URL 后缀是文件本身的扩展名，不受服务端 MIME 错标影响，因此优先级最高：
/// 无论 Content-Type 是 <c>audio/mpegurl</c>（m3u8 的标准 MIME）、<c>video/mp2t</c>
/// 还是 <c>text/plain</c> 等错标值，<c>.m3u8</c> 一律按清单处理。
/// Content-Type 只负责两件事：URL 无定论时识别格式；URL 是图片/音频后缀时
/// 用流媒体协议专属的强信号（如 <c>video/mp2t</c>）把伪装成分片的资源翻案。
/// </para>
/// </remarks>
public static class VideoUrlMatcher
{
    /// <summary>
    /// 明确指向图片的后缀集合。命中即拒绝，不再检查 Content-Type。
    /// </summary>
    /// <remarks>
    /// 这是最可靠的拒绝证据：后缀是文件本身的扩展名，不受服务端 MIME 错误声明的影响。
    /// 覆盖常见的位图（jpeg/png/webp/gif/bmp/heic/avif）与矢量（svg/ico）。
    /// </remarks>
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".ico", ".heic", ".heif", ".avif", ".tif", ".tiff", ".svg"
    };

    /// <summary>明确指向音频的后缀集合，同样不是视频。</summary>
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a", ".opus", ".ape", ".aiff"
    };

    /// <summary>URL 后缀到格式的映射表。使用不区分大小写的比较器，兼容站点的大小写混用。</summary>
    private static readonly Dictionary<string, VideoFormat> ExtensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".m3u8"] = VideoFormat.M3u8,
        [".m3u"] = VideoFormat.M3u8,
        [".ts"] = VideoFormat.Ts,
        [".m4s"] = VideoFormat.M4s,
        [".mp4"] = VideoFormat.Mp4,
        [".mpd"] = VideoFormat.Mpd,
        [".ism"] = VideoFormat.Ismc,
        [".ismc"] = VideoFormat.Ismc
    };

    /// <summary>
    /// <b>强信号</b> Content-Type 映射：这些 MIME 是流媒体协议专属的，绝不会被误用到非视频资源上。
    /// 即便 URL 后缀看起来像图片，也信任 Content-Type。
    /// </summary>
    /// <remarks>
    /// 典型场景：某些 CDN 把 TS 分片伪装成 <c>.jpeg</c> 后缀（如 <c>video0.jpeg</c>），
    /// 但 Content-Type 正确返回 <c>video/mp2t</c> —— 这其实是有效的 TS 分片，必须接受。
    /// </remarks>
    private static readonly Dictionary<string, VideoFormat> StrongContentTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/vnd.apple.mpegurl"] = VideoFormat.M3u8,
        ["application/x-mpegurl"] = VideoFormat.M3u8,
        ["audio/mpegurl"] = VideoFormat.M3u8,
        ["audio/x-mpegurl"] = VideoFormat.M3u8,
        ["application/dash+xml"] = VideoFormat.Mpd,
        ["application/vnd.ms-sstr+xml"] = VideoFormat.Ismc,
        ["video/mp2t"] = VideoFormat.Ts
    };

    /// <summary>
    /// <b>弱信号</b> Content-Type 映射：这些 MIME 在极端情况下可能被 CDN 错误地给了封面图。
    /// 因此接受它们的前提是 URL 后缀也指向视频格式，双重验证防误报。
    /// </summary>
    private static readonly Dictionary<string, VideoFormat> WeakContentTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["video/mp4"] = VideoFormat.Mp4,
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

        // 先解析 URL 后缀信息，后面的判定会反复用到
        string? urlExtension = null;
        bool urlIsImage = false;
        bool urlIsAudio = false;
        bool urlIsVideoExtension = false;

        if (!string.IsNullOrWhiteSpace(url)
            && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            urlExtension = Path.GetExtension(uri.AbsolutePath);

            if (urlExtension.Length > 0)
            {
                urlIsImage = ImageExtensions.Contains(urlExtension);
                urlIsAudio = AudioExtensions.Contains(urlExtension);
                urlIsVideoExtension = ExtensionMap.ContainsKey(urlExtension);
            }
        }

        string? mediaType = NormalizeMediaType(contentType);

        // ========== 分支 1：URL 明确指向视频格式 → 无条件以 URL 为准 ==========
        // URL 后缀是文件自身的扩展名，是比 Content-Type 更可靠的证据。
        // m3u8 的标准 Content-Type 本就是 audio/mpegurl（以 audio/ 开头），
        // 各种 CDN 还会错标成 video/mp2t、text/plain 甚至 application/octet-stream ——
        // 因此绝不能先按 Content-Type 过滤，否则清单会被误杀。清单是清单，分片是分片。
        if (urlIsVideoExtension && urlExtension is not null)
        {
            format = ExtensionMap[urlExtension];
            return true;
        }

        // ========== 分支 2：URL 明确是图片/音频 → 仅协议专属强信号可翻案 ==========
        // 典型场景：CDN 把 TS 分片伪装成 .jpeg 后缀（如 video0.jpeg），
        // 但 Content-Type 正确返回 video/mp2t —— 这是有效的分片，必须接受；
        // 而弱信号（video/mp4）可能只是封面图被错标，不能翻案。
        if (urlIsImage || urlIsAudio)
        {
            if (mediaType is not null && StrongContentTypeMap.TryGetValue(mediaType, out var fragmentFormat))
            {
                format = fragmentFormat;
                return true;
            }

            return false;
        }

        // ========== 分支 3：URL 无定论 → 交给 Content-Type 判定 ==========
        if (mediaType is not null)
        {
            // 强信号 MIME（mp2t/mpegurl/dash+xml）是流媒体协议专属，绝不会被误用到封面图
            if (StrongContentTypeMap.TryGetValue(mediaType, out var strongFormat))
            {
                format = strongFormat;
                return true;
            }

            // 弱信号（video/mp4/iso.segment）在 URL 无定论时可以直接采纳
            if (WeakContentTypeMap.TryGetValue(mediaType, out var weakFormat))
            {
                format = weakFormat;
                return true;
            }

            // image/audio/text/font 绝不可能是视频 —— 直接拒绝
            if (mediaType.StartsWith("image/") || mediaType.StartsWith("audio/")
                || mediaType.StartsWith("text/") || mediaType.StartsWith("font/"))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// 把 Content-Type 规范化为不含参数的媒体类型。
    /// </summary>
    /// <param name="contentType">响应头中的 Content-Type，允许为 null。</param>
    /// <returns>小写、去参数的媒体类型；输入为空时返回 null。</returns>
    private static string? NormalizeMediaType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        return contentType.Split(';')[0].Trim().ToLowerInvariant();
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
