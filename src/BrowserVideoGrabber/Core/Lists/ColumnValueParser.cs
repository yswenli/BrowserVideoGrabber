/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Lists
*文件名： ColumnValueParser
*版本号： V1.0.0.0
*唯一标识：5cb5e896-0f00-4515-9103-8c603da5d65b
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/11/15 00:15:17
*描述：列表列文本解析器，把界面可读文本转换为可比较的键（数量、时长、时间、下载进度）。
*
*=================================================
*修改标记
*修改时间：2026/11/15 00:15:17
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Globalization;
using System.Text.RegularExpressions;

namespace BrowserVideoGrabber.Core.Lists;

/// <summary>
/// 列表列文本的解析工具：把界面上的可读文本转换为可比较的键。
/// </summary>
/// <remarks>
/// 返回 <see langword="null"/> 表示「无法解析」，由列表控件统一排到末尾。
/// 所有解析都按 InvariantCulture 处理，避免系统区域设置影响小数点。
/// </remarks>
public static partial class ColumnValueParser
{
    private const double Kilobyte = 1024d;
    private const double Megabyte = Kilobyte * 1024d;
    private const double Gigabyte = Megabyte * 1024d;
    private const double Terabyte = Gigabyte * 1024d;

    /// <summary>普通文本：空串与 <c>-</c> 视为无效值。</summary>
    public static IComparable? ParseText(string? text)
        => string.IsNullOrWhiteSpace(text) || text == "-" ? null : text;

    /// <summary>解析带单位的数量（体积、速度、百分比、片数、倍率）。</summary>
    public static IComparable? ParseQuantity(string? text)
    {
        if (!TryExtractFirstNumber(text, out var value, out var unit))
        {
            return null;
        }

        var multiplier = unit?.ToUpperInvariant() switch
        {
            "KB" => Kilobyte,
            "MB" => Megabyte,
            "GB" => Gigabyte,
            "TB" => Terabyte,
            _ => 1d
        };

        return value * multiplier;
    }

    /// <summary>解析 <c>mm:ss</c> / <c>h:mm:ss</c> 形式的时长。</summary>
    public static IComparable? ParseDuration(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split(':');
        if (parts.Length is < 2 or > 3)
        {
            return null;
        }

        if (!int.TryParse(parts[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || !int.TryParse(parts[^2], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || seconds is < 0 or > 59
            || minutes < 0)
        {
            return null;
        }

        var hours = 0;
        if (parts.Length == 3
            && (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out hours) || hours < 0))
        {
            return null;
        }

        return TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
    }

    /// <summary>解析 <c>HH:mm:ss</c> 形式的时刻（发现时间列）。</summary>
    public static IComparable? ParseClock(string? text) => ParseDuration(text);

    /// <summary>解析 <c>MM-dd HH:mm</c> 形式的完成时间。</summary>
    public static IComparable? ParseMonthDay(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTime.TryParseExact(
            text,
            "MM-dd HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var value)
            ? value
            : null;
    }

    /// <summary>解析「已下载」复合文本，取 <c>/</c> 前半段。</summary>
    public static IComparable? ParseDownloaded(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var slash = text.IndexOf('/');
        var downloaded = (slash >= 0 ? text[..slash] : text).Trim();

        if (downloaded.Length == 0)
        {
            return null;
        }

        // 时长进度（01:20 / 02:00）统一换算成秒，和字节进度一样返回 double，
        // 避免同一列里 TimeSpan 与 double 混排时 CompareTo 抛异常
        if (downloaded.Contains(':'))
        {
            return ParseDuration(downloaded) is TimeSpan duration ? duration.TotalSeconds : null;
        }

        return ParseQuantity(downloaded);
    }

    private static bool TryExtractFirstNumber(string? text, out double value, out string? unit)
    {
        value = 0;
        unit = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = NumberPattern().Match(text);
        if (!match.Success
            || !double.TryParse(
                match.Groups["value"].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value))
        {
            return false;
        }

        unit = match.Groups["unit"].Success ? match.Groups["unit"].Value : null;
        return true;
    }

    [GeneratedRegex(@"(?<value>\d+(?:\.\d+)?)\s*(?<unit>[A-Za-z]+|片|%)?")]
    private static partial Regex NumberPattern();
}
