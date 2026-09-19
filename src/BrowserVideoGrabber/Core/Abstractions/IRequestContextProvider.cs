/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Abstractions
*文件名： IRequestContextProvider
*版本号： V1.0.0.0
*唯一标识：f246d094-6e66-4f6f-9d43-dd19aaf0f721
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:26:00
*描述：请求上下文提供者抽象，负责从浏览器会话导出下载所需的鉴权请求头。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:26:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Core.Abstractions;

/// <summary>
/// 请求上下文提供者抽象：为指定资源地址构造带鉴权信息的下载请求上下文。
/// </summary>
/// <remarks>
/// 该接口把「从浏览器会话取 Cookie / UA / Referer」的能力与嗅探器解耦：
/// 嗅探器只负责发现地址，何时取、取哪些域名的 Cookie 由调用方决定。
/// 这样也便于在无浏览器的场景（如命令行批量下载）下替换为固定上下文的实现。
/// </remarks>
public interface IRequestContextProvider
{
    /// <summary>
    /// 为指定资源地址创建请求上下文。
    /// </summary>
    /// <param name="resourceUrl">即将下载的资源地址，用于确定需要导出哪些域的 Cookie。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>
    /// 携带 Referer / User-Agent / Cookie 的上下文。
    /// 无法获取任何信息时返回空上下文而非 null，调用方无需判空。
    /// </returns>
    Task<RequestContext> CreateAsync(string resourceUrl, CancellationToken cancellationToken = default);
}
