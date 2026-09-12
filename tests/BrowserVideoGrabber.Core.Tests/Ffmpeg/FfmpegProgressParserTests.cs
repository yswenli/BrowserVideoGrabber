/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Ffmpeg
*文件名： FfmpegProgressParserTests
*版本号： V1.0.0.0
*唯一标识：f5947bc3-e3ce-4063-990c-c3cb38fda48d
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:40:00
*描述：FfmpegProgressParser 的单元测试，覆盖时长提取、百分比计算、脏行容错与降级策略。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:40:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Ffmpeg;

namespace BrowserVideoGrabber.Tests.Ffmpeg;

/// <summary>
/// <see cref="FfmpegProgressParser"/> 的行为验证。
/// </summary>
public sealed class FfmpegProgressParserTests
{
    /// <summary>
    /// 应能从 ffmpeg 的输入探测信息中提取媒体总时长。
    /// </summary>
    [Fact]
    public void Feed_ShouldExtractDuration()
    {
        var parser = new FfmpegProgressParser();

        parser.Feed("  Duration: 00:20:05.00, start: 0.000000, bitrate: 1234 kb/s");

        Assert.Equal(TimeSpan.FromSeconds(1205), parser.Duration);
    }

    /// <summary>
    /// 已知总时长时应按 time= 计算出正确的百分比。
    /// </summary>
    [Fact]
    public void Feed_ShouldComputePercentFromTimeAndDuration()
    {
        var parser = new FfmpegProgressParser();
        parser.Feed("  Duration: 00:20:05.00, start: 0.000000, bitrate: 1234 kb/s");

        var progress = parser.Feed("frame=  120 fps= 30 q=-1.0 size=  102400kB time=00:12:41.00 bitrate=2097.2kbits/s speed=2.1x");

        Assert.NotNull(progress);
        Assert.True(progress.HasTotal);

        // 761 / 1205 ≈ 63.15%
        Assert.InRange(progress.Percent, 62.5, 63.8);
        Assert.Equal(TimeSpan.FromSeconds(761), progress.Processed);
        Assert.Equal(TimeSpan.FromSeconds(1205), progress.Total);
    }

    /// <summary>
    /// 应支持 -progress 输出的 out_time 键值格式。
    /// </summary>
    [Fact]
    public void Feed_ShouldSupportProgressPipeFormat()
    {
        var parser = new FfmpegProgressParser();
        parser.Feed("Duration: 00:01:40.00, start: 0.000000, bitrate: 1000 kb/s");

        var progress = parser.Feed("out_time=00:00:50.000000");

        Assert.NotNull(progress);
        Assert.InRange(progress.Percent, 49.0, 51.0);
    }

    /// <summary>
    /// 部分 ffmpeg 版本只输出 out_time_us（微秒），必须兼容。
    /// </summary>
    [Fact]
    public void Feed_ShouldSupportOutTimeMicroseconds()
    {
        var parser = new FfmpegProgressParser();
        parser.Feed("Duration: 00:01:40.00, start: 0.000000, bitrate: 1000 kb/s");

        var progress = parser.Feed("out_time_us=50000000");

        Assert.NotNull(progress);
        Assert.InRange(progress.Percent, 49.0, 51.0);
    }

    /// <summary>
    /// 应解析出 ffmpeg 的处理速度倍率，用于界面展示「2.1x」这类信息。
    /// </summary>
    [Fact]
    public void Feed_ShouldParseSpeedMultiplier()
    {
        var parser = new FfmpegProgressParser();

        var progress = parser.Feed("frame=  120 fps= 30 q=-1.0 size=  1024kB time=00:00:12.34 bitrate=2097.2kbits/s speed=2.1x");

        Assert.NotNull(progress);
        Assert.Equal(2.1, progress.SpeedMultiplier, precision: 3);
    }

    /// <summary>
    /// 逐行喂入时进度必须单调不减，否则界面会出现进度条倒退的观感问题。
    /// </summary>
    [Fact]
    public void Feed_ShouldKeepProgressMonotonic()
    {
        var parser = new FfmpegProgressParser();
        parser.Feed("Duration: 00:02:00.00, start: 0.000000, bitrate: 1000 kb/s");

        var percents = new List<double>();
        foreach (var line in new[]
                 {
                     "out_time=00:00:10.000000",
                     "out_time=00:00:30.000000",
                     "out_time=00:01:00.000000",
                     "out_time=00:01:45.000000"
                 })
        {
            var progress = parser.Feed(line);
            if (progress is not null)
            {
                percents.Add(progress.Percent);
            }
        }

        Assert.Equal(4, percents.Count);
        for (var i = 1; i < percents.Count; i++)
        {
            Assert.True(percents[i] >= percents[i - 1], $"进度出现回退：{percents[i - 1]} -> {percents[i]}");
        }
    }

    /// <summary>
    /// 无关行与畸形行必须被安静忽略，绝不能因为 ffmpeg 版本差异导致程序崩溃。
    /// </summary>
    /// <param name="line">待喂入的行。</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Stream #0:0: Video: h264 (High), yuv420p, 1920x1080")]
    [InlineData("time=not-a-timecode")]
    [InlineData("out_time_us=abc")]
    [InlineData("frame=  0 fps=0.0 q=0.0 size=       0kB time=N/A bitrate=N/A speed=N/A")]
    public void Feed_ShouldIgnoreUnparsableLines(string line)
    {
        var parser = new FfmpegProgressParser();

        var progress = parser.Feed(line);

        Assert.Null(progress);
    }

    /// <summary>
    /// 未知总时长时必须降级：百分比为 0、HasTotal 为 false，但仍然上报已处理时长，
    /// 这样界面至少可以显示「已处理 00:01:20」而不是完全空白。
    /// </summary>
    [Fact]
    public void Feed_ShouldDegradeGracefully_WhenDurationUnknown()
    {
        var parser = new FfmpegProgressParser();

        var progress = parser.Feed("out_time=00:01:20.000000");

        Assert.NotNull(progress);
        Assert.False(progress.HasTotal);
        Assert.Equal(0d, progress.Percent);
        Assert.Equal(TimeSpan.FromSeconds(80), progress.Processed);
    }

    /// <summary>
    /// 总时长为零时不得触发除零，百分比应保持为 0。
    /// </summary>
    [Fact]
    public void Feed_ShouldNotDivideByZero_WhenDurationIsZero()
    {
        var parser = new FfmpegProgressParser();
        parser.Feed("Duration: 00:00:00.00, start: 0.000000, bitrate: 0 kb/s");

        var progress = parser.Feed("out_time=00:00:01.000000");

        Assert.NotNull(progress);
        Assert.Equal(0d, progress.Percent);
    }

    /// <summary>
    /// 应能解析出已落盘的总字节数，用于界面展示下载体积。
    /// </summary>
    [Fact]
    public void Feed_ShouldParseTotalSize()
    {
        var parser = new FfmpegProgressParser();

        var progress = parser.Feed("out_time=00:00:01.000000\ntotal_size=1048576");

        Assert.NotNull(progress);
        Assert.Equal(1048576, progress.DownloadedBytes);
    }

    /// <summary>
    /// 解析器提供重置能力，供重试时清空上一轮的时长与进度。
    /// </summary>
    [Fact]
    public void Reset_ShouldClearDurationAndProgress()
    {
        var parser = new FfmpegProgressParser();
        parser.Feed("Duration: 00:20:05.00, start: 0.000000, bitrate: 1234 kb/s");
        parser.Feed("out_time=00:12:41.000000");

        parser.Reset();

        Assert.Null(parser.Duration);
        Assert.Null(parser.LastProgress);
    }
}
