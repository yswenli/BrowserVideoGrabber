/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Formatting
*文件名： DisplayTextTests
*版本号： V1.0.0.0
*唯一标识：3f0f1b2c-6d4e-4a8b-9c1d-2e3f4a5b6c7d
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/11/15 01:10:00
*描述：界面格式化文本测试，重点保证列文本在任意系统区域下都能被解析器回读。
*
*=================================================
*修改标记
*修改时间：2026/11/15 01:10:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Globalization;

using BrowserVideoGrabber.App.Formatting;
using BrowserVideoGrabber.Core.Lists;

namespace BrowserVideoGrabber.Tests.Formatting;

/// <summary>
/// <see cref="DisplayText"/> 的单元测试：格式必须稳定，且能被 <see cref="ColumnValueParser"/> 回读。
/// </summary>
public sealed class DisplayTextTests
{
    [Fact]
    public void Size_UsesInvariantCulture_UnderGermanCulture()
    {
        WithCulture("de-DE", () =>
        {
            var text = DisplayText.Size(1258291);

            Assert.Equal("1.2 MB", text);
            Assert.Equal(1258291.2d, Assert.IsType<double>(ColumnValueParser.ParseSize(text)), 3);
        });
    }

    [Fact]
    public void Percent_UsesInvariantCulture_UnderGermanCulture()
    {
        WithCulture("de-DE", () =>
        {
            var text = DisplayText.Percent(45.5, hasTotal: true);

            Assert.Equal("45.5%", text);
            Assert.Equal(45.5d, Assert.IsType<double>(ColumnValueParser.ParseProgress(text)), 3);
        });
    }

    [Fact]
    public void Time_UsesInvariantCulture_UnderGermanCulture()
    {
        WithCulture("de-DE", () =>
        {
            var text = DisplayText.Time(new DateTimeOffset(2026, 9, 13, 1, 20, 0, TimeSpan.Zero));

            Assert.Equal("09-13 01:20", text);
            Assert.Equal(
                new DateTime(2026, 9, 13, 1, 20, 0),
                Assert.IsType<DateTime>(
                    ColumnValueParser.ParseMonthDay(text, new DateTime(2026, 11, 15, 12, 0, 0))));
        });
    }

    [Fact]
    public void Speed_UsesInvariantCulture_UnderGermanCulture()
    {
        WithCulture("de-DE", () =>
        {
            var text = DisplayText.Speed(3565158.4d);

            Assert.Equal("3.4 MB/s", text);
            Assert.Equal(3565158.4d, Assert.IsType<double>(ColumnValueParser.ParseSpeed(text)), 3);
        });
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(0.5d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Speed_ReturnsDash_WhenNotAValidByteRate(double bytesPerSecond)
    {
        Assert.Equal("-", DisplayText.Speed(bytesPerSecond));
    }

    [Fact]
    public void Duration_UsesStableFormat()
    {
        Assert.Equal("01:20", DisplayText.Duration(80));
        Assert.Equal("1:02:03", DisplayText.Duration(3723));
    }

    [Fact]
    public void Duration_ReturnsDash_WhenOverflowOrInvalid()
    {
        Assert.Equal("-", DisplayText.Duration(1e20));
        Assert.Equal("-", DisplayText.Duration(double.NaN));
        Assert.Equal("-", DisplayText.Duration(double.PositiveInfinity));
    }

    private static void WithCulture(string cultureName, Action action)
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}