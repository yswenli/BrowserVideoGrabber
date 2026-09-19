/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.App.Formatting
*文件名： DisplayText
*版本号： V1.0.0.0
*唯一标识：a3d81e57-4f9c-4a26-b7d1-6e2b8c5a3f47
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 01:14:00
*描述：界面文本格式化工具，集中处理格式名、状态名、体积与速度的可读化转换。
*
*=================================================
*修改标记
*修改时间：2026/9/13 01:14:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.App.Formatting;

/// <summary>
/// 界面文本格式化工具。
/// </summary>
/// <remarks>
/// 把「枚举 / 字节数 / 速度」到「中文字符串」的转换集中在一处，
/// 避免同一个状态在三个页签里被写成三种不同的说法。
/// </remarks>
internal static class DisplayText
{
    private static readonly string[] SizeUnits = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>
    /// 把资源格式转换为界面展示文本。
    /// </summary>
    /// <param name="format">资源格式。</param>
    /// <returns>展示文本，如 <c>M3U8</c>、<c>MP4</c>。</returns>
    public static string Format(VideoFormat format) => format switch
    {
        VideoFormat.M3u8 => "M3U8",
        VideoFormat.Ts => "TS 分片",
        VideoFormat.M4s => "M4S 分片",
        VideoFormat.Mp4 => "MP4",
        VideoFormat.Mpd => "DASH",
        VideoFormat.Ismc => "SmoothStream",
        _ => "未知"
    };

    /// <summary>
    /// 把任务状态转换为界面展示文本。
    /// </summary>
    /// <param name="status">任务状态。</param>
    /// <returns>展示文本。</returns>
    public static string Status(DownloadStatus status) => status switch
    {
        DownloadStatus.Pending => "等待中",
        DownloadStatus.Running => "下载中",
        DownloadStatus.Paused => "已暂停",
        DownloadStatus.Completed => "已完成",
        DownloadStatus.Failed => "失败",
        DownloadStatus.Canceled => "已取消",
        _ => "未知"
    };

    /// <summary>
    /// 把字节数格式化为便于阅读的文本。
    /// </summary>
    /// <param name="bytes">字节数。</param>
    /// <returns>形如 <c>12.5 MB</c> 的文本；非正数返回 <c>-</c>。</returns>
    public static string Size(long bytes)
    {
        if (bytes <= 0)
        {
            return "-";
        }

        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < SizeUnits.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        // 字节量级直接显示整数，避免出现 "512.00 B" 这种别扭写法
        return unit == 0 ? $"{bytes} B" : $"{value:0.##} {SizeUnits[unit]}";
    }

    /// <summary>
    /// 把下载速度格式化为便于阅读的文本。
    /// </summary>
    /// <param name="bytesPerSecond">每秒字节数。</param>
    /// <returns>形如 <c>3.4 MB/s</c> 的文本；未知时返回 <c>-</c>。</returns>
    public static string Speed(double bytesPerSecond)
        => bytesPerSecond <= 0 ? "-" : $"{Size((long)bytesPerSecond)}/s";

    /// <summary>
    /// 把进度百分比格式化为文本。
    /// </summary>
    /// <param name="percent">百分比（0~100）。</param>
    /// <param name="hasTotal">是否已知总量。</param>
    /// <returns>已知总量时为百分比，否则为 <c>未知</c>。</returns>
    public static string Percent(double percent, bool hasTotal)
        => hasTotal ? $"{Math.Clamp(percent, 0d, 100d):0.0}%" : "计算中…";

    /// <summary>
    /// 把时间格式化为短文本。
    /// </summary>
    /// <param name="time">时间；为空返回 <c>-</c>。</param>
    /// <returns>形如 <c>09-13 01:20</c> 的文本。</returns>
    public static string Time(DateTimeOffset? time)
        => time.HasValue ? time.Value.ToString("MM-dd HH:mm") : "-";

    /// <summary>
    /// 把视频时长（秒）格式化为可读文本。
    /// </summary>
    /// <param name="seconds">时长（秒）；为 null 或非正数返回 <c>-</c>。</param>
    /// <returns>
    /// 不足 1 小时返回 <c>mm:ss</c>（如 <c>120:00</c>），
    /// 满 1 小时返回 <c>h:mm:ss</c>（如 <c>2:00:00</c>）。
    /// </returns>
    /// <remarks>
    /// 嗅探列表的时长可能未知（主清单未探测到变体），用 <c>-</c> 而非 <c>0</c> 表示，
    /// 避免把「未知」误读成「时长为零」。
    /// </remarks>
    public static string Duration(double? seconds)
    {
        if (seconds is not > 0)
        {
            return "-";
        }

        var total = TimeSpan.FromSeconds(seconds.Value);
        return total.TotalHours >= 1
            ? $"{(int)total.TotalHours}:{total.Minutes:00}:{total.Seconds:00}"
            : $"{(int)total.TotalMinutes:00}:{total.Seconds:00}";
    }
}
