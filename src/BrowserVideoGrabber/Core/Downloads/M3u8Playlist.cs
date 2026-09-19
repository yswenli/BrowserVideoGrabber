/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Downloads
*文件名： M3u8Playlist
*版本号： V1.0.0.0
*唯一标识：326c8c82-2d46-444c-a26c-79dc70b77b77
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:52:00
*描述：m3u8 播放列表解析结果模型，含加密类型、清晰度变体、分片序列与 init 段。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:52:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

namespace BrowserVideoGrabber.Core.Downloads;

/// <summary>
/// m3u8 播放列表的加密方式。
/// </summary>
public enum M3u8Encryption
{
    /// <summary>未加密，或显式声明 METHOD=NONE。</summary>
    None = 0,

    /// <summary>AES-128 加密。密钥可通过 URI 直接获取，ffmpeg 能自动解密，本工具支持。</summary>
    Aes128 = 1,

    /// <summary>DRM 保护（SAMPLE-AES / SESSION-KEY / Widevine / PlayReady）。本工具不支持。</summary>
    Drm = 2,

    /// <summary>出现了 KEY 标签但加密方式无法识别，按「不可下载」保守处理。</summary>
    Unknown = 3
}

/// <summary>
/// 主播放列表中的一档清晰度变体。
/// </summary>
/// <param name="Uri">变体播放列表的绝对地址。</param>
/// <param name="Bandwidth">声明码率（bit/s）。未声明时为 0。</param>
/// <param name="Resolution">分辨率（形如 1920x1080）。未声明时为 null。</param>
public sealed record M3u8Variant(string Uri, long Bandwidth, string? Resolution);

/// <summary>
/// 媒体播放列表中的一个分片。
/// </summary>
/// <param name="Uri">分片绝对地址。</param>
/// <param name="DurationSeconds">分片时长（秒）。未声明时为 0。</param>
public sealed record M3u8Segment(string Uri, double DurationSeconds);

/// <summary>
/// m3u8 播放列表解析结果。
/// </summary>
/// <remarks>
/// 该对象承担两个职责：
/// 1）下载前的 DRM 预检（<see cref="IsDrmProtected"/>），用于在启动 ffmpeg 之前就告知用户「受保护内容」；
/// 2）为界面提供清晰度选项（<see cref="Variants"/>）与分片统计（<see cref="Segments"/>）。
/// </remarks>
public sealed class M3u8Playlist
{
    /// <summary>解析是否得到有效内容。分片与变体均为空时判定为无效。</summary>
    public bool IsValid { get; init; }

    /// <summary>是否为主播放列表（含 #EXT-X-STREAM-INF，指向多档清晰度的子列表）。</summary>
    public bool IsMasterPlaylist { get; init; }

    /// <summary>清晰度变体列表。媒体播放列表时为空。</summary>
    public IReadOnlyList<M3u8Variant> Variants { get; init; } = Array.Empty<M3u8Variant>();

    /// <summary>分片列表。主播放列表时为空。</summary>
    public IReadOnlyList<M3u8Segment> Segments { get; init; } = Array.Empty<M3u8Segment>();

    /// <summary>加密方式。</summary>
    public M3u8Encryption Encryption { get; init; } = M3u8Encryption.None;

    /// <summary>AES-128 密钥地址。未加密时为 null。</summary>
    public string? KeyUri { get; init; }

    /// <summary>
    /// 清单声明的初始向量（<c>#EXT-X-KEY</c> 的 <c>IV</c> 属性，形如 <c>0x0123…</c>）。未声明时为 null。
    /// </summary>
    /// <remarks>
    /// 该属性仅在 AES-128 下有意义。为 null 不代表不需要 IV，而是要求解密方按规范
    /// 用「媒体序号 + 分片下标」自行推导 —— 两者的语义差别必须保留，不能在解析阶段就填上默认值。
    /// </remarks>
    public string? KeyIv { get; init; }

    /// <summary>媒体序号（<c>#EXT-X-MEDIA-SEQUENCE</c>）。未声明时为 0。</summary>
    /// <remarks>该值参与 IV 推导，因此不能省略为 null：未声明的语义就是「从 0 开始」。</remarks>
    public long MediaSequence { get; init; }

    /// <summary>
    /// 是否为直播流。
    /// </summary>
    /// <remarks>
    /// 判定依据是「含分片且缺少 <c>#EXT-X-ENDLIST</c>」。主清单不含分片，恒为 false ——
    /// 否则「多清晰度点播」会被误判成直播，从而错走 ffmpeg 录制路径。
    /// </remarks>
    public bool IsLive { get; init; }

    /// <summary>fMP4（DASH 风格）播放列表的初始化分片地址（#EXT-X-MAP）。未声明时为 null。</summary>
    public string? InitSegmentUri { get; init; }

    /// <summary>分片时长累加得到的总时长。</summary>
    public TimeSpan TotalDuration { get; init; }

    /// <summary>是否为受 DRM 保护的内容。为 true 时下载器应直接拒绝并给出明确提示。</summary>
    public bool IsDrmProtected { get; init; }

    /// <summary>
    /// 表示「解析失败」的共享实例。
    /// </summary>
    /// <remarks>
    /// 单例复用可以避免每次解析失败都分配对象；由于本类不可变，共享是安全的。
    /// </remarks>
    public static M3u8Playlist Invalid { get; } = new() { IsValid = false };

    /// <summary>
    /// 清晰度最高的一档变体；无变体时返回 null。
    /// </summary>
    /// <remarks>
    /// 主清单中各档变体的排列顺序没有规范约束，不能想当然取第一个。
    /// 这里优先选带分辨率的、再比较声明码率，使界面展示的是「最高可用清晰度」，
    /// 而不是「恰好排在第一位的那档」。
    /// </remarks>
    public M3u8Variant? BestVariant
    {
        get
        {
            M3u8Variant? best = null;

            foreach (var variant in Variants)
            {
                if (best is null)
                {
                    best = variant;
                    continue;
                }

                var hasResolution = !string.IsNullOrEmpty(variant.Resolution);
                var bestHasResolution = !string.IsNullOrEmpty(best.Resolution);

                if ((hasResolution && !bestHasResolution)
                    || (hasResolution == bestHasResolution && variant.Bandwidth > best.Bandwidth))
                {
                    best = variant;
                }
            }

            return best;
        }
    }
}
