/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Models
*文件名： SniffedVideo
*版本号： V1.0.0.0
*唯一标识：741735af-7d87-465d-9418-e1de945f7a5a
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:13:00
*描述：嗅探结果项模型，表示页面上被发现的一个可下载视频资源。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:13:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

namespace BrowserVideoGrabber.Core.Models;

/// <summary>
/// 嗅探结果项：表示页面上被发现的一个可下载视频资源。
/// </summary>
/// <remarks>
/// 由三条嗅探链路（网络响应监听 / URL 特征匹配 / JS 注入 Hook）统一产出，
/// 经 <c>VideoFamilyIndex</c> 按「同一视频」归并去重后进入界面列表。
/// 本类型为不可变对象，界面列表通过 <see cref="Id"/> 定位条目。
/// </remarks>
public sealed class SniffedVideo
{
    /// <summary>
    /// 嗅探项标识，用于界面列表定位与增量刷新。
    /// </summary>
    /// <remarks>
    /// 该值由「视频家族」决定而非随机生成（见 <see cref="Snapshot"/>）：
    /// 同一个视频的多级资源（主清单 / 媒体清单 / 分片）共用同一个标识，
    /// 界面上因此表现为「一行被原地刷新」，而不是同一个视频冒出好几行。
    /// </remarks>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>资源原始地址。下载时直接使用该地址，不做任何改写，避免破坏动态签名参数。</summary>
    public required string Url { get; init; }

    /// <summary>归一化后的地址，仅用于界面展示与去重比较，不参与实际请求。</summary>
    public string NormalizedUrl { get; init; } = string.Empty;

    /// <summary>资源格式，决定后续由哪个下载处理器处理。</summary>
    public required VideoFormat Format { get; init; }

    /// <summary>HTTP 响应头中的 Content-Type。仅网络响应链路嗅探到的资源才有值。</summary>
    public string? ContentType { get; init; }

    /// <summary>分辨率（形如 1920x1080）。从主播放列表的 #EXT-X-STREAM-INF 解析得到，可能为空。</summary>
    public string? Resolution { get; init; }

    /// <summary>码率（单位 bit/s）。从主播放列表的 #EXT-X-STREAM-INF 解析得到，可能为空。</summary>
    public long? Bandwidth { get; init; }

    /// <summary>嗅探来源标记，便于排查漏抓问题。取值：network（响应监听）/ url（特征匹配）/ jshook（脚本劫持）。</summary>
    public string Source { get; init; } = "url";

    /// <summary>
    /// 当前页面标题（来自所属 WebView2 的 <c>DocumentTitle</c>）。
    /// </summary>
    /// <remarks>
    /// 多标签页下每个嗅探器绑定自己的 <c>WebView2</c>，因此该值自然对应各自所在页面。
    /// 下载文件默认以它为文件名（截断 15 字符）；为空时回退到 <see cref="DisplayTitle"/>。
    /// </remarks>
    public string? PageTitle { get; init; }

    /// <summary>
    /// 视频总时长（秒）。
    /// </summary>
    /// <remarks>
    /// 只有读到播放列表正文才能算出：媒体清单直接累加 <c>#EXTINF</c>；
    /// 主清单本身不含分片，需再取一次最高码率变体的清单才能得到。
    /// 因此该值可能为空，界面应显示「-」而不是 0 —— 0 会被误读成「时长为零」。
    /// </remarks>
    public double? DurationSeconds { get; init; }

    /// <summary>
    /// 资源字节数，来自响应头的 <c>Content-Length</c>。
    /// </summary>
    /// <remarks>
    /// 只对单文件资源（如 MP4）有意义。m3u8 的该值是<b>清单文本本身</b>的大小（通常几百字节），
    /// 与视频体积毫无关系，绝不能当作视频大小展示。
    /// </remarks>
    public long? ContentLength { get; init; }

    /// <summary>发现时间。</summary>
    public DateTimeOffset DetectedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>
    /// 估算的视频体积（字节）；信息不足时为 null。
    /// </summary>
    /// <remarks>
    /// <para>
    /// m3u8 无法直接得知成品大小：清单里既没有总字节数，分片数量与每片大小也只有逐片请求才知道。
    /// 这里用「时长 × 声明码率 ÷ 8」推算，误差主要取决于码率声明是否接近实际值。
    /// </para>
    /// <para>
    /// 单文件资源（MP4）直接用 <c>Content-Length</c>，那是精确值而非估算。
    /// </para>
    /// </remarks>
    public long? EstimatedBytes
    {
        get
        {
            if (Format == VideoFormat.Mp4 && ContentLength is > 0)
            {
                return ContentLength;
            }

            if (DurationSeconds is > 0 && Bandwidth is > 0)
            {
                return (long)Math.Ceiling(DurationSeconds.Value * Bandwidth.Value / 8d);
            }

            return null;
        }
    }

    /// <summary>
    /// <see cref="EstimatedBytes"/> 是否为估算值（而非服务端声明的精确长度）。
    /// </summary>
    /// <remarks>界面据此决定是否加上「≈」前缀，避免把推算值呈现得像实测值一样可信。</remarks>
    public bool IsSizeEstimated
        => EstimatedBytes.HasValue && !(Format == VideoFormat.Mp4 && ContentLength is > 0);

    /// <summary>
    /// 展示用短标题：取归一化地址的最后一段路径，路径为空时回退为完整地址。
    /// </summary>
    /// <remarks>
    /// 流媒体地址的文件名常常是毫无信息量的通用名（<c>index.m3u8</c>、<c>playlist.m3u8</c>、
    /// <c>manifest.mpd</c>…）。同一个页面上若有多个视频，列表里就会出现多个同名条目而无法分辨。
    /// 因此当文件名命中通用名集合时，额外带上父目录名（形如 <c>720p/index.m3u8</c>）。
    /// </remarks>
    public string DisplayTitle
    {
        get
        {
            // 归一化地址可能为空（构造时未赋值），此时直接回退到原始地址
            var source = string.IsNullOrWhiteSpace(NormalizedUrl) ? Url : NormalizedUrl;
            if (string.IsNullOrWhiteSpace(source))
            {
                return string.Empty;
            }

            var slashIndex = source.LastIndexOf('/');
            var tail = slashIndex >= 0 && slashIndex < source.Length - 1
                ? source[(slashIndex + 1)..]
                : source;

            if (string.IsNullOrWhiteSpace(tail))
            {
                return source;
            }

            return QualifyGenericName(source, tail);
        }
    }

    /// <summary>
    /// 创建一个仅替换标识的新条目。
    /// </summary>
    /// <param name="id">新标识。</param>
    /// <returns>除标识外与原条目完全一致的新实例。</returns>
    /// <remarks>
    /// 由视频家族索引调用：同族条目必须共用家族派生的稳定标识，
    /// 界面才能把「分片行升级为清单行」表现为原地刷新。
    /// </remarks>
    public SniffedVideo WithId(Guid id) => new()
    {
        Id = id,
        Url = Url,
        NormalizedUrl = NormalizedUrl,
        Format = Format,
        ContentType = ContentType,
        Resolution = Resolution,
        Bandwidth = Bandwidth,
        Source = Source,
        PageTitle = PageTitle,
        DurationSeconds = DurationSeconds,
        ContentLength = ContentLength,
        DetectedAt = DetectedAt
    };

    /// <summary>通用播放列表文件名（不含扩展名）。命中时标题补上父目录名以便区分。</summary>
    private static readonly HashSet<string> GenericPlaylistNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "index",
        "index-fmp4",
        "playlist",
        "master",
        "manifest",
        "media",
        "main",
        "stream",
        "hls",
        "chunklist",
        "prog_index",
        "out",
        "video",
        "file"
    };

    /// <summary>
    /// 文件名缺少辨识度时，用父目录名加以限定。
    /// </summary>
    /// <param name="source">用于解析的完整地址。</param>
    /// <param name="tail">地址最后一段。</param>
    /// <returns>展示标题。</returns>
    private static string QualifyGenericName(string source, string tail)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            return tail;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return tail;
        }

        var name = Path.GetFileNameWithoutExtension(segments[^1]);
        if (!GenericPlaylistNames.Contains(name))
        {
            return tail;
        }

        var parent = segments[^2];

        // 父目录本身也是通用名时不再叠加，避免出现 index/index.m3u8 这种同样无法分辨的标题
        return GenericPlaylistNames.Contains(parent) ? tail : parent + "/" + segments[^1];
    }
}
