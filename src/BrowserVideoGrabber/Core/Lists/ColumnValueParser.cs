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
/// <para>
/// 返回 <see langword="null"/> 表示「无法解析」，由列表控件统一排到末尾。
/// </para>
/// <para>
/// 每一列都必须使用<b>量纲一致</b>的解析方法：「大小」用 <see cref="ParseSize"/>（字节）、
/// 「速度」用 <see cref="ParseSpeed"/>（字节/秒）、「进度」用 <see cref="ParseProgress"/>（百分比）、
/// 「已下载」用 <see cref="ParseDownloaded"/>（完成比例）。不要把不同量纲的值混进同一列比较 ——
/// 那会让排序结果失去业务含义。
/// </para>
/// <para>
/// 所有数字解析都按 <see cref="CultureInfo.InvariantCulture"/> 处理，界面文本也必须用不变文化格式化，
/// 否则德语等以逗号作小数点的区域会把 <c>1,2 MB</c> 解析成 <c>1</c>。
/// </para>
/// </remarks>
public static partial class ColumnValueParser
{
    private const double Kilobyte = 1024d;
    private const double Megabyte = Kilobyte * 1024d;
    private const double Gigabyte = Megabyte * 1024d;
    private const double Terabyte = Gigabyte * 1024d;

    // 体积是字节数，业务上不可能超过 long 上限；超出视为解析失败，避免 Infinity 变成排序键
    private const double MaxByteValue = long.MaxValue;

    /// <summary>
    /// 解析普通文本。
    /// </summary>
    /// <param name="text">列文本。</param>
    /// <returns>
    /// 去掉首尾空白后的文本；空白、<c>-</c>、<c>—</c>、<c>–</c> 返回 <see langword="null"/>。
    /// </returns>
    public static IComparable? ParseText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        return trimmed is "-" or "—" or "–" ? null : trimmed;
    }

    /// <summary>
    /// 解析体积文本（字节数）。
    /// </summary>
    /// <param name="text">列文本，如 <c>12.5 MB</c>、<c>≈ 1.2 GB</c>、<c>512 B</c>。</param>
    /// <returns>
    /// 字节数；含 <c>/</c>（如 <c>12 / 30 片</c>）或无法解析时返回 <see langword="null"/>。
    /// </returns>
    public static IComparable? ParseSize(string? text) => ParseSizeValue(text);

    /// <summary>
    /// 解析速度文本（字节/秒）。
    /// </summary>
    /// <param name="text">列文本，如 <c>3.4 MB/s</c>。</param>
    /// <returns>
    /// 字节/秒；<c>1.2x</c> 这类倍率、<c>-</c> 与无法解析时返回 <see langword="null"/>。
    /// </returns>
    /// <remarks>倍率与字节速率没有可比性，因此只接受带 <c>/s</c> 后缀的文本。</remarks>
    public static IComparable? ParseSpeed(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        return trimmed.EndsWith("/s", StringComparison.OrdinalIgnoreCase)
            ? ParseSizeValue(trimmed[..^2])
            : null;
    }

    /// <summary>
    /// 解析进度文本，统一换算成 0~100 的百分比。
    /// </summary>
    /// <param name="text">列文本，如 <c>45.0%</c>、<c>12 / 30 片</c>。</param>
    /// <returns>
    /// 百分比；<c>计算中…</c>、<c>等待中</c> 等状态文字返回 <see langword="null"/>。
    /// </returns>
    /// <remarks>
    /// 分片进度显示的是「已完成片数 / 总片数」，与百分比不是同一量纲，
    /// 必须先换算成百分比，否则 <c>30 / 30 片</c> 会排在 <c>45.0%</c> 后面。
    /// </remarks>
    public static IComparable? ParseProgress(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();

        if (trimmed.EndsWith('%'))
        {
            return ParseQuantity(trimmed) is double percent && double.IsFinite(percent)
                ? Math.Clamp(percent, 0d, 100d)
                : null;
        }

        var match = SegmentProgressPattern().Match(trimmed);
        if (!match.Success
            || !TryParsePart(match.Groups["done"].Value, out var done)
            || !TryParsePart(match.Groups["total"].Value, out var total)
            || total <= 0)
        {
            return null;
        }

        return Math.Clamp(done * 100d / total, 0d, 100d);
    }

    /// <summary>
    /// 解析带单位的数量（体积、速度、百分比、片数、倍率）。
    /// </summary>
    /// <param name="text">列文本。</param>
    /// <returns>按单位换算后的数值；未知单位或无法解析时返回 <see langword="null"/>。</returns>
    /// <remarks>
    /// 这是通用解析器。具体列应优先使用 <see cref="ParseSize"/> / <see cref="ParseSpeed"/> /
    /// <see cref="ParseProgress"/> / <see cref="ParseDownloaded"/>，避免量纲混排。
    /// 复合文本（含 <c>/</c>，如 <c>12 / 30 片</c>、<c>3.4 MB/s</c>）一律返回 <see langword="null"/>，
    /// 防止调用方误把它当成单一数值。
    /// </remarks>
    public static IComparable? ParseQuantity(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)
            || text.Contains('/')
            || !TryParseQuantityText(text, out var value, out var unit)
            || !TryGetMultiplier(unit, out var multiplier))
        {
            return null;
        }

        var result = value * multiplier;
        return double.IsFinite(result) && result <= MaxByteValue ? result : null;
    }

    /// <summary>
    /// 解析 <c>mm:ss</c> / <c>h:mm:ss</c> 形式的时长。
    /// </summary>
    /// <param name="text">列文本。</param>
    /// <returns>
    /// 时长；格式非法或超出 <see cref="TimeSpan.MaxValue"/> 时返回 <see langword="null"/>。
    /// </returns>
    public static IComparable? ParseDuration(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Trim().Split(':');
        if (parts.Length is < 2 or > 3
            || !TryParsePart(parts[^1], out var seconds)
            || !TryParsePart(parts[^2], out var minutes)
            || seconds > 59)
        {
            return null;
        }

        var hours = 0;
        if (parts.Length == 3
            && (!TryParsePart(parts[0], out hours) || minutes > 59))
        {
            return null;
        }

        var totalSeconds = (hours * 3600d) + (minutes * 60d) + seconds;
        return totalSeconds > TimeSpan.MaxValue.TotalSeconds ? null : TimeSpan.FromSeconds(totalSeconds);
    }

    /// <summary>
    /// 解析 <c>HH:mm:ss</c> 形式的时刻（发现时间列）。
    /// </summary>
    /// <param name="text">列文本。</param>
    /// <returns>
    /// 时刻；不是严格的三段式（小时 0~23、分秒 0~59）时返回 <see langword="null"/>。
    /// </returns>
    public static IComparable? ParseClock(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Trim().Split(':');
        if (parts.Length != 3
            || !TryParsePart(parts[0], out var hours)
            || !TryParsePart(parts[1], out var minutes)
            || !TryParsePart(parts[2], out var seconds)
            || hours > 23
            || minutes > 59
            || seconds > 59)
        {
            return null;
        }

        return new TimeSpan(hours, minutes, seconds);
    }

    /// <summary>
    /// 解析 <c>MM-dd HH:mm</c> 形式的完成时间（文本不含年份）。
    /// </summary>
    /// <param name="text">列文本。</param>
    /// <returns>不晚于当前时间的最近一次出现；无法解析时返回 <see langword="null"/>。</returns>
    /// <remarks>任务会跨年持久化，因此不能简单补当前年份：12-31 的任务在次年 1 月应落在上一年。</remarks>
    public static IComparable? ParseMonthDay(string? text) => ParseMonthDay(text, DateTime.Now);

    /// <summary>
    /// 解析 <c>MM-dd HH:mm</c> 形式的完成时间，并以指定时间为参考推断年份。
    /// </summary>
    /// <param name="text">列文本。</param>
    /// <param name="reference">参考时间，用于判断「最近一次出现」。</param>
    /// <returns>
    /// 不晚于 <paramref name="reference"/> 的最近一次出现；无法解析时返回 <see langword="null"/>。
    /// </returns>
    /// <remarks>该重载便于单元测试固定参考时间，生产代码使用无参重载。</remarks>
    public static IComparable? ParseMonthDay(string? text, DateTime reference)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = MonthDayPattern().Match(text.Trim());
        if (!match.Success
            || !TryParsePart(match.Groups["month"].Value, out var month)
            || !TryParsePart(match.Groups["day"].Value, out var day)
            || !TryParsePart(match.Groups["hour"].Value, out var hour)
            || !TryParsePart(match.Groups["minute"].Value, out var minute)
            || month is < 1 or > 12
            || day < 1
            || hour > 23
            || minute > 59)
        {
            return null;
        }

        // 文本没有年份：从参考年份向前找第一个合法且不晚于参考时间的日期，
        // 这样 12-31 在次年 1 月会正确落到上一年，02-29 也能落到最近的闰年。
        // 下界必须夹在 1，否则参考年很小时 DaysInMonth(0, ...) 会抛异常。
        var minYear = Math.Max(1, reference.Year - 8);
        for (var year = reference.Year; year >= minYear; year--)
        {
            if (day > DateTime.DaysInMonth(year, month))
            {
                continue;
            }

            var candidate = new DateTime(year, month, day, hour, minute, 0);
            if (candidate <= reference)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// 解析「已下载」复合文本，换算成完成比例。
    /// </summary>
    /// <param name="text">列文本，如 <c>12.5 MB / 100 MB</c>、<c>01:20 / 02:00</c>、<c>5 / 10 片</c>。</param>
    /// <returns>
    /// 0~1 的完成比例；总量未知（<c>/ 未知</c>）、两侧量纲不一致或无法解析时返回 <see langword="null"/>。
    /// </returns>
    /// <remarks>
    /// 字节进度、时长进度与分片进度量纲不同，不能直接比较；换算成比例后三者才可比。
    /// 总量未知时宁可返回 <see langword="null"/> 排到末尾，也不要拿「已下载字节」与「比例」硬比。
    /// </remarks>
    public static IComparable? ParseDownloaded(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();

        // 分片进度与字节/时长进度本质上都是「完成比例」，统一成 0~1 才能同列比较
        var segment = SegmentProgressPattern().Match(trimmed);
        if (segment.Success)
        {
            if (!TryParsePart(segment.Groups["done"].Value, out var done)
                || !TryParsePart(segment.Groups["total"].Value, out var total)
                || total <= 0)
            {
                return null;
            }

            return Math.Clamp(done / (double)total, 0d, 1d);
        }

        var slash = trimmed.IndexOf('/');
        if (slash < 0)
        {
            return null;
        }

        if (!TryParseAmount(trimmed[..slash], out var downloadedKind, out var downloaded)
            || !TryParseAmount(trimmed[(slash + 1)..], out var totalKind, out var totalValue)
            || downloadedKind != totalKind
            || totalValue <= 0d)
        {
            return null;
        }

        var ratio = downloaded / totalValue;
        return double.IsFinite(ratio) ? Math.Clamp(ratio, 0d, 1d) : null;
    }

    /// <summary>
    /// 解析「已下载」中的单侧数值：带冒号按时长（秒），否则按体积（字节）。
    /// </summary>
    /// <param name="text">单侧文本。</param>
    /// <param name="kind">量纲。</param>
    /// <param name="value">秒或字节；允许为 0，负数视为无效。</param>
    /// <returns>解析成功返回 true。</returns>
    private static bool TryParseAmount(string text, out AmountKind kind, out double value)
    {
        var trimmed = text.Trim();

        if (trimmed.Contains(':'))
        {
            kind = AmountKind.Duration;
            if (ParseDuration(trimmed) is not TimeSpan duration)
            {
                value = 0d;
                return false;
            }

            value = duration.TotalSeconds;
        }
        else
        {
            kind = AmountKind.Size;
            if (ParseSizeValue(trimmed) is not double size)
            {
                value = 0d;
                return false;
            }

            value = size;
        }

        return double.IsFinite(value) && value >= 0d;
    }

    /// <summary>「已下载」单侧数值的量纲。</summary>
    private enum AmountKind
    {
        Size,
        Duration
    }

    /// <summary>
    /// 解析纯体积文本为字节数。
    /// </summary>
    /// <param name="text">列文本。</param>
    /// <returns>字节数；含 <c>/</c>、未知单位或无法解析时返回 <see langword="null"/>。</returns>
    private static double? ParseSizeValue(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)
            || text.Contains('/')
            || !TryParseQuantityText(text, out var value, out var unit)
            || !TryGetSizeMultiplier(unit, out var multiplier))
        {
            return null;
        }

        var result = value * multiplier;
        return double.IsFinite(result) && result <= MaxByteValue ? result : null;
    }

    /// <summary>
    /// 获取体积单位的换算倍数。
    /// </summary>
    /// <param name="unit">单位文本。</param>
    /// <param name="multiplier">换算倍数。</param>
    /// <returns>单位受支持返回 true；未知单位返回 false。</returns>
    private static bool TryGetSizeMultiplier(string? unit, out double multiplier)
    {
        switch (unit?.ToUpperInvariant())
        {
            case null or "" or "B":
                multiplier = 1d;
                return true;

            case "KB":
                multiplier = Kilobyte;
                return true;

            case "MB":
                multiplier = Megabyte;
                return true;

            case "GB":
                multiplier = Gigabyte;
                return true;

            case "TB":
                multiplier = Terabyte;
                return true;

            default:
                multiplier = 0d;
                return false;
        }
    }

    /// <summary>
    /// 获取通用数量单位的换算倍数（体积单位 + 无量纲单位）。
    /// </summary>
    /// <param name="unit">单位文本。</param>
    /// <param name="multiplier">换算倍数。</param>
    /// <returns>单位受支持返回 true；未知单位返回 false。</returns>
    private static bool TryGetMultiplier(string? unit, out double multiplier)
    {
        if (TryGetSizeMultiplier(unit, out multiplier))
        {
            return true;
        }

        switch (unit?.ToUpperInvariant())
        {
            case "X" or "%" or "片":
                multiplier = 1d;
                return true;

            default:
                multiplier = 0d;
                return false;
        }
    }

    /// <summary>
    /// 解析一个非负整数片段。
    /// </summary>
    /// <param name="text">片段文本。</param>
    /// <param name="value">解析结果。</param>
    /// <returns>解析成功返回 true。</returns>
    private static bool TryParsePart(string text, out int value)
        => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// 完整解析「可选 ≈ 前缀 + 数字 + 可选单位」的文本。
    /// </summary>
    /// <param name="text">列文本。</param>
    /// <param name="value">数字。</param>
    /// <param name="unit">单位；没有单位时为 null。</param>
    /// <returns>整段文本都合法时返回 true。</returns>
    /// <remarks>
    /// 必须整段匹配：逗号小数（<c>1,2 MB</c>）、重复小数点（<c>1.2.3 MB</c>）、
    /// 负号（含 <c>≈ -1 MB</c> 这类前缀负号）与尾部垃圾都视为解析失败，
    /// 不能从中间截一个数字出来当有效值。
    /// </remarks>
    private static bool TryParseQuantityText(string? text, out double value, out string? unit)
    {
        value = 0d;
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
                out value)
            || !double.IsFinite(value))
        {
            value = 0d;
            return false;
        }

        unit = match.Groups["unit"].Success ? match.Groups["unit"].Value : null;
        return true;
    }

    [GeneratedRegex(@"^\s*(?:≈\s*)?(?<value>[0-9]+(?:\.[0-9]+)?)\s*(?<unit>[A-Za-z]+|片|%)?\s*$")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"^\s*(?<done>[0-9]+)\s*/\s*(?<total>[0-9]+)\s*片\s*$")]
    private static partial Regex SegmentProgressPattern();

    [GeneratedRegex(@"^(?<month>[0-9]{1,2})-(?<day>[0-9]{1,2})\s+(?<hour>[0-9]{1,2}):(?<minute>[0-9]{1,2})$")]
    private static partial Regex MonthDayPattern();
}
