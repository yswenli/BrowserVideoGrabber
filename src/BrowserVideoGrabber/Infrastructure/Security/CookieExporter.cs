/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Infrastructure.Security
*文件名： CookieExporter
*版本号： V1.0.0.0
*唯一标识：06514f8c-0bfc-4c88-9f93-66c08ca57d6e
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 23:42:00
*描述：从 WebView2 会话导出 Cookie，供外部下载器复用浏览器的登录态。
*
*=================================================
*修改标记
*修改时间：2026/9/12 23:42:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Text;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BrowserVideoGrabber.Infrastructure.Security;

/// <summary>
/// Cookie 导出器。
/// </summary>
/// <remarks>
/// <para>
/// 导出 Cookie 是本工具能够下载「需要登录的内容」的关键：
/// 用户在浏览器里登录后，WebView2 会话中已持有有效的会话 Cookie，
/// 把它们注入到下载请求上即可让 ffmpeg 与原生下载器同样通过服务端鉴权。
/// </para>
/// <para>
/// <b>安全边界</b>：只导出与目标地址匹配的 Cookie（由 <c>GetCookiesAsync</c> 按域过滤），
/// 不做全量导出，也不落盘持久化，只在内存中传递到下载请求头。
/// </para>
/// </remarks>
public static class CookieExporter
{
    /// <summary>
    /// 导出指定地址可用的 Cookie 串。
    /// </summary>
    /// <param name="webView">WebView2 控件。</param>
    /// <param name="url">目标资源地址，用于确定需要导出哪些域的 Cookie。</param>
    /// <returns>
    /// 形如 <c>k1=v1; k2=v2</c> 的 Cookie 请求头取值；
    /// 无可用 Cookie 或 WebView2 尚未初始化时返回 null。
    /// </returns>
    public static Task<string?> ExportAsync(WebView2 webView, string url)
    {
        ArgumentNullException.ThrowIfNull(webView);

        var coreWebView = webView.CoreWebView2;

        // 浏览器尚未完成初始化，此时没有任何会话信息可导出
        return coreWebView is null
            ? Task.FromResult<string?>(null)
            : ExportAsync(coreWebView, url);
    }

    /// <summary>
    /// 从 WebView2 核心对象导出 Cookie 串。
    /// </summary>
    /// <param name="coreWebView">WebView2 核心对象。</param>
    /// <param name="url">目标资源地址。</param>
    /// <returns>Cookie 请求头取值；无可用 Cookie 时返回 null。</returns>
    public static async Task<string?> ExportAsync(CoreWebView2 coreWebView, string url)
    {
        ArgumentNullException.ThrowIfNull(coreWebView);

        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        try
        {
            var cookies = await coreWebView.CookieManager.GetCookiesAsync(url).ConfigureAwait(true);
            if (cookies is null || cookies.Count == 0)
            {
                return null;
            }

            var builder = new StringBuilder();

            foreach (var cookie in cookies)
            {
                if (string.IsNullOrWhiteSpace(cookie.Name))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append("; ");
                }

                builder.Append(cookie.Name).Append('=').Append(cookie.Value);
            }

            return builder.Length == 0 ? null : builder.ToString();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            // 地址格式非法或浏览器已释放：导出失败不应影响下载流程，只是拿不到登录态
            return null;
        }
    }
}
