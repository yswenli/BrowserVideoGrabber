/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
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

    /// <summary>
    /// 跨域请求来源标识，取值应为<b>来源页面</b>的 origin（<c>scheme://host[:port]</c>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>该头必须取「页面」的 origin，绝不能取媒体地址自己的 origin。</b>
    /// 早期结论「带 Origin 会导致 CDN 返回占位图」是<b>取值错误</b>造成的误判，已实测推翻：
    /// playergo 系 CDN 会把 <c>Origin</c> 与站点域名白名单比对，
    /// 传媒体地址自身的 <c>https://m3cdn.playergo.top</c> 或无关域名 <c>https://example.com</c>
    /// 都判为不合法并回退诱饵；只有传来源站点的 origin 才返回真实内容。
    /// </para>
    /// <para>
    /// 实测对照（同一条地址、同一时刻，仅 Origin 不同）：
    /// 不传 → 122 字节诱饵清单；传 CDN 自身 origin → 122 字节诱饵清单；
    /// 传页面 origin → 617 字节真实 master 清单，分片亦从 56024 字节占位图变为 1.1~1.4 MB 真实 TS。
    /// </para>
    /// <para>
    /// 诱饵的危险在于隐蔽：HTTP 200、<c>Content-Type</c> 合法、首字节是合法 TS 同步字 <c>0x47</c>，
    /// ffmpeg 能顺利 <c>-c copy</c> 并以退出码 0 结束，于是「下载成功」与「内容有效」完全脱钩。
    /// 因此本头不是可选项，而是取到真实内容的前提；分片级校验用于兜底，不能替代本头。
    /// </para>
    /// </remarks>
    public string? Origin { get; set; }

    /// <summary>会话 Cookie 串，形如 <c>k1=v1; k2=v2</c>。</summary>
    public string? Cookie { get; set; }

    /// <summary>
    /// 其余自定义请求头。键使用不区分大小写的比较器，避免重复附加同名头。
    /// </summary>
    /// <remarks>
    /// 提供 <c>init</c> 访问器而非只读属性，是为了让 JSON 反序列化能够整体替换该字典：
    /// 只读集合属性在部分序列化场景下无法被正确还原，会导致重启后自定义请求头静默丢失。
    /// </remarks>
    public IDictionary<string, string> ExtraHeaders { get; init; } =
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

        // Origin 必须为「页面」的 origin：部分 CDN 以该头做来源白名单校验，
        // 缺失或取值不对时会对每个分片返回合法但内容错误的占位数据。详见 Origin 属性备注。
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
