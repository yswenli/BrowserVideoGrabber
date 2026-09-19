/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Downloads
*文件名： HlsDownloadPlan
*版本号： V1.0.0.0
*唯一标识：cbad5433-7108-46bc-bdc6-02023d68de80
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 03:46:00
*描述：HLS 下载计划模型，描述一次点播下载所需的全部要素。
*
*=================================================
*修改标记
*修改时间：2026/9/13 03:46:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

namespace BrowserVideoGrabber.Core.Downloads;

/// <summary>
/// HLS 点播下载计划。
/// </summary>
/// <remarks>
/// <para>
/// 该对象是「清单解析」与「分片下载」之间的稳定契约：前者产出它，后者只消费它。
/// 这样分片下载器完全不需要理解 m3u8 语法，测试时也可以直接手工构造计划，
/// 不必为了验证下载逻辑而去拼一份播放列表。
/// </para>
/// <para>
/// <b>为什么需要 <see cref="SegmentStartOffsets"/></b>：当部分分片被判定为占位内容而跳过时，
/// 必须能告诉用户「缺的是哪一段时间」。仅凭分片下标无法换算成时间，
/// 而这个前缀和数组恰好是「下标 → 时间轴位置」的映射，因此在构建计划时一次算好。
/// </para>
/// </remarks>
public sealed class HlsDownloadPlan
{
    /// <summary>媒体清单（而非主清单）的绝对地址，用于推导相对分片地址与日志定位。</summary>
    public required string MediaPlaylistUrl { get; init; }

    /// <summary>分片序列（按播放顺序）。</summary>
    public required IReadOnlyList<M3u8Segment> Segments { get; init; }

    /// <summary>
    /// 各分片在时间轴上的起始位置，与 <see cref="Segments"/> 一一对应。
    /// </summary>
    /// <remarks>形如 <c>[0s, 10s, 20s, ...]</c>，由分片时长累加得到。</remarks>
    public required IReadOnlyList<TimeSpan> SegmentStartOffsets { get; init; }

    /// <summary>加密方式。</summary>
    public M3u8Encryption Encryption { get; init; } = M3u8Encryption.None;

    /// <summary>AES-128 密钥地址。未加密时为 null。</summary>
    public string? KeyUri { get; init; }

    /// <summary>
    /// 清单声明的初始向量（形如 <c>0x0123...</c>）。未声明时为 null。
    /// </summary>
    /// <remarks>
    /// 为 null 时按 HLS 规范用「媒体序号 + 分片下标」推导，这一步由
    /// <c>Aes128Decryptor</c> 负责，计划本身不做推导，因为它不该持有解密知识。
    /// </remarks>
    public string? KeyIv { get; init; }

    /// <summary>清单声明的媒体序号（<c>#EXT-X-MEDIA-SEQUENCE</c>）。未声明时为 0。</summary>
    public long MediaSequence { get; init; }

    /// <summary>fMP4 播放列表的初始化分片地址（<c>#EXT-X-MAP</c>）。null 表示无需 init 段。</summary>
    public string? InitSegmentUri { get; init; }

    /// <summary>选中的清晰度（形如 <c>842x480</c>）。未知时为 null。</summary>
    public string? Resolution { get; init; }

    /// <summary>选中的声明码率（bit/s）。未知时为 0。</summary>
    public long Bandwidth { get; init; }

    /// <summary>分片时长累加得到的总时长。</summary>
    public TimeSpan TotalDuration { get; init; }

    /// <summary>分片总数。</summary>
    public int SegmentCount => Segments.Count;
}
