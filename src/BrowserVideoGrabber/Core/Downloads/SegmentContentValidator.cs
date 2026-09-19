/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Downloads
*文件名： SegmentContentValidator
*版本号： V1.0.0.0
*唯一标识：26c4260e-3c54-44fa-836f-b27173e585b0
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 00:44:00
*描述：逐片内容校验器，识别 CDN 返回的占位/污染分片，并跨片检测重复指纹，支撑「部分成功」策略。
*
*=================================================
*修改标记
*修改时间：2026/9/13 00:44:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Security.Cryptography;

namespace BrowserVideoGrabber.Core.Downloads;

/// <summary>
/// 单个分片的内容校验结论。
/// </summary>
public sealed record SegmentVerdict
{
    /// <summary>该分片是否可用于拼接。</summary>
    public bool Usable { get; init; }

    /// <summary>不可用时的原因描述；可用时为 null。</summary>
    public string? Reason { get; init; }

    /// <summary>
    /// 构造一条校验结论。
    /// </summary>
    /// <param name="usable">是否可用。</param>
    /// <param name="reason">不可用原因；可用时留空。</param>
    public SegmentVerdict(bool usable, string? reason = null)
    {
        Usable = usable;
        Reason = reason;
    }
}

/// <summary>
/// 逐片内容校验器：识别被 CDN 污染的占位分片，并跨片检测「重复的同一份内容」。
/// </summary>
/// <remarks>
/// 实测结论（见 docs/review 诊断报告）是：污染响应是一段<b>格式完全合法</b>的 TS
/// （HTTP 200 + <c>Content-Type: image/jpeg</c> + 56024 字节 + 首字节 0x47）。
/// 因此「状态码」「TS 同步字」单独看都会放行，唯一稳定可靠的特征是
/// <b>多个不同地址返回了逐字节相同的内容</b>。本类把「重复指纹」作为主判据，
/// 并把图片/文本内容类型、TS 同步字缺失作为高置信辅助判据。
/// </remarks>
public sealed class SegmentContentValidator
{
    private readonly double _minimumUsableRatio;
    private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>
    /// 使用默认参数构造校验器。
    /// </summary>
    public SegmentContentValidator()
        : this(new SegmentValidatorOptions())
    {
    }

    /// <summary>
    /// 使用指定参数构造校验器。
    /// </summary>
    /// <param name="options">可调参数。</param>
    public SegmentContentValidator(SegmentValidatorOptions options)
    {
        _minimumUsableRatio = options?.MinimumUsableRatio ?? 0.1;
    }

    /// <summary>
    /// 校验单个分片的内容是否可用。
    /// </summary>
    /// <param name="contentType">响应的 Content-Type；可为 null。</param>
    /// <param name="length">响应体字节长度。</param>
    /// <param name="head">响应体前若干字节（用于同步字检查）。</param>
    /// <param name="expectTsSyncByte">是否要求首字节为 TS 同步字 0x47；fMP4 等非 TS 分片应设 false。</param>
    /// <returns>校验结论。</returns>
    public SegmentVerdict Inspect(string? contentType, long length, ReadOnlySpan<byte> head, bool expectTsSyncByte = true)
    {
        if (length <= 0)
        {
            return new SegmentVerdict(false, "分片内容为空");
        }

        if (!string.IsNullOrWhiteSpace(contentType) && contentType!.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return new SegmentVerdict(false, $"内容类型 {contentType} 为图片，疑似占位响应");
        }

        if (IsTextual(contentType))
        {
            return new SegmentVerdict(false, $"内容类型 {contentType} 为文本，疑似错误页或限流页");
        }

        if (expectTsSyncByte && head.Length > 0 && head[0] != 0x47)
        {
            return new SegmentVerdict(false, "首字节不是 TS 同步字 0x47，可能不是 MPEG-TS 分片");
        }

        return new SegmentVerdict(true);
    }

    /// <summary>
    /// 计算内容指纹：同时受「长度」与「首尾字节」影响，定长为 SHA-256 十六进制摘要。
    /// </summary>
    /// <param name="head">内容首部字节（取前若干字节即可，无需整段）。</param>
    /// <param name="tail">内容尾部字节。</param>
    /// <param name="length">内容总长度（字节）。</param>
    /// <returns>64 字符的十六进制指纹。</returns>
    public static string ComputeFingerprint(ReadOnlySpan<byte> head, ReadOnlySpan<byte> tail, long length)
    {
        var lengthBytes = BitConverter.GetBytes(length);
        var buffer = new byte[head.Length + tail.Length + lengthBytes.Length];
        head.CopyTo(buffer);
        tail.CopyTo(buffer.AsSpan(head.Length));
        lengthBytes.CopyTo(buffer.AsSpan(head.Length + tail.Length));

        var hash = SHA256.HashData(buffer);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// 记录一份指纹，供后续 <see cref="IsDuplicate"/> 判定。
    /// </summary>
    /// <param name="fingerprint">由 <see cref="ComputeFingerprint"/> 生成的指纹。</param>
    public void Remember(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return;
        }

        lock (_gate)
        {
            _seen.Add(fingerprint);
        }
    }

    /// <summary>
    /// 判断指定指纹是否在此之前已被记录（即不同分片返回了同一份内容）。
    /// </summary>
    /// <param name="fingerprint">待检查的指纹。</param>
    /// <returns>若已记录则为 true（疑似重复占位）。</returns>
    public bool IsDuplicate(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return false;
        }

        lock (_gate)
        {
            return _seen.Contains(fingerprint);
        }
    }

    /// <summary>
    /// 原子地「检测并登记」一份指纹：若此前未见过则登记并返回 true（可取回），
    /// 若已见过则直接返回 false（重复占位）。
    /// </summary>
    /// <remarks>
    /// 取片是并发进行的，若把 <see cref="IsDuplicate"/> 与 <see cref="Remember"/> 分开调用，
    /// 多个内容相同的占位分片可能同时看到「未重复」而都被判为可取回，从而漏掉重复检测。
    /// 因此并发场景必须走本方法，由同一把锁保证「检测+登记」不可分割。
    /// </remarks>
    /// <param name="fingerprint">由 <see cref="ComputeFingerprint"/> 生成的指纹。</param>
    /// <returns>指纹首次出现（可取回）则为 true；已重复（应判废）则为 false。</returns>
    public bool TryClaim(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return false;
        }

        lock (_gate)
        {
            if (_seen.Contains(fingerprint))
            {
                return false;
            }

            _seen.Add(fingerprint);
            return true;
        }
    }

    /// <summary>
    /// 根据可用分片占比判断是否仍值得拼成成品。
    /// </summary>
    /// <param name="total">分片总数。</param>
    /// <param name="usable">可用分片数。</param>
    /// <returns>值得保留则为 true。</returns>
    public bool IsWorthKeeping(int total, int usable)
    {
        if (total <= 0 || usable <= 0)
        {
            return false;
        }

        return (double)usable / total >= _minimumUsableRatio;
    }

    /// <summary>
    /// 判定内容类型是否为文本（错误页/限流页的典型形态）。
    /// </summary>
    /// <param name="contentType">内容类型。</param>
    /// <returns>是文本则为 true。</returns>
    private static bool IsTextual(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        if (contentType!.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("application/javascript", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);
    }
}
