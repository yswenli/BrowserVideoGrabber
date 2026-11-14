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
*描述：ColumnValueParser 的单元测试，覆盖各列真实格式、无效值与单位换算。
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
/// 列文本解析器测试：覆盖各列真实格式、无效值与单位换算。
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
    public void ParseQuantity_ReturnsNull_WhenNoNumber(string? text)
    {
        Assert.Null(ColumnValueParser.ParseQuantity(text));
    }

    [Theory]
    [InlineData("12:34", 754d)]
    [InlineData("02:00:00", 7200d)]
    [InlineData("00:30", 30d)]
    [InlineData("1:2:3", 3723d)]
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
    public void ParseDuration_ReturnsNull_WhenInvalid(string text)
    {
        Assert.Null(ColumnValueParser.ParseDuration(text));
    }

    [Fact]
    public void ParseClock_ParsesHourMinuteSecond()
    {
        var actual = Assert.IsType<TimeSpan>(ColumnValueParser.ParseClock("09:13:45"));

        Assert.Equal(new TimeSpan(9, 13, 45), actual);
    }

    [Fact]
    public void ParseMonthDay_ParsesWithoutYear()
    {
        var actual = Assert.IsType<DateTime>(ColumnValueParser.ParseMonthDay("09-13 01:20"));

        Assert.Equal(9, actual.Month);
        Assert.Equal(13, actual.Day);
        Assert.Equal(1, actual.Hour);
        Assert.Equal(20, actual.Minute);
    }

    [Theory]
    [InlineData("-")]
    [InlineData("2026-09-13")]
    [InlineData("")]
    public void ParseMonthDay_ReturnsNull_WhenInvalid(string text)
    {
        Assert.Null(ColumnValueParser.ParseMonthDay(text));
    }

    [Fact]
    public void ParseDownloaded_UsesBytesBeforeSlash()
    {
        var actual = Assert.IsType<double>(ColumnValueParser.ParseDownloaded("12.5 MB / 100 MB"));

        Assert.Equal(13107200d, actual, 3);
    }

    [Fact]
    public void ParseDownloaded_ConvertsDurationToSeconds()
    {
        var actual = Assert.IsType<double>(ColumnValueParser.ParseDownloaded("01:20 / 02:00"));

        Assert.Equal(80d, actual, 3);
    }

    [Theory]
    [InlineData("- / 未知")]
    [InlineData("-")]
    [InlineData("")]
    public void ParseDownloaded_ReturnsNull_WhenInvalid(string text)
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
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ParseText_ReturnsNull_WhenEmptyOrDash(string? text)
    {
        Assert.Null(ColumnValueParser.ParseText(text));
    }
}
