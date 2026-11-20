/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Infrastructure.Downloads
*文件名： HttpMediaFetcher
*版本号： V1.0.0.0
*唯一标识：c1a7b3e9-2f4d-4a81-9b2c-7e0d6f5a1c43
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 03:50:00
*描述：IMediaFetcher 的 HttpClient 实现，把「按地址取文本/字节/落盘」收口到真实网络调用。
*
*=================================================
*修改标记
*修改时间：2026/9/13 03:50:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BrowserVideoGrabber.Core.Abstractions;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Infrastructure.Downloads;

/// <summary>
/// 基于 <see cref="HttpClient"/> 的媒体抓取实现。
/// </summary>
/// <remarks>
/// <para>
/// 这是 <see cref="IMediaFetcher"/> 在真实网络上的唯一落地。与之相对，
/// 单元测试使用内存假实现（<c>FakeMediaFetcher</c>）精确构造「第 N 个分片返回占位图」等场景，
/// 二者遵循同一份契约，因此 C# 取片编排逻辑完全不依赖真实网络即可被覆盖。
/// </para>
/// <para>
/// <b>三条铁律</b>：
/// 1）业务失败（403/404/超时/令牌失效）落到 <see cref="MediaFetchResult.Success"/> = false，绝不抛异常；
/// 2）只有调用方取消才允许向外抛 <see cref="OperationCanceledException"/>；
/// 3）<c>Origin</c> 头按站点策略自适应回退 —— 默认携带（playergo 系 CDN 依赖该头返回真实内容），
/// 被 403 拒绝时回退为不带 Origin 重试一次并按主机记住结论（qnlycc 系 CDN 对该头直接 403）。
/// 详见 <see cref="SendWithOriginFallbackAsync"/>。
/// </para>
/// <para>
/// 注入的 <see cref="HttpClient"/> 须关闭自动解压（分片拼接依赖字节偏移）、总超时设为无限（超时改由
/// 上层空闲超时承担）。这一约定与 <see cref="HttpDownloadHandler"/> 共用，由 <c>AppHost</c> 统一保证。
/// </para>
/// </remarks>
public sealed class HttpMediaFetcher : IMediaFetcher
{
    /// <summary>流式复制分片时的缓冲区大小（80 KiB）。</summary>
    private const int CopyBufferBytes = 80 * 1024;

    private readonly HttpClient _httpClient;
    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// 已证实「拒绝页面 Origin」的主机集合。命中后该主机的后续请求直接跳过 Origin，
    /// 避免每个分片都先吃一次注定 403 的往返。
    /// </summary>
    private readonly ConcurrentDictionary<string, bool> _originRejectedByHost = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 初始化抓取器。
    /// </summary>
    /// <param name="httpClient">HTTP 客户端。须关闭自动解压、超时设为无限。</param>
    /// <param name="fileSystem">文件系统抽象，用于「落盘」类抓取创建目录与写入。</param>
    public HttpMediaFetcher(HttpClient httpClient, IFileSystem fileSystem)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    /// <inheritdoc />
    public async Task<MediaFetchResult> GetStringAsync(
        string url,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        if (!TryCreateUri(url, out var uri))
        {
            return MediaFetchResult.Fail($"无法识别的播放列表地址：{url}", isRetryable: false);
        }

        try
        {
            using var response = await SendWithOriginFallbackAsync(
                uri, context, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return MediaFetchResult.Fail(DescribeStatus(response.StatusCode), IsRetryableStatus(response.StatusCode), (int)response.StatusCode);
            }

            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            return MediaFetchResult.Ok((int)response.StatusCode, contentType, Encoding.UTF8.GetByteCount(text), text: text);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
        {
            return MediaFetchResult.Fail($"获取播放列表失败：{exception.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<MediaFetchResult> GetBytesAsync(
        string url,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        if (!TryCreateUri(url, out var uri))
        {
            return MediaFetchResult.Fail($"无法识别的地址：{url}", isRetryable: false);
        }

        try
        {
            using var response = await SendWithOriginFallbackAsync(
                uri, context, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return MediaFetchResult.Fail(DescribeStatus(response.StatusCode), IsRetryableStatus(response.StatusCode), (int)response.StatusCode);
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return MediaFetchResult.Ok((int)response.StatusCode, response.Content.Headers.ContentType?.MediaType, bytes.Length, bytes: bytes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
        {
            return MediaFetchResult.Fail($"获取资源失败：{exception.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<MediaFetchResult> GetToFileAsync(
        string url,
        RequestContext context,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (!TryCreateUri(url, out var uri))
        {
            return MediaFetchResult.Fail($"无法识别的分片地址：{url}", isRetryable: false);
        }

        try
        {
            // 响应头读毕即返回，不让连接一直占用到整片下完；分片体积可达数 MB，流式写盘更省内存
            using var response = await SendWithOriginFallbackAsync(
                uri, context, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return MediaFetchResult.Fail(DescribeStatus(response.StatusCode), IsRetryableStatus(response.StatusCode), (int)response.StatusCode);
            }

            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                _fileSystem.CreateDirectory(directory);
            }

            long written;
            using (var destination = _fileSystem.OpenWrite(destinationPath, false))
            {
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var buffer = new byte[CopyBufferBytes];
                while (true)
                {
                    var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        break;
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                written = destination.Length;
            }

            return MediaFetchResult.Ok((int)response.StatusCode, response.Content.Headers.ContentType?.MediaType, written);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
        {
            return MediaFetchResult.Fail($"分片下载失败：{exception.Message}");
        }
    }

    /// <summary>
    /// 把地址解析为绝对 URI。
    /// </summary>
    /// <param name="url">原始地址。</param>
    /// <param name="uri">解析结果。</param>
    /// <returns>解析成功返回 true。</returns>
    private static bool TryCreateUri(string url, [NotNullWhen(true)] out Uri? uri)
        => Uri.TryCreate(url, UriKind.Absolute, out uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// 发送 GET 请求，遇「带 Origin 被 403 拒绝」时回退为不带 Origin 重试一次。
    /// </summary>
    /// <param name="uri">目标地址。</param>
    /// <param name="context">请求上下文。</param>
    /// <param name="completionOption">响应读取方式。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>最终响应（成功或回退后的失败），由调用方负责释放。</returns>
    /// <remarks>
    /// <para>
    /// 两类站点策略互相矛盾：playergo 系 CDN 依赖页面 <c>Origin</c> 返回真实内容，
    /// qnlycc 系 CDN 则对该头直接 403 —— 因此既不能一律携带也不能一律剔除，
    /// 只能以响应为准做一次自适应回退。
    /// </para>
    /// <para>
    /// 回退成功才把主机记入「拒绝 Origin」名单：回退后仍失败（如签名过期）时结论未被证实，
    /// 下次仍应先尝试带 Origin，否则会把正常的 403 误学成来源策略问题。
    /// </para>
    /// </remarks>
    private async Task<HttpResponseMessage> SendWithOriginFallbackAsync(
        Uri uri,
        RequestContext context,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        var hasOrigin = !string.IsNullOrWhiteSpace(context.Origin);
        var skipOrigin = hasOrigin && _originRejectedByHost.ContainsKey(uri.Host);

        var response = await SendOnceAsync(uri, context, includeOrigin: !skipOrigin, completionOption, cancellationToken)
            .ConfigureAwait(false);

        // 已跳过 Origin 还被拒、上下文本就无 Origin、或并非 403 —— 都没有回退依据
        if (skipOrigin || !hasOrigin || response.StatusCode != HttpStatusCode.Forbidden)
        {
            return response;
        }

        response.Dispose();
        response = await SendOnceAsync(uri, context, includeOrigin: false, completionOption, cancellationToken)
            .ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            _originRejectedByHost[uri.Host] = true;
        }

        return response;
    }

    /// <summary>
    /// 单次 GET 发送。
    /// </summary>
    /// <param name="uri">目标地址。</param>
    /// <param name="context">请求上下文。</param>
    /// <param name="includeOrigin">是否携带 Origin 头。</param>
    /// <param name="completionOption">响应读取方式。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>原始响应。</returns>
    private async Task<HttpResponseMessage> SendOnceAsync(
        Uri uri,
        RequestContext context,
        bool includeOrigin,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        HttpRequestHeaders.Apply(request, context, includeOrigin);
        return await _httpClient.SendAsync(request, completionOption, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 把 HTTP 状态码转换为面向用户的中文失败描述。
    /// </summary>
    /// <param name="statusCode">状态码。</param>
    /// <returns>中文描述。</returns>
    private static string DescribeStatus(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"目标服务器拒绝访问（HTTP {code}）。请确认已在浏览器中登录该站点后重新嗅探。",
            HttpStatusCode.NotFound or HttpStatusCode.Gone => $"资源地址已失效（HTTP {code}），通常为动态签名过期。请在页面重新播放后再次嗅探。",
            _ => code >= 500
                ? $"服务器暂时不可用（HTTP {code}），请稍后重试。"
                : $"服务器返回异常状态（HTTP {code}）。"
        };
    }

    /// <summary>
    /// 依据状态码判断失败是否值得重试。
    /// </summary>
    /// <param name="statusCode">状态码。</param>
    /// <returns>401/403（鉴权缺失）与 404（地址失效）不可重试，其余可重试。</returns>
    private static bool IsRetryableStatus(HttpStatusCode statusCode)
        => statusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Gone);
}
