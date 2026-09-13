/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Infrastructure.Sniffing
*文件名： NetworkSniffRules
*版本号： V1.0.0.0
*唯一标识：84dff95e-6d98-4cd0-90fa-481032a07eaa
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 23:36:00
*描述：网络嗅探规则，定义哪些响应值得检查以及如何读取响应头。
*
*=================================================
*修改标记
*修改时间：2026/9/12 23:36:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Models;
using BrowserVideoGrabber.Core.Sniffing;
using Microsoft.Web.WebView2.Core;

namespace BrowserVideoGrabber.Infrastructure.Sniffing;

/// <summary>
/// 网络嗅探规则。
/// </summary>
/// <remarks>
/// 该类只承载「嗅探器专属」的判定逻辑（哪些协议不必检查、如何从 WebView2 的响应头对象中取值），
/// 而「什么样的 URL / Content-Type 算视频」这一领域知识统一委托给 Core 层的
/// <see cref="VideoUrlMatcher"/>，避免同一条规则在两处各写一份而逐渐不一致。
/// </remarks>
public static class NetworkSniffRules
{
    /// <summary>无需检查的协议前缀。</summary>
    /// <remarks>
    /// <c>data:</c> 与 <c>blob:</c> 是内存内的伪地址，没有对应的可下载资源；
    /// <c>chrome-extension:</c> 与 <c>devtools:</c> 是浏览器自身资源，与页面内容无关。
    /// 提前排除它们可以显著减少无效判定。
    /// </remarks>
    private static readonly string[] IgnoredSchemePrefixes =
    {
        "data:",
        "blob:",
        "devtools:",
        "chrome-extension:",
        "about:",
        "filesystem:"
    };

    /// <summary>
    /// 判断一个请求地址是否值得进入嗅探流程。
    /// </summary>
    /// <param name="url">请求地址。</param>
    /// <returns>值得检查返回 true。</returns>
    public static bool ShouldInspect(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        foreach (var prefix in IgnoredSchemePrefixes)
        {
            if (url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 判定响应是否为视频资源。
    /// </summary>
    /// <param name="url">请求地址。</param>
    /// <param name="contentType">响应头中的 Content-Type。</param>
    /// <param name="format">输出参数：识别到的格式。</param>
    /// <returns>是视频资源返回 true。</returns>
    public static bool TryIdentify(string? url, string? contentType, out VideoFormat format)
        => VideoUrlMatcher.TryMatch(url, contentType, out format);

    /// <summary>
    /// 从 WebView2 响应头集合中读取 Content-Type。
    /// </summary>
    /// <param name="headers">WebView2 响应头集合，允许为 null。</param>
    /// <returns>Content-Type 取值；不存在时返回 null。</returns>
    public static string? ReadContentType(CoreWebView2HttpResponseHeaders? headers)
    {
        if (headers is null)
        {
            return null;
        }

        // WebView2 的响应头集合没有 TryGet 语义：必须先 Contains 再 GetHeader，
        // 直接取值在缺失时只会拿到空串，无法区分「没有该头」与「值为空」
        return headers.Contains("Content-Type") ? headers.GetHeader("Content-Type") : null;
    }
}
