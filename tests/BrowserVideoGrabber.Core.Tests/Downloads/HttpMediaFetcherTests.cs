/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Downloads
*文件名： HttpMediaFetcherTests
*版本号： V1.0.0.0
*唯一标识：a41c7f29-6d3e-4b18-9a52-0c7e8f5b3d61
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/11/20 22:20:00
*描述：HttpMediaFetcher 的单元测试，覆盖「页面 Origin 被 CDN 拒绝时自适应回退」这一根因修复。
*
*=================================================
*修改标记
*修改时间：2026/11/20 22:20:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BrowserVideoGrabber.Core.Models;
using BrowserVideoGrabber.Infrastructure.Downloads;
using BrowserVideoGrabber.Tests.Fakes;

namespace BrowserVideoGrabber.Tests.Downloads;

/// <summary>
/// <see cref="HttpMediaFetcher"/> 的行为验证。
/// </summary>
/// <remarks>
/// <para>
/// 本组用例固化一次真实故障的根因结论：下载请求携带「页面 Origin」时，
/// 部分 CDN（如 qnlycc 系）对每个分片直接返回 403；而另一部分 CDN（如 playergo 系）
/// 又依赖该头返回真实内容。两种站点策略互相矛盾，因此既不能一律携带、也不能一律剔除，
/// 只能「默认携带、被 403 拒绝时回退重试一次不带 Origin，且按主机记住结论」。
/// </para>
/// <para>
/// 桩处理器记录每一次请求的全部请求头，作为「回退是否发生、是否只发生一次、
/// 是否只剔除 Origin 而保留 Referer/UA」的判定依据。
/// </para>
/// </remarks>
public sealed class HttpMediaFetcherTests
{
    private const string PlaylistUrl = "https://cdn.example.com/video/index.m3u8";
    private const string KeyUrl = "https://cdn.example.com/video/crypt.key";
    private const string SegmentUrl = "https://cdn.example.com/video/seg0.ts";

    private const string PlaylistText = "#EXTM3U\n#EXT-X-TARGETDURATION:6\n";
    private const string Referer = "https://page.example.com/watch?id=1";

    /// <summary>
    /// 上下文带 Origin 而 CDN 以 403 拒绝时，取清单必须回退为「不带 Origin」重试一次并取回正文。
    /// </summary>
    [Fact]
    public async Task GetStringAsync_ShouldRetryWithoutOrigin_WhenCdnRejectsOrigin()
    {
        var handler = new OriginRejectingHandler(Encoding.UTF8.GetBytes(PlaylistText), "application/vnd.apple.mpegurl");
        var fetcher = CreateFetcher(handler);

        var result = await fetcher.GetStringAsync(PlaylistUrl, CreateContextWithOrigin(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(PlaylistText, result.Text);

        // 第一次带 Origin 被拒，第二次不带 Origin 命中
        Assert.Equal(2, handler.CapturedHeaders.Count);
        Assert.Contains("Origin", handler.CapturedHeaders[0].Keys);
        Assert.DoesNotContain("Origin", handler.CapturedHeaders[1].Keys);

        // 回退只剔除 Origin：Referer 与 UA 必须原样保留，否则换成另一种 403
        Assert.Equal(Referer, handler.CapturedHeaders[1]["Referer"]);
        Assert.Contains("User-Agent", handler.CapturedHeaders[1]);
    }

    /// <summary>
    /// 取密钥同样走回退：第一次带 Origin 被拒，第二次不带 Origin 拿到 16 字节密钥。
    /// </summary>
    [Fact]
    public async Task GetBytesAsync_ShouldRetryWithoutOrigin_WhenCdnRejectsOrigin()
    {
        var key = new byte[16];
        Random.Shared.NextBytes(key);
        var handler = new OriginRejectingHandler(key, "application/octet-stream");
        var fetcher = CreateFetcher(handler);

        var result = await fetcher.GetBytesAsync(KeyUrl, CreateContextWithOrigin(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(key, result.Bytes);
        Assert.Equal(2, handler.CapturedHeaders.Count);
        Assert.DoesNotContain("Origin", handler.CapturedHeaders[1].Keys);
    }

    /// <summary>
    /// 取分片（落盘）走回退：正文必须完整写入目标文件，且只重试一次。
    /// </summary>
    [Fact]
    public async Task GetToFileAsync_ShouldRetryWithoutOrigin_WhenCdnRejectsOrigin()
    {
        var payload = new byte[2048];
        Random.Shared.NextBytes(payload);
        var handler = new OriginRejectingHandler(payload, "video/mp2t");
        var fileSystem = new FakeFileSystem();
        var fetcher = CreateFetcher(handler, fileSystem);
        const string destination = @"C:\downloads\seg0.ts";

        var result = await fetcher.GetToFileAsync(SegmentUrl, CreateContextWithOrigin(), destination, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(payload, fileSystem.ReadFile(destination));
        Assert.Equal(2, handler.CapturedHeaders.Count);
        Assert.DoesNotContain("Origin", handler.CapturedHeaders[1].Keys);
    }

    /// <summary>
    /// 主机拒绝 Origin 的结论要被记住：同一主机的后续请求不再先发一发注定 403 的请求。
    /// </summary>
    [Fact]
    public async Task GetBytesAsync_ShouldSkipOriginOnLaterRequests_AfterHostRejectedIt()
    {
        var handler = new OriginRejectingHandler(new byte[] { 1, 2, 3 }, "application/octet-stream");
        var fetcher = CreateFetcher(handler);
        var context = CreateContextWithOrigin();

        var first = await fetcher.GetBytesAsync(KeyUrl, context, CancellationToken.None);
        var second = await fetcher.GetBytesAsync(KeyUrl, context, CancellationToken.None);

        Assert.True(first.Success);
        Assert.True(second.Success);

        // 第一次调用 2 个请求（403 + 回退成功），第二次调用只发 1 个且不带 Origin
        Assert.Equal(3, handler.CapturedHeaders.Count);
        Assert.DoesNotContain("Origin", handler.CapturedHeaders[2].Keys);
    }

    /// <summary>
    /// 正常站点（带 Origin 即 200）不能被误判：Origin 一直保留，不会学会一个错误结论。
    /// </summary>
    [Fact]
    public async Task GetBytesAsync_ShouldKeepOrigin_WhenCdnAcceptsIt()
    {
        var handler = new OriginRejectingHandler(new byte[] { 9, 9 }, "application/octet-stream")
        {
            RejectWhenHasOrigin = false
        };
        var fetcher = CreateFetcher(handler);
        var context = CreateContextWithOrigin();

        var first = await fetcher.GetBytesAsync(KeyUrl, context, CancellationToken.None);
        var second = await fetcher.GetBytesAsync(KeyUrl, context, CancellationToken.None);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(2, handler.CapturedHeaders.Count);
        Assert.All(handler.CapturedHeaders, headers => Assert.Contains("Origin", headers.Keys));
    }

    /// <summary>
    /// 上下文本就没有 Origin 时，403 不触发任何重试 —— 重发同样内容只是浪费一个往返。
    /// </summary>
    [Fact]
    public async Task GetBytesAsync_ShouldNotRetry_WhenContextHasNoOrigin()
    {
        var handler = new OriginRejectingHandler(Array.Empty<byte>(), "application/octet-stream")
        {
            RejectAll = true
        };
        var fetcher = CreateFetcher(handler);

        var result = await fetcher.GetBytesAsync(
            KeyUrl,
            new RequestContext { UserAgent = "Mozilla/5.0 Test" },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(403, result.StatusCode);
        Assert.Single(handler.CapturedHeaders);
    }

    /// <summary>
    /// 回退后仍 403（例如签名过期）：把最终失败如实上报，且不把该主机记成「拒绝 Origin」。
    /// </summary>
    [Fact]
    public async Task GetBytesAsync_ShouldReportFailure_WhenRetryWithoutOriginAlsoRejected()
    {
        var handler = new OriginRejectingHandler(Array.Empty<byte>(), "application/octet-stream")
        {
            RejectAll = true
        };
        var fetcher = CreateFetcher(handler);
        var context = CreateContextWithOrigin();

        var first = await fetcher.GetBytesAsync(KeyUrl, context, CancellationToken.None);
        Assert.False(first.Success);
        Assert.Equal(2, handler.CapturedHeaders.Count); // 带 Origin 一次 + 回退一次

        // 回退没有成功，结论未被证实 —— 下一次调用仍应先尝试带 Origin
        var second = await fetcher.GetBytesAsync(KeyUrl, context, CancellationToken.None);
        Assert.False(second.Success);
        Assert.Contains("Origin", handler.CapturedHeaders[2].Keys);
    }

    /// <summary>
    /// 构造带页面 Origin 的请求上下文。
    /// </summary>
    private static RequestContext CreateContextWithOrigin()
        => new()
        {
            Referer = Referer,
            Origin = "https://page.example.com",
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Test"
        };

    private static HttpMediaFetcher CreateFetcher(HttpMessageHandler handler, FakeFileSystem? fileSystem = null)
    {
        var httpClient = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        return new HttpMediaFetcher(httpClient, fileSystem ?? new FakeFileSystem());
    }

    /// <summary>
    /// 桩处理器：记录请求头快照，按策略对带 Origin 的请求（或全部请求）返回 403。
    /// </summary>
    private sealed class OriginRejectingHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;
        private readonly string _contentType;

        public OriginRejectingHandler(byte[] payload, string contentType)
        {
            _payload = payload;
            _contentType = contentType;
        }

        /// <summary>拒绝一切请求（模拟签名过期等与 Origin 无关的 403）。</summary>
        public bool RejectAll { get; set; }

        /// <summary>仅拒绝携带 Origin 的请求（模拟 CDN 来源白名单），默认开启。</summary>
        public bool RejectWhenHasOrigin { get; set; } = true;

        /// <summary>已捕获的请求头快照，按请求顺序排列。</summary>
        public List<Dictionary<string, string>> CapturedHeaders { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in request.Headers)
            {
                headers[header.Key] = string.Join(",", header.Value);
            }

            CapturedHeaders.Add(headers);

            var rejected = RejectAll || (RejectWhenHasOrigin && headers.ContainsKey("Origin"));
            if (rejected)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent(string.Empty, Encoding.UTF8, "text/plain")
                });
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_payload)
            };
            response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(_contentType);
            return Task.FromResult(response);
        }
    }
}
