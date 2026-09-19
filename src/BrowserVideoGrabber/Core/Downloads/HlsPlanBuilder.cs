/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Downloads
*文件名： HlsPlanBuilder
*版本号： V1.0.0.0
*唯一标识：ed68178d-dfee-4016-92c6-f6da761f7787
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 03:47:00
*描述：HLS 下载计划构建器，把媒体清单文本翻译为可执行计划或明确的不支持原因。
*
*=================================================
*修改标记
*修改时间：2026/9/13 03:47:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

namespace BrowserVideoGrabber.Core.Downloads;

/// <summary>
/// 计划构建的结果：要么给出计划，要么给出「为什么不能下载」以及后续该怎么做。
/// </summary>
/// <remarks>
/// 三个布尔标志对应三种<b>处理方式不同</b>的不可下载情形，必须彼此区分：
/// <list type="bullet">
///   <item><description><see cref="IsMasterPlaylist"/>：还需要取一次媒体清单，调用方继续走。</description></item>
///   <item><description><see cref="IsLive"/>：C# 侧无法规划完整下载，应改走 ffmpeg 录制。</description></item>
///   <item><description><see cref="IsDrmProtected"/>：无论如何都下不了，应当直接告知用户。</description></item>
/// </list>
/// 若把它们合并成一个含糊的 <c>Success = false</c>，调用方就只能用错误文本做字符串匹配，
/// 那是最脆弱的耦合形式。
/// </remarks>
public sealed record HlsPlanResult
{
    /// <summary>是否成功产出可用计划。</summary>
    public bool Success { get; init; }

    /// <summary>产出的计划。成功时非空。</summary>
    public HlsDownloadPlan? Plan { get; init; }

    /// <summary>失败原因（中文描述）。成功时为空。</summary>
    public string? Error { get; init; }

    /// <summary>该失败是否值得重试。</summary>
    public bool IsRetryable { get; init; }

    /// <summary>是否为受 DRM 保护的内容。</summary>
    public bool IsDrmProtected { get; init; }

    /// <summary>是否为直播流（清单缺少 <c>#EXT-X-ENDLIST</c>）。</summary>
    public bool IsLive { get; init; }

    /// <summary>传入的是主清单，需要先解析清晰度变体再取媒体清单。</summary>
    public bool IsMasterPlaylist { get; init; }

    /// <summary>
    /// 构造成功结果。
    /// </summary>
    /// <param name="plan">下载计划。</param>
    /// <returns>成功结果。</returns>
    public static HlsPlanResult Ok(HlsDownloadPlan plan)
        => new() { Success = true, Plan = plan };

    /// <summary>
    /// 构造失败结果。
    /// </summary>
    /// <param name="error">失败原因（中文描述）。</param>
    /// <param name="isRetryable">是否值得重试，默认 true。</param>
    /// <param name="isDrmProtected">是否为 DRM 保护内容。</param>
    /// <returns>失败结果。DRM 情形强制标记为不可重试。</returns>
    public static HlsPlanResult Fail(string error, bool isRetryable = true, bool isDrmProtected = false)
        => new()
        {
            Error = error,
            IsRetryable = isDrmProtected ? false : isRetryable,
            IsDrmProtected = isDrmProtected
        };

    /// <summary>
    /// 表示「传入的是主清单」。
    /// </summary>
    /// <returns>带 <see cref="IsMasterPlaylist"/> 标志的结果。</returns>
    public static HlsPlanResult Master() => new() { IsMasterPlaylist = true, IsRetryable = true };

    /// <summary>
    /// 表示「这是一个直播流」。
    /// </summary>
    /// <returns>带 <see cref="IsLive"/> 标志的结果。</returns>
    /// <remarks>
    /// 不标注为可重试：重试不会让直播清单长出 <c>#EXT-X-ENDLIST</c>。
    /// 调用方看到该标志应改走 ffmpeg 录制路径，而不是再次尝试 C# 取片。
    /// </remarks>
    public static HlsPlanResult Live()
        => new()
        {
            IsLive = true,
            Error = "该地址是直播流（清单缺少 #EXT-X-ENDLIST），分片列表没有终点。",
            IsRetryable = false
        };
}

/// <summary>
/// HLS 下载计划构建器。
/// </summary>
/// <remarks>
/// <para>
/// 纯函数、无副作用、不触网：输入是「媒体清单文本 + 自身地址」，输出是计划或拒绝原因。
/// 把主清单的二级拉取留给调用方（<c>HlsDownloadHandler</c>），
/// 是因为那属于「取数据」而非「翻译数据」，混进来会让本类必须依赖 <c>IMediaFetcher</c>，
/// 从而失去「一行文本即可完整测试」这个最有价值的性质。
/// </para>
/// </remarks>
public static class HlsPlanBuilder
{
    /// <summary>
    /// 由媒体清单文本构建下载计划。
    /// </summary>
    /// <param name="mediaPlaylistText">媒体清单（含分片的那一层）原文，允许为 null 或空白。</param>
    /// <param name="mediaPlaylistUri">媒体清单自身的绝对地址，用于解析相对分片地址。</param>
    /// <param name="resolution">已选中的清晰度，由调用方从主清单回填。未知时传 null。</param>
    /// <param name="bandwidth">已选中的声明码率，由调用方从主清单回填。未知时传 0。</param>
    /// <returns>构建结果。永远不抛异常。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mediaPlaylistUri"/> 为 null 时抛出。</exception>
    public static HlsPlanResult Build(
        string? mediaPlaylistText,
        Uri mediaPlaylistUri,
        string? resolution = null,
        long bandwidth = 0)
    {
        ArgumentNullException.ThrowIfNull(mediaPlaylistUri);

        if (string.IsNullOrWhiteSpace(mediaPlaylistText))
        {
            return HlsPlanResult.Fail("未能获取播放列表内容（服务端返回空响应）。");
        }

        var playlist = M3u8Parser.Parse(mediaPlaylistText, mediaPlaylistUri);

        if (!playlist.IsValid)
        {
            return HlsPlanResult.Fail(
                "播放列表内容无法解析：可能是动态签名已过期（服务端返回了错误页），或该清单格式不受支持。");
        }

        // 主清单只描述清晰度，本身不含分片，需要调用方再取一次媒体清单
        if (playlist.IsMasterPlaylist)
        {
            return HlsPlanResult.Master();
        }

        if (playlist.IsDrmProtected)
        {
            return HlsPlanResult.Fail(
                "该内容受 DRM 保护（检测到 SAMPLE-AES 或 SESSION-KEY），本工具无法下载受保护的加密内容。",
                isRetryable: false,
                isDrmProtected: true);
        }

        if (playlist.Encryption == M3u8Encryption.Unknown)
        {
            return HlsPlanResult.Fail(
                "播放列表声明了无法识别的加密方式，无法确定解密参数，因此不能安全地下载。",
                isRetryable: false);
        }

        if (playlist.Encryption == M3u8Encryption.Aes128 && string.IsNullOrWhiteSpace(playlist.KeyUri))
        {
            return HlsPlanResult.Fail(
                "播放列表声明为 AES-128 加密，但未给出密钥地址，无法解密分片。",
                isRetryable: false);
        }

        if (playlist.IsLive)
        {
            return HlsPlanResult.Live();
        }

        return HlsPlanResult.Ok(new HlsDownloadPlan
        {
            MediaPlaylistUrl = mediaPlaylistUri.ToString(),
            Segments = playlist.Segments,
            SegmentStartOffsets = BuildStartOffsets(playlist.Segments),
            Encryption = playlist.Encryption,
            KeyUri = playlist.KeyUri,
            KeyIv = playlist.KeyIv,
            MediaSequence = playlist.MediaSequence,
            InitSegmentUri = playlist.InitSegmentUri,
            Resolution = resolution,
            Bandwidth = bandwidth,
            TotalDuration = playlist.TotalDuration
        });
    }

    /// <summary>
    /// 计算各分片在时间轴上的起始位置（时长前缀和）。
    /// </summary>
    /// <param name="segments">分片序列。</param>
    /// <returns>与输入等长的起始时间数组。</returns>
    /// <remarks>
    /// 未声明 <c>#EXTINF</c> 的分片时长为 0，会让后续所有偏移量偏小 —— 这是刻意的：
    /// 与其猜测时长，不如让「缺失区间」的换算结果保守一些，也不会误导用户。
    /// </remarks>
    private static TimeSpan[] BuildStartOffsets(IReadOnlyList<M3u8Segment> segments)
    {
        var offsets = new TimeSpan[segments.Count];
        var accumulated = TimeSpan.Zero;

        for (var index = 0; index < segments.Count; index++)
        {
            offsets[index] = accumulated;
            accumulated += TimeSpan.FromSeconds(segments[index].DurationSeconds);
        }

        return offsets;
    }
}
