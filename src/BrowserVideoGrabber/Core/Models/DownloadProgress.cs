/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Models
*文件名： DownloadProgress
*版本号： V1.0.0.0
*唯一标识：087a2b27-13e6-4e61-b265-914c95e9c6ef
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:17:00
*描述：下载进度快照模型，统一承载 ffmpeg 与原生 HTTP 两条链路的进度数据。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:17:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

namespace BrowserVideoGrabber.Core.Models;

/// <summary>
/// 下载进度快照。ffmpeg 链路与原生 HTTP 链路统一产出该对象，界面无需区分来源。
/// </summary>
/// <remarks>
/// 之所以用不可变记录（record）而非可变类，是因为进度对象会跨线程从下载线程
/// 传递到 UI 线程，不可变可以彻底避免「读的时候被改了一半」的撕裂问题。
/// </remarks>
public sealed record DownloadProgress
{
    /// <summary>已完成百分比（0~100）。当总时长/总字节数未知时固定为 0。</summary>
    public double Percent { get; init; }

    /// <summary>是否已知总大小（ffmpeg 链路为已知总时长，HTTP 链路为已知 Content-Length）。</summary>
    public bool HasTotal { get; init; }

    /// <summary>ffmpeg 链路：已处理的媒体时长。</summary>
    public TimeSpan? Processed { get; init; }

    /// <summary>ffmpeg 链路：媒体总时长。</summary>
    public TimeSpan? Total { get; init; }

    /// <summary>HTTP 链路：已下载字节数。</summary>
    public long DownloadedBytes { get; init; }

    /// <summary>HTTP 链路：待下载总字节数，未知时为空。</summary>
    public long? TotalBytes { get; init; }

    /// <summary>HTTP 链路：瞬时下载速度（字节/秒），未知时为 0。</summary>
    public double BytesPerSecond { get; init; }

    /// <summary>ffmpeg 链路：处理速度倍率（如 2.1 表示 2.1 倍速）。未知时为 0。</summary>
    public double SpeedMultiplier { get; init; }

    /// <summary>
    /// 构造一个「总量未知」的进度对象，仅携带已处理量。
    /// </summary>
    /// <param name="processed">已处理的媒体时长。</param>
    /// <returns>百分比为 0、<see cref="HasTotal"/> 为 false 的进度快照。</returns>
    public static DownloadProgress FromProcessed(TimeSpan processed)
        => new() { Processed = processed, Percent = 0d, HasTotal = false };
}
