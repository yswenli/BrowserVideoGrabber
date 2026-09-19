/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Fakes
*文件名： FakeHttpMessageHandler
*版本号： V1.0.0.0
*唯一标识：b8942641-7c2e-4d19-9a35-8f1b6c2d4e57
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 00:22:00
*描述：HTTP 消息处理器的假实现，支持 Range 分段响应，用于原生下载器的离线单测。
*
*=================================================
*修改标记
*修改时间：2026/9/13 00:22:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace BrowserVideoGrabber.Tests.Fakes;

/// <summary>
/// <see cref="HttpMessageHandler"/> 的假实现：从固定字节数组提供服务，并模拟 Range 分段响应。
/// </summary>
/// <remarks>
/// 有了它，分片下载、断点续传、状态码映射这三类逻辑都可以在不联网的前提下被精确验证，
/// 而且可以按需伪造「服务端不支持 Range」「返回 403」等真实网络里难以稳定复现的场景。
/// 同时它会记录每一次请求的请求头与 Range 区间，作为「请求是否携带鉴权信息」「是否真的续传」的证据。
/// </remarks>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly byte[] _content;
    private readonly object _sync = new();
    private readonly List<Dictionary<string, string>> _requestHeaders = new();
    private readonly List<RangeItemHeaderValue?> _ranges = new();

    /// <summary>
    /// 初始化假处理器。
    /// </summary>
    /// <param name="content">要提供的文件内容。</param>
    public FakeHttpMessageHandler(byte[] content) => _content = content;

    /// <summary>服务端是否声明支持 Range。为 false 时任何请求都返回 200 与完整内容。</summary>
    public bool SupportsRange { get; set; } = true;

    /// <summary>强制返回的状态码。非 200 时直接以该状态码失败，不再提供内容。</summary>
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

    /// <summary>每次响应前的模拟延时，用于测试取消传播。</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    /// <summary>已接收的请求总数。</summary>
    public int RequestCount
    {
        get
        {
            lock (_sync)
            {
                return _requestHeaders.Count;
            }
        }
    }

    /// <summary>按请求顺序记录的请求头快照。</summary>
    public IReadOnlyList<Dictionary<string, string>> CapturedRequestHeaders
    {
        get
        {
            lock (_sync)
            {
                return _requestHeaders.ToList();
            }
        }
    }

    /// <summary>按请求顺序记录的 Range 区间；未携带 Range 的请求对应 null。</summary>
    public IReadOnlyList<RangeItemHeaderValue?> CapturedRanges
    {
        get
        {
            lock (_sync)
            {
                return _ranges.ToList();
            }
        }
    }

    /// <summary>
    /// 用文本内容构造假处理器，便于测试中直观书写。
    /// </summary>
    /// <param name="text">文本内容。</param>
    /// <returns>假处理器实例。</returns>
    public static FakeHttpMessageHandler FromText(string text) => new(Encoding.UTF8.GetBytes(text));

    /// <summary>
    /// 构造一段内容可识别的字节序列：第 n 个字节等于 n 对 251 取模。
    /// </summary>
    /// <param name="length">字节数。</param>
    /// <returns>字节数组。</returns>
    /// <remarks>
    /// 每个位置的值都不相同（251 为质数，在 251 以内不会重复），
    /// 因此拼接顺序一旦出错，断言「输出与源逐字节相等」就能立刻暴露问题。
    /// </remarks>
    public static byte[] CreatePatternContent(int length)
    {
        var content = new byte[length];
        for (var index = 0; index < length; index++)
        {
            content[index] = (byte)(index % 251);
        }

        return content;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 使用 NonValidated 视图取值：直接枚举 Headers 会把 User-Agent 按产品记号重新解析，
        // "Mozilla/5.0 Test" 会被拆成两段并以逗号拼接，导致断言拿到被改写的值
        foreach (var header in request.Headers.NonValidated)
        {
            headers[header.Key] = header.Value.ToString();
        }

        var range = request.Headers.Range?.Ranges.FirstOrDefault();

        lock (_sync)
        {
            _requestHeaders.Add(headers);
            _ranges.Add(range);
        }

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
        }

        if (StatusCode != HttpStatusCode.OK)
        {
            return new HttpResponseMessage(StatusCode) { Content = new ByteArrayContent([]) };
        }

        if (!SupportsRange || range?.From is null)
        {
            return BuildFullResponse();
        }

        return BuildRangeResponse(range);
    }

    /// <summary>
    /// 构造整文件响应（200）。
    /// </summary>
    /// <returns>响应消息。</returns>
    private HttpResponseMessage BuildFullResponse()
    {
        var content = CreateVideoContent(_content);

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    /// <summary>
    /// 构造分段响应（206）。
    /// </summary>
    /// <param name="range">请求的 Range 区间。</param>
    /// <returns>响应消息。</returns>
    private HttpResponseMessage BuildRangeResponse(RangeItemHeaderValue range)
    {
        var start = range.From ?? 0;
        var total = _content.Length;

        if (start >= total)
        {
            return new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                Content = new ByteArrayContent([])
            };
        }

        var end = Math.Min(range.To ?? (total - 1), total - 1);
        var length = (int)(end - start + 1);
        var slice = new byte[length];
        Array.Copy(_content, (int)start, slice, 0, length);

        var content = CreateVideoContent(slice);
        content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, total);

        return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
    }

    /// <summary>
    /// 构造带视频内容类型的字节内容。
    /// </summary>
    /// <param name="bytes">字节数据。</param>
    /// <returns>HTTP 内容。</returns>
    private static ByteArrayContent CreateVideoContent(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        return content;
    }
}
