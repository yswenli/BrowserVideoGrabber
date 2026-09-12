/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Infrastructure.Sniffing
*文件名： WebView2RequestContextProvider
*版本号： V1.0.0.0
*唯一标识：f34915dc-0c6f-4d3e-b8c2-f481594df2fa
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 23:48:00
*描述：从 WebView2 会话导出请求上下文，为下载请求补齐 Referer / UA / Cookie。
*
*=================================================
*修改标记
*修改时间：2026/9/12 23:48:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Abstractions;
using BrowserVideoGrabber.Core.Configuration;
using BrowserVideoGrabber.Core.Models;
using BrowserVideoGrabber.Infrastructure.Security;
using Microsoft.Web.WebView2.WinForms;

namespace BrowserVideoGrabber.Infrastructure.Sniffing;

/// <summary>
/// 基于 WebView2 会话的请求上下文提供者。
/// </summary>
/// <remarks>
/// <para>
/// 职责单一：把「浏览器当前的登录态与来源信息」翻译成下载器可用的请求头。
/// 之所以从嗅探器中独立出来，是因为二者关注点不同 ——
/// 嗅探器关心「发现了什么」，本类关心「用什么身份去取」，且前者会随内核更换而变，后者不会。
/// </para>
/// <para>
/// <b>Referer 的来源</b>：取浏览器当前顶层文档地址。真实场景中，分片请求的 Referer
/// 有时是 iframe 内的播放页地址而非顶层地址，但 WebView2 未暴露请求发起者信息，
/// 顶层地址已是可获取的最接近值；若站点校验失败，错误信息会提示用户检查 Referer。
/// </para>
/// </remarks>
public sealed class WebView2RequestContextProvider : IRequestContextProvider
{
    private readonly WebView2 _webView;
    private readonly AppSettings? _settings;

    /// <summary>
    /// 初始化提供者。
    /// </summary>
    /// <param name="webView">WebView2 控件，用于读取当前会话。</param>
    /// <param name="settings">应用设置，用于读取用户自定义的 User-Agent。</param>
    public WebView2RequestContextProvider(WebView2 webView, AppSettings? settings = null)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _settings = settings;
    }

    /// <inheritdoc />
    public async Task<RequestContext> CreateAsync(string resourceUrl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var context = new RequestContext();
        var coreWebView = _webView.CoreWebView2;

        if (coreWebView is null)
        {
            // 浏览器未初始化：返回空上下文，下载会以「无鉴权」方式尝试
            return context;
        }

        // User-Agent：优先使用用户在设置中显式指定的值，否则沿用浏览器当前 UA
        var configuredUserAgent = _settings?.UserAgent;
        context.UserAgent = string.IsNullOrWhiteSpace(configuredUserAgent)
            ? coreWebView.Settings.UserAgent
            : configuredUserAgent;

        var pageUrl = coreWebView.Source;
        if (!string.IsNullOrWhiteSpace(pageUrl) && Uri.TryCreate(pageUrl, UriKind.Absolute, out var pageUri))
        {
            context.Referer = pageUrl;
            context.Origin = $"{pageUri.Scheme}://{pageUri.Authority}";
        }

        var cookie = await CookieExporter.ExportAsync(coreWebView, resourceUrl).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            context.Cookie = cookie;
        }

        return context;
    }
}
