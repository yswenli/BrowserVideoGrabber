/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Models
*文件名： MediaFetchResult
*版本号： V1.0.0.0
*唯一标识：f0e60069-c99e-42fc-80ef-7a48ab3fe49b
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 03:45:00
*描述：媒体抓取结果模型，同时承载响应元数据（状态码 / 内容类型 / 长度）与正文字节。
*
*=================================================
*修改标记
*修改时间：2026/9/13 03:45:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

namespace BrowserVideoGrabber.Core.Models;

/// <summary>
/// 一次媒体抓取的结局。
/// </summary>
/// <remarks>
/// <para>
/// 之所以把「状态码」与「内容类型」这两项协议层信息一路带到领域层，
/// 而不是在抓取实现里就地消化，是因为它们本身就是<b>判定内容真伪的关键证据</b>：
/// 真实站点在令牌失效时会返回 HTTP 200 + <c>image/jpeg</c> 的占位图，
/// 若抓取层只回报「成功 / 失败」，上层就永远发现不了「下载成功但内容不是视频」。
/// </para>
/// <para>
/// 本类型用同一套字段承载三类抓取的产物（文本 / 字节 / 落盘），
/// 避免为一个仅有返回载体差异的场景维护三个几乎相同的记录。
/// </para>
/// </remarks>
public sealed record MediaFetchResult
{
    /// <summary>本次抓取是否成功（成功指「拿到了响应且状态码为 2xx」）。</summary>
    public bool Success { get; init; }

    /// <summary>HTTP 状态码。未产生响应时为 0。</summary>
    public int StatusCode { get; init; }

    /// <summary>响应声明的 <c>Content-Type</c>。未声明时为 null。</summary>
    public string? ContentType { get; init; }

    /// <summary>响应正文长度（字节）。落盘抓取时为实际写盘字节数。</summary>
    public long Length { get; init; }

    /// <summary>失败原因（中文描述）。成功时为空。</summary>
    public string? Error { get; init; }

    /// <summary>该失败是否值得重试。</summary>
    public bool IsRetryable { get; init; }

    /// <summary>文本正文。仅 <c>GetStringAsync</c> 成功时填充。</summary>
    public string? Text { get; init; }

    /// <summary>字节正文。仅 <c>GetBytesAsync</c> 成功时填充。</summary>
    public byte[]? Bytes { get; init; }

    /// <summary>
    /// 构造成功结果。
    /// </summary>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="contentType">响应内容类型。</param>
    /// <param name="length">正文长度（字节）。</param>
    /// <param name="text">文本正文，可选。</param>
    /// <param name="bytes">字节正文，可选。</param>
    /// <returns>成功结果实例。</returns>
    public static MediaFetchResult Ok(
        int statusCode,
        string? contentType,
        long length,
        string? text = null,
        byte[]? bytes = null)
        => new()
        {
            Success = true,
            StatusCode = statusCode,
            ContentType = contentType,
            Length = length,
            Text = text,
            Bytes = bytes
        };

    /// <summary>
    /// 构造失败结果。
    /// </summary>
    /// <param name="error">失败原因（中文描述）。</param>
    /// <param name="isRetryable">是否值得重试，默认 true。</param>
    /// <param name="statusCode">已获得的 HTTP 状态码；无响应时为 0。</param>
    /// <returns>失败结果实例。</returns>
    public static MediaFetchResult Fail(string error, bool isRetryable = true, int statusCode = 0)
        => new()
        {
            Success = false,
            Error = error,
            IsRetryable = isRetryable,
            StatusCode = statusCode
        };
}
