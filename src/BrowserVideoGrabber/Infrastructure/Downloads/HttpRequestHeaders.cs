/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Infrastructure.Downloads
*文件名： HttpRequestHeaders
*版本号： V1.0.0.0
*唯一标识：cfebe5bc-a8f0-4937-bda0-a996d1a663d0
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 23:52:00
*描述：把请求上下文写入 HTTP 请求头的共享工具，供原生下载器与 ffmpeg 预检复用。
*
*=================================================
*修改标记
*修改时间：2026/9/12 23:52:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Infrastructure.Downloads;

/// <summary>
/// HTTP 请求头写入工具。
/// </summary>
/// <remarks>
/// 抽成共享工具而不是在两个下载器中各写一份，是因为「哪些头需要注入、如何处理非法值」
/// 属于同一个知识，一旦站点策略变化（例如新增 Sec-Fetch-* 校验），只需改一处。
/// </remarks>
public static class HttpRequestHeaders
{
    /// <summary>
    /// 把请求上下文中的鉴权与来源信息写入请求头。
    /// </summary>
    /// <param name="request">目标请求。</param>
    /// <param name="context">请求上下文。</param>
    public static void Apply(HttpRequestMessage request, RequestContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (!string.IsNullOrWhiteSpace(context.Referer)
            && Uri.TryCreate(context.Referer, UriKind.Absolute, out var referer))
        {
            request.Headers.Referrer = referer;
        }

        // 一律使用 TryAddWithoutValidation：UA 串常含括号与分号、
        // Cookie 串含 "=" 与 ";"，走严格校验会被判定为非法值而直接抛异常
        if (!string.IsNullOrWhiteSpace(context.UserAgent))
        {
            request.Headers.TryAddWithoutValidation("User-Agent", context.UserAgent);
        }

        // Origin 取「页面」的 origin 而非媒体地址自身：CDN 以该头做来源白名单校验，
        // 缺失或取错值会对每个分片返回合法但内容错误的占位数据。详见 RequestContext.Origin 备注。
        if (!string.IsNullOrWhiteSpace(context.Origin))
        {
            request.Headers.TryAddWithoutValidation("Origin", context.Origin);
        }

        if (!string.IsNullOrWhiteSpace(context.Cookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", context.Cookie);
        }

        foreach (var header in context.ExtraHeaders)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }
}
