/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Formatting
*文件名： DownloadPaneFormattingTests
*版本号： V1.0.0.0
*唯一标识：b7c1d2e3-4f50-4a61-8b72-9c83da94eb05
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/11/15 02:05:00
*描述：下载列表「速度」「已下载」列文本测试，保证这些文本能被排序解析器回读。
*
*=================================================
*修改标记
*修改时间：2026/11/15 02:05:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.App.Panes;
using BrowserVideoGrabber.Core.Lists;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Tests.Formatting;

/// <summary>
/// <see cref="DownloadPane"/> 列文本格式化测试。
/// </summary>
/// <remarks>
/// 「速度」「已下载」两列的文本会被列排序解析器回读，因此格式必须稳定：
/// 0 字节要显示成 <c>0 B</c> 而不是表示「未知」的 <c>-</c>，
/// 否则刚开始的任务会被当成未知排到末尾。
/// </remarks>
public sealed class DownloadPaneFormattingTests
{
    private const long Megabyte = 1024L * 1024L;

    [Fact]
    public void FormatDownloaded_ReturnsZeroBytes_WhenNothingDownloadedYet()
    {
        var task = CreateTask();
        var progress = new DownloadProgress
        {
            DownloadedBytes = 0,
            TotalBytes = 100 * Megabyte,
            HasTotal = true
        };

        var text = DownloadPane.FormatDownloaded(progress, task);

        Assert.Equal("0 B / 100 MB", text);
        Assert.Equal(0d, Assert.IsType<double>(ColumnValueParser.ParseDownloaded(text)));
    }

    [Fact]
    public void FormatDownloaded_ReturnsByteRatio()
    {
        var task = CreateTask();
        var progress = new DownloadProgress
        {
            DownloadedBytes = 13107200,
            TotalBytes = 100 * Megabyte,
            HasTotal = true
        };

        var text = DownloadPane.FormatDownloaded(progress, task);

        Assert.Equal("12.5 MB / 100 MB", text);
        Assert.Equal(0.125d, Assert.IsType<double>(ColumnValueParser.ParseDownloaded(text)), 6);
    }

    [Fact]
    public void FormatDownloaded_ReturnsZeroDuration_WhenProcessingJustStarted()
    {
        var task = CreateTask();
        var progress = new DownloadProgress
        {
            Processed = TimeSpan.Zero,
            Total = TimeSpan.FromMinutes(2)
        };

        var text = DownloadPane.FormatDownloaded(progress, task);

        Assert.Equal("00:00 / 02:00", text);
        Assert.Equal(0d, Assert.IsType<double>(ColumnValueParser.ParseDownloaded(text)));
    }

    [Fact]
    public void FormatDownloaded_ReturnsDurationRatio()
    {
        var task = CreateTask();
        var progress = new DownloadProgress
        {
            Processed = TimeSpan.FromSeconds(80),
            Total = TimeSpan.FromMinutes(2)
        };

        var text = DownloadPane.FormatDownloaded(progress, task);

        Assert.Equal("01:20 / 02:00", text);
        Assert.Equal(2d / 3d, Assert.IsType<double>(ColumnValueParser.ParseDownloaded(text)), 6);
    }

    [Fact]
    public void FormatDownloaded_ReturnsUnknownTotal_WhenTotalMissing()
    {
        var task = CreateTask();
        var progress = new DownloadProgress
        {
            DownloadedBytes = 13107200,
            HasTotal = false
        };

        var text = DownloadPane.FormatDownloaded(progress, task);

        Assert.Equal("12.5 MB / 未知", text);
        Assert.Null(ColumnValueParser.ParseDownloaded(text));
    }

    [Fact]
    public void FormatDownloaded_FallsBackToTaskSnapshot_WhenProgressMissing()
    {
        var task = CreateTask();
        task.DownloadedBytes = 13107200;

        Assert.Equal("12.5 MB / 未知", DownloadPane.FormatDownloaded(null, task));
    }

    [Fact]
    public void FormatSpeed_ReturnsMultiplier_ForFfmpegProgress()
    {
        var text = DownloadPane.FormatSpeed(new DownloadProgress { SpeedMultiplier = 1.2 });

        Assert.Equal("1.2x", text);

        // 倍率与字节/秒不是一个量纲，按设计不参与「速度」列数值排序
        Assert.Null(ColumnValueParser.ParseSpeed(text));
    }

    [Fact]
    public void FormatSpeed_ReturnsByteRate_ForHttpProgress()
    {
        var text = DownloadPane.FormatSpeed(new DownloadProgress { BytesPerSecond = 3565158.4 });

        Assert.Equal("3.4 MB/s", text);
        Assert.Equal(3565158.4d, Assert.IsType<double>(ColumnValueParser.ParseSpeed(text)), 6);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(0.5d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void FormatSpeed_ReturnsDash_WhenRateUnknownOrSubByte(double? bytesPerSecond)
    {
        var progress = bytesPerSecond is null
            ? null
            : new DownloadProgress { BytesPerSecond = bytesPerSecond.Value };

        Assert.Equal("-", DownloadPane.FormatSpeed(progress));
    }

    private static DownloadTask CreateTask() => new()
    {
        Url = "https://example.test/video.mp4",
        Format = VideoFormat.Mp4
    };
}