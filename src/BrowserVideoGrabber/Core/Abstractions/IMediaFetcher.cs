/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Abstractions
*文件名： IMediaFetcher
*版本号： V1.0.0.0
*唯一标识：a942788f-a65e-4dd4-bec2-b5c900173896
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 03:44:00
*描述：媒体资源抓取抽象，定义「按地址取文本 / 取字节 / 取到文件」三类能力。
*
*=================================================
*修改标记
*修改时间：2026/9/13 03:44:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Core.Abstractions;

/// <summary>
/// 媒体资源抓取抽象：从远端按地址取回文本、字节或文件。
/// </summary>
/// <remarks>
/// <para>
/// 引入该抽象的目的是把「下载决策」与「网络传输」彻底分开。HLS 下载的全部编排逻辑
/// （选清晰度、规划分片、逐片校验、AES 解密、缺失统计）都依赖它，而这些逻辑恰恰是最需要
/// 被精确测试的部分。只有把网络调用收口到这唯一一个接口上，
/// 才能在单测里用内存实现构造出「第 7 个分片返回占位图」「密钥返回 16 字节」这类场景。
/// </para>
/// <para>
/// <b>为什么不直接用 <c>HttpClient</c></b>：<c>HttpClient</c> 是具体类型，无法在不联网的
/// 前提下控制响应；而它在 Core 层出现还会把 <c>System.Net.Http</c> 的实现细节带进领域模型。
/// </para>
/// <para>
/// 实现方约定：<b>不要抛异常表达业务失败</b>。地址失效、403、超时等都应落到
/// <see cref="MediaFetchResult.Success"/> 为 false 的结果上并给出中文原因；
/// 只有调用方取消才允许抛 <see cref="OperationCanceledException"/> 并向上传播。
/// </para>
/// </remarks>
public interface IMediaFetcher
{
    /// <summary>
    /// 取回文本内容（用于 m3u8 清单）。
    /// </summary>
    /// <param name="url">资源绝对地址。</param>
    /// <param name="context">请求上下文（Referer / User-Agent / Cookie）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>结果；成功时 <see cref="MediaFetchResult.Text"/> 为响应正文。</returns>
    Task<MediaFetchResult> GetStringAsync(
        string url,
        RequestContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// 取回字节内容（用于 AES-128 密钥这类小体积资源）。
    /// </summary>
    /// <param name="url">资源绝对地址。</param>
    /// <param name="context">请求上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>结果；成功时 <see cref="MediaFetchResult.Bytes"/> 为响应正文。</returns>
    Task<MediaFetchResult> GetBytesAsync(
        string url,
        RequestContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// 取回并写入指定文件（用于分片）。
    /// </summary>
    /// <param name="url">资源绝对地址。</param>
    /// <param name="context">请求上下文。</param>
    /// <param name="destinationPath">目标文件路径，实现方负责创建所需目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>结果；成功时 <see cref="MediaFetchResult.Length"/> 为落盘字节数。</returns>
    /// <remarks>
    /// 之所以提供「直接落盘」而非「先返回字节再落盘」，是因为分片体积可达数百 KB 至数 MB，
    /// 并发取片时把所有内容留在内存会造成不必要的压力。
    /// </remarks>
    Task<MediaFetchResult> GetToFileAsync(
        string url,
        RequestContext context,
        string destinationPath,
        CancellationToken cancellationToken);
}
