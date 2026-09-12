/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Models
*文件名： RequestContext
*版本号： V1.0.0.0
*唯一标识：67e73a84-8216-40b6-ae57-007e3a705182
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:14:00
*描述：下载请求上下文，承载反爬鉴权所需的 Referer / User-Agent / Origin / Cookie 等请求头。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:14:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Text;

namespace BrowserVideoGrabber.Core.Models;

/// <summary>
/// 下载请求上下文：承载反爬鉴权所需的 HTTP 请求头。
/// </summary>
/// <remarks>
/// <para>
/// 绝大多数流媒体地址会强校验 Referer / User-Agent / Origin / Cookie，直连必然返回 403。
/// 本对象在任务入队时从内嵌浏览器会话（WebView2 CookieManager + 当前页面地址）导出，
/// 下载时由 <c>HttpDownloadHandler</c> 与 <c>FfmpegArgumentBuilder</c> 分别消费。
/// </para>
/// <para>
/// 之所以把「构建 header 文本块」的能力放在这里而不是放在 ffmpeg 参数构建器中，
/// 是因为「有哪些头、顺序如何」属于上下文自身的知识，而「如何转成 ffmpeg 参数」才是构建器的职责。
/// </para>
/// </remarks>
public sealed class RequestContext
{
    /// <summary>来源页地址。缺失时部分站点会直接拒绝请求。</summary>
    public string? Referer { get; set; }

    /// <summary>浏览器 User-Agent。建议与内嵌浏览器保持一致，避免出现版本特征差异。</summary>
    public string? UserAgent { get; set; }

    /// <summary>跨域请求来源标识。</summary>
    public string? Origin { get; set; }

    /// <summary>会话 Cookie 串，形如 <c>k1=v1; k2=v2</c>。</summary>
    public string? Cookie { get; set; }

    /// <summary>其余自定义请求头。键使用不区分大小写的比较器，避免重复附加同名头。</summary>
    public IDictionary<string, string> ExtraHeaders { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 构建 HTTP 头文本块，每行以 CRLF 结尾。
    /// </summary>
    /// <returns>
    /// 形如 <c>Referer: https://a.com/\r\nUser-Agent: Mozilla/5.0\r\n</c> 的文本。
    /// 当所有字段均为空时返回空字符串。
    /// </returns>
    /// <remarks>
    /// 使用 CRLF 而非 LF 是 ffmpeg <c>-headers</c> 参数的硬性要求：
    /// 单个头部之间必须以 <c>\r\n</c> 分隔，整体作为一个进程参数传入。
    /// </remarks>
    public string BuildHeaderBlock()
    {
        var builder = new StringBuilder();

        // 局部函数：仅追加有值的头，避免产生 "Referer: " 这类空值头引起服务端校验异常
        void AppendHeader(string name, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            builder.Append(name).Append(": ").Append(value).Append("\r\n");
        }

        AppendHeader("Referer", Referer);
        AppendHeader("Origin", Origin);
        AppendHeader("User-Agent", UserAgent);
        AppendHeader("Cookie", Cookie);

        foreach (var header in ExtraHeaders)
        {
            AppendHeader(header.Key, header.Value);
        }

        return builder.ToString();
    }

    /// <summary>
    /// 创建当前上下文的深拷贝。
    /// </summary>
    /// <returns>新的 <see cref="RequestContext"/> 实例，字典内容独立，互不影响。</returns>
    public RequestContext Clone()
    {
        var clone = new RequestContext
        {
            Referer = Referer,
            UserAgent = UserAgent,
            Origin = Origin,
            Cookie = Cookie
        };

        foreach (var header in ExtraHeaders)
        {
            clone.ExtraHeaders[header.Key] = header.Value;
        }

        return clone;
    }

    /// <summary>
    /// 创建一个仅携带 User-Agent 的默认上下文。
    /// </summary>
    /// <param name="userAgent">User-Agent 字符串，为空时不设置该头。</param>
    /// <returns>新的 <see cref="RequestContext"/> 实例。</returns>
    public static RequestContext CreateDefault(string? userAgent = null)
        => new() { UserAgent = userAgent };
}
