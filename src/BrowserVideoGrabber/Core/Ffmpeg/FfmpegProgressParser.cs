/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Ffmpeg
*文件名： FfmpegProgressParser
*版本号： V1.0.0.0
*唯一标识：4c9a8dfa-5fd5-431a-9c9d-382edc12a41c
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 23:00:00
*描述：ffmpeg 进度解析器，从 stderr 逐行提取总时长、已处理时长、速度与输出体积。
*
*=================================================
*修改标记
*修改时间：2026/9/12 23:00:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Globalization;
using System.Text.RegularExpressions;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Core.Ffmpeg;

/// <summary>
/// ffmpeg stderr 进度解析器。
/// </summary>
/// <remarks>
/// <para>
/// 需要同时兼容两种输出风格，原因是 ffmpeg 各发行版与各版本的行为并不一致：
/// <list type="bullet">
///   <item><description>
///     <b>信息行</b>：输入探测阶段打印的 <c>Duration: 00:20:05.00</c>，
///     以及默认状态行中的 <c>time=00:12:41.00 ... speed=2.1x</c>。
///   </description></item>
///   <item><description>
///     <b>进度管道行</b>：<c>-progress</c> 输出的键值对，如
///     <c>out_time=00:00:50.000000</c>、<c>out_time_us=50000000</c>、<c>total_size=1048576</c>。
///   </description></item>
/// </list>
/// </para>
/// <para>
/// 所有正则都采用「在整行中搜索」而非「整行匹配」的策略，
/// 这样即使 ffmpeg 在一条日志里混排了多种信息，也不会漏掉关键字段。
/// </para>
/// </remarks>
public sealed class FfmpegProgressParser
{
    /// <summary>匹配输入探测行中的媒体总时长。</summary>
    private static readonly Regex DurationRegex = new(
        @"Duration:\s*(\d+):(\d{2}):(\d{2}(?:\.\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>匹配默认状态行中的已处理时长。</summary>
    private static readonly Regex TimeRegex = new(
        @"\btime=(\d+):(\d{2}):(\d{2}(?:\.\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>匹配进度管道输出的已处理时长（时间码形式）。</summary>
    private static readonly Regex OutTimeRegex = new(
        @"\bout_time=(\d+):(\d{2}):(\d{2}(?:\.\d+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// 匹配进度管道输出的微秒字段。
    /// </summary>
    /// <remarks>
    /// 注意：ffmpeg 的 <c>out_time_ms</c> 字段实际写入的是<b>微秒</b>而非毫秒
    /// （官方长期存在的命名与语义不一致问题），因此这里对 <c>_ms</c> 与 <c>_us</c> 做同样处理。
    /// </remarks>
    private static readonly Regex OutTimeMicrosecondsRegex = new(
        @"\bout_time_(?:us|ms)=(\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>匹配处理速度倍率。</summary>
    private static readonly Regex SpeedRegex = new(
        @"speed=\s*([\d.]+)x",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>匹配已写出的总字节数。</summary>
    private static readonly Regex TotalSizeRegex = new(
        @"\btotal_size=(\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>媒体总时长。未从输出中探测到时为 null。</summary>
    public TimeSpan? Duration { get; private set; }

    /// <summary>最近一次成功解析出的进度快照。</summary>
    public DownloadProgress? LastProgress { get; private set; }

    /// <summary>
    /// 喂入一行 ffmpeg stderr 输出，并尝试解析出新的进度。
    /// </summary>
    /// <param name="line">stderr 的一行。允许为 null 或空白。</param>
    /// <returns>
    /// 解析出的进度快照；该行不含任何可识别的进度字段时返回 null。
    /// </returns>
    /// <remarks>
    /// 本方法绝不抛异常：ffmpeg 的日志格式随版本变化，
    /// 任何无法识别的行只应被安静忽略，绝不能因此中断下载或让程序崩溃。
    /// </remarks>
    public DownloadProgress? Feed(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        // 1) 总时长（出现在输入探测阶段，先于所有进度行）
        var durationMatch = DurationRegex.Match(line);
        if (durationMatch.Success)
        {
            Duration = ToTimeSpan(durationMatch, 1);
        }

        // 2) 已处理时长：优先取 out_time（精度更高），依次回退到 time= 与微秒字段
        var processed = ExtractProcessed(line);

        // 3) 处理速度
        double? speed = null;
        var speedMatch = SpeedRegex.Match(line);
        if (speedMatch.Success
            && double.TryParse(speedMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedSpeed))
        {
            speed = parsedSpeed;
        }

        // 4) 已写出字节数
        long? totalSize = null;
        var sizeMatch = TotalSizeRegex.Match(line);
        if (sizeMatch.Success
            && long.TryParse(sizeMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSize))
        {
            totalSize = parsedSize;
        }

        // 三类字段全都没有，说明这是一行普通日志（编码信息、流信息等），不是进度行
        if (processed is null && speed is null && totalSize is null)
        {
            return null;
        }

        var hasTotal = Duration.HasValue && Duration.Value.TotalSeconds > 0d;
        var percent = 0d;

        if (hasTotal && processed.HasValue)
        {
            percent = processed.Value.TotalSeconds / Duration!.Value.TotalSeconds * 100d;
            // 进度管道在结尾偶尔会给出略大于总时长的值，裁剪到 [0,100] 避免进度条越界
            percent = Math.Clamp(percent, 0d, 100d);
        }

        var progress = new DownloadProgress
        {
            Percent = percent,
            HasTotal = hasTotal,
            Processed = processed,
            Total = hasTotal ? Duration : null,
            SpeedMultiplier = speed ?? 0d,
            DownloadedBytes = totalSize ?? 0L
        };

        LastProgress = progress;
        return progress;
    }

    /// <summary>
    /// 重置解析状态，供重试时清空上一轮的时长与进度。
    /// </summary>
    /// <remarks>
    /// 必须重置 <see cref="Duration"/>，否则重试时若 ffmpeg 尚未打印新的 Duration，
    /// 旧的时长会被沿用，可能出现进度条直接从中间开始的观感问题。
    /// </remarks>
    public void Reset()
    {
        Duration = null;
        LastProgress = null;
    }

    /// <summary>
    /// 从一行输出中提取已处理时长。
    /// </summary>
    /// <param name="line">stderr 的一行。</param>
    /// <returns>已处理时长；无法提取时返回 null。</returns>
    private static TimeSpan? ExtractProcessed(string line)
    {
        var outTimeMatch = OutTimeRegex.Match(line);
        if (outTimeMatch.Success)
        {
            return ToTimeSpan(outTimeMatch, 1);
        }

        var timeMatch = TimeRegex.Match(line);
        if (timeMatch.Success)
        {
            return ToTimeSpan(timeMatch, 1);
        }

        var microsMatch = OutTimeMicrosecondsRegex.Match(line);
        if (microsMatch.Success
            && long.TryParse(microsMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds))
        {
            return TimeSpan.FromMilliseconds(microseconds / 1000d);
        }

        return null;
    }

    /// <summary>
    /// 把正则匹配到的 <c>时:分:秒</c> 三组转换为 <see cref="TimeSpan"/>。
    /// </summary>
    /// <param name="match">已匹配成功的正则结果。</param>
    /// <param name="firstGroupIndex">小时所在分组的索引，分钟与秒为其后两组。</param>
    /// <returns>对应的时间跨度。</returns>
    private static TimeSpan ToTimeSpan(Match match, int firstGroupIndex)
    {
        var hours = int.Parse(match.Groups[firstGroupIndex].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups[firstGroupIndex + 1].Value, CultureInfo.InvariantCulture);
        var seconds = double.Parse(match.Groups[firstGroupIndex + 2].Value, NumberStyles.Float, CultureInfo.InvariantCulture);

        return TimeSpan.FromSeconds((hours * 3600d) + (minutes * 60d) + seconds);
    }
}
