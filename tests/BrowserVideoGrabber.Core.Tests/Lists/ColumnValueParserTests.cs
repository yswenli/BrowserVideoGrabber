/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Lists
*文件名： ColumnValueParserTests
*版本号： V1.0.0.0
*唯一标识：40181c95-b0e3-48e9-9175-d753845cd5cd
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/11/15 00:15:17
*描述：ColumnValueParser 的单元测试，覆盖各列真实格式、无效值、单位换算与跨年推断。
*
*=================================================
*修改标记
*修改时间：2026/11/15 00:15:17
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Lists;

namespace BrowserVideoGrabber.Tests.Lists;

/// <summary>
/// 列文本解析器测试：覆盖各列真实格式、无效值、单位换算与跨年推断。
/// </summary>
public sealed class ColumnValueParserTests
{
    [Theory]
    [InlineData("12.5 MB", 13107200d)]
    [InlineData("1.2 GB", 1288490188.8d)]
    [InlineData("≈ 1.2 GB", 1288490188.8d)]
    [InlineData("3.4 MB/s", 3565158.4d)]
    [InlineData("1.2x", 1.2d)]
    [InlineData("45.0%", 45d)]
    [InlineData("12 / 30 片", 12d)]
    [InlineData("512 B", 512d)]
    [InlineData("0 B", 0d)]
    [InlineData("1 KB", 1024d)]
    [InlineData("1 TB", 1099511627776d)]
    [InlineData("100 %", 100d)]
    public void ParseQuantity_ReturnsNumericValue(string text, double expected)
    {
        var actual = Assert.IsType<double>(ColumnValueParser.ParseQuantity(text));

        Assert.Equal(expected, actual, 3);
    }

    [Theory]
    [InlineData("-")]
    [InlineData("计算中…")]
    [InlineData("等待中")]
    [InlineData("部分完成")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("12.5 MiB")]
    [InlineData("1 PB")]
    [InlineData("1.2 Mbps")]
    [InlineData("-12.5 MB")]
    public void ParseQuantity_ReturnsNull_WhenNoNumberOrUnknownUnit(string? text)
    {
        Assert.Null(ColumnValueParser.ParseQuantity(text));
    }

    [Theory]
    [InlineData("12.5 MB", 13107200d)]
    [InlineData("≈ 1.2 GB", 1288490188.8d)]
    [InlineData("512 B", 512d)]
    [InlineData("1 TB", 1099511627776d)]
    public void ParseSize_ReturnsBytes(string text, double expected)
    {
        var actual = Assert.IsType<double>(ColumnValueParser.ParseSize(text));

        Assert.Equal(expected, actual, 3);
    }

    [Theory]
    [InlineData("12 / 30 片")]
    [InlineData("3.4 MB/s")]
    [InlineData("1.2x")]
    [InlineData("45.0%")]
    [InlineData("-")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseSize_ReturnsNull_WhenNotAPlainSize(string? text)
    {
        Assert.Null(ColumnValueParser.ParseSize(text));
    }

    [Theory]
    [InlineData("3.4 MB/s", 3565158.4d)]
    [InlineData("512 B/s", 512d)]
    [InlineData("1.5 GB/s", 1610612736d)]
    public void ParseSpeed_ReturnsBytesPerSecond(string text, double expected)
    {
        var actual = Assert.IsType<double>(ColumnValueParser.ParseSpeed(text));

        Assert.Equal(expected, actual, 3);
    }

    [Theory]
    [InlineData("1.2x")]
    [InlineData("3.4 MB")]
    [InlineData("-")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseSpeed_ReturnsNull_WhenNotByteRate(string? text)
    {
        Assert.Null(ColumnValueParser.ParseSpeed(text));
    }

    [Theory]
    [InlineData("45.0%", 45d)]
    [InlineData("100.0%", 100d)]
    [InlineData("12 / 30 片", 40d)]
    [InlineData("30 / 30 片", 100d)]
    [InlineData("0 / 30 片", 0d)]
    public void ParseProgress_NormalizesToPercent(string text, double expected)
    {
        var actual = Assert.IsType<double>(ColumnValueParser.ParseProgress(text));

        Assert.Equal(expected, actual, 3);
    }

    [Theory]
    [InlineData("计算中…")]
    [InlineData("等待中")]
    [InlineData("-")]
    [InlineData("12 / 0 片")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseProgress_ReturnsNull_WhenUnknown(string? text)
    {
        Assert.Null(ColumnValueParser.ParseProgress(text));
    }

    [Theory]
    [InlineData("12:34", 754d)]
    [InlineData("02:00:00", 7200d)]
    [InlineData("00:30", 30d)]
    [InlineData("1:2:3", 3723d)]
    [InlineData("120:00", 7200d)]
    public void ParseDuration_ReturnsTimeSpan(string text, double expectedSeconds)
    {
        var actual = Assert.IsType<TimeSpan>(ColumnValueParser.ParseDuration(text));

        Assert.Equal(expectedSeconds, actual.TotalSeconds, 3);
    }

    [Theory]
    [InlineData("-")]
    [InlineData("abc")]
    [InlineData("12")]
    [InlineData("12:60")]
    [InlineData("1:2:3:4")]
    [InlineData("-1:20")]
    [InlineData("1:-2")]
    [InlineData("2147483647:00:00")]
    [InlineData(null)]
    public void ParseDuration_ReturnsNull_WhenInvalidOrOverflow(string? text)
    {
        Assert.Null(ColumnValueParser.ParseDuration(text));
    }

    [Fact]
    public void ParseClock_ParsesHourMinuteSecond()
    {
        var actual = Assert.IsType<TimeSpan>(ColumnValueParser.ParseClock("09:13:45"));

        Assert.Equal(new TimeSpan(9, 13, 45), actual);
    }

    [Theory]
    [InlineData("12:34")]
    [InlineData("24:00:00")]
    [InlineData("09:60:00")]
    [InlineData("09:13:60")]
    [InlineData("-1:20:00")]
    [InlineData("-")]
    [InlineData(null)]
    public void ParseClock_ReturnsNull_WhenNotStrictTimeOfDay(string? text)
    {
        Assert.Null(ColumnValueParser.ParseClock(text));
    }

    [Fact]
    public void ParseMonthDay_ParsesWithoutYear()
    {
        var actual = Assert.IsType<DateTime>(
            ColumnValueParser.ParseMonthDay("09-13 01:20", new DateTime(2026, 11, 15, 12, 0, 0)));

        Assert.Equal(new DateTime(2026, 9, 13, 1, 20, 0), actual);
    }

    [Fact]
    public void ParseMonthDay_RollsBackToPreviousYear_WhenDateIsInFuture()
    {
        var actual = Assert.IsType<DateTime>(
            ColumnValueParser.ParseMonthDay("12-31 23:59", new DateTime(2026, 1, 1, 0, 30, 0)));

        Assert.Equal(new DateTime(2025, 12, 31, 23, 59, 0), actual);
    }

    [Fact]
    public void ParseMonthDay_UsesLeapYear_WhenDayIsFebruary29()
    {
        var actual = Assert.IsType<DateTime>(
            ColumnValueParser.ParseMonthDay("02-29 12:00", new DateTime(2026, 11, 15, 12, 0, 0)));

        Assert.Equal(new DateTime(2024, 2, 29, 12, 0, 0), actual);
    }

    [Theory]
    [InlineData("-")]
    [InlineData("2026-09-13")]
    [InlineData("13-01 00:00")]
    [InlineData("09-13 24:00")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseMonthDay_ReturnsNull_WhenInvalid(string? text)
    {
        Assert.Null(ColumnValueParser.ParseMonthDay(text, new DateTime(2026, 11, 15, 12, 0, 0)));
    }

    [Theory]
    [InlineData("12.5 MB / 100 MB", 0.125d)]
    [InlineData("01:20 / 02:00", 0.6666667d)]
    [InlineData("02:00:00 / 03:00:00", 0.6666667d)]
    [InlineData("100 MB / 100 MB", 1d)]
    public void ParseDownloaded_ReturnsCompletionRatio(string text, double expected)
    {
        var actual = Assert.IsType<double>(ColumnValueParser.ParseDownloaded(text));

        Assert.Equal(expected, actual, 6);
    }

    [Theory]
    [InlineData("- / 未知")]
    [InlineData("12.5 MB / 未知")]
    [InlineData("01:20 / 未知")]
    [InlineData("-")]
    [InlineData("12.5 MB")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseDownloaded_ReturnsNull_WhenTotalUnknownOrInvalid(string? text)
    {
        Assert.Null(ColumnValueParser.ParseDownloaded(text));
    }

    [Fact]
    public void ParseText_ReturnsOriginalText()
    {
        Assert.Equal("淘宝网", Assert.IsType<string>(ColumnValueParser.ParseText("淘宝网")));
    }

    [Theory]
    [InlineData("-")]
    [InlineData(" - ")]
    [InlineData("—")]
    [InlineData("–")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ParseText_ReturnsNull_WhenEmptyOrDash(string? text)
    {
        Assert.Null(ColumnValueParser.ParseText(text));
    }

    [Theory]
    [InlineData("计算中…")]
    [InlineData("等待中")]
    [InlineData("部分完成")]
    public void ParseText_ReturnsStatusWords(string text)
    {
        Assert.Equal(text, Assert.IsType<string>(ColumnValueParser.ParseText(text)));
    }
}
