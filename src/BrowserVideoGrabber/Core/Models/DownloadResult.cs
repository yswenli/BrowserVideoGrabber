/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Models
*文件名： DownloadResult
*版本号： V1.0.0.0
*唯一标识：aba5b01f-a136-44a4-86e8-62f56a0404a3
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:18:00
*描述：下载结果模型，描述单次下载尝试的成败、失败原因与是否可重试。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:18:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

namespace BrowserVideoGrabber.Core.Models;

/// <summary>
/// 下载结果：描述「一次」下载尝试的结局。
/// </summary>
/// <remarks>
/// 注意区分「一次尝试的结果」与「任务的最终状态」：
/// 一次尝试失败但 <see cref="IsRetryable"/> 为 true 时，任务会被重新置为待下载并再次尝试；
/// 只有尝试失败且不可重试，或重试次数耗尽，任务才会落到 Failed 终态。
/// </remarks>
public sealed record DownloadResult
{
    /// <summary>本次尝试是否成功。</summary>
    public bool Success { get; init; }

    /// <summary>失败原因（中文描述）。成功时为空。</summary>
    public string? Error { get; init; }

    /// <summary>成功时的输出文件完整路径。</summary>
    public string? OutputPath { get; init; }

    /// <summary>成功时的输出文件字节数。</summary>
    public long OutputBytes { get; init; }

    /// <summary>
    /// 本次尝试是否为「部分成功」：分片有缺失，成品文件含有空白时段。
    /// </summary>
    /// <remarks>
    /// 部分成功仍属于成功（<see cref="Success"/> 为 true、任务进入已完成终态），
    /// 但与完整成功不同，界面应据此外显「缺失时段」提示，而非把它伪装成完好的视频。
    /// </remarks>
    public bool IsPartial { get; init; }

    /// <summary>部分成功的说明文字（缺失时段、可用分片占比等）；完整成功时为空。</summary>
    public string? PartialDetail { get; init; }

    /// <summary>
    /// 该失败是否值得重试。
    /// 网络中断、5xx、超时等属于可重试；DRM 保护、404、参数错误属于不可重试。
    /// </summary>
    public bool IsRetryable { get; init; }

    /// <summary>是否为 DRM / 受保护内容导致的失败。界面据此给出专门的提示文案。</summary>
    public bool IsDrmProtected { get; init; }

    /// <summary>
    /// 构造成功结果。
    /// </summary>
    /// <param name="outputPath">输出文件完整路径。</param>
    /// <param name="outputBytes">输出文件字节数。</param>
    /// <returns>成功结果实例。</returns>
    public static DownloadResult Ok(string outputPath, long outputBytes)
        => new()
        {
            Success = true,
            OutputPath = outputPath,
            OutputBytes = outputBytes
        };

    /// <summary>
    /// 构造「部分成功」结果。
    /// </summary>
    /// <param name="outputPath">输出文件完整路径。</param>
    /// <param name="outputBytes">输出文件字节数。</param>
    /// <param name="partialDetail">部分成功的说明文字（缺失时段等）。</param>
    /// <returns>部分成功结果实例。<see cref="Success"/> 为 true、<see cref="IsPartial"/> 为 true。</returns>
    /// <remarks>
    /// 与 <see cref="Ok"/> 的唯一区别是 <see cref="IsPartial"/> 标记为 true 并附带说明。
    /// 部分成功仍计入成功：任务落到「已完成」终态，但界面会据
    /// <paramref name="partialDetail"/> 如实标注缺失时段，而不是把残缺文件伪装成完整视频。
    /// </remarks>
    public static DownloadResult OkPartial(string outputPath, long outputBytes, string? partialDetail)
        => new()
        {
            Success = true,
            OutputPath = outputPath,
            OutputBytes = outputBytes,
            IsPartial = true,
            PartialDetail = partialDetail
        };

    /// <summary>
    /// 构造失败结果。
    /// </summary>
    /// <param name="error">失败原因（中文描述）。</param>
    /// <param name="isRetryable">是否值得重试，默认 true。</param>
    /// <param name="isDrmProtected">是否为 DRM 保护内容，默认 false。</param>
    /// <returns>失败结果实例。DRM 保护失败强制标记为不可重试。</returns>
    public static DownloadResult Fail(string error, bool isRetryable = true, bool isDrmProtected = false)
        => new()
        {
            Success = false,
            Error = error,
            // DRM 内容重试多少次都不会成功，强制不可重试，避免无意义地占满重试次数
            IsRetryable = isDrmProtected ? false : isRetryable,
            IsDrmProtected = isDrmProtected
        };
}
