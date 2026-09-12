/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Infrastructure.Sniffing
*文件名： WebView2Sniffer
*版本号： V1.0.0.0
*唯一标识：50901b24-cdeb-4d12-ac2c-66c6879bb8b8
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 23:46:00
*描述：基于 WebView2 的视频嗅探器，融合网络响应监听与 JS 注入两条捕获链路，并按「同一个视频」归并去重。
*
*=================================================
*修改标记
*修改时间：2026/9/13 03:40:00
*修改人： yswenli
*版本号： V1.0.1.0
*描述：改用 VideoFamilyIndex 归并同一视频的多级资源；解析清单正文后登记其拥有的变体与分片目录，
*      使一个视频在列表上只占一行，且该行始终指向可完整下载的清单地址。
*
*****************************************************************************/

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using BrowserVideoGrabber.Core.Abstractions;
using BrowserVideoGrabber.Core.Downloads;
using BrowserVideoGrabber.Core.Models;
using BrowserVideoGrabber.Core.Sniffing;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BrowserVideoGrabber.Infrastructure.Sniffing;

/// <summary>
/// 基于 WebView2 的视频嗅探器。
/// </summary>
/// <remarks>
/// <para>
/// 融合两条捕获链路，互为补充：
/// <list type="bullet">
///   <item><description>
///     <b>网络响应监听</b>（<see cref="CoreWebView2.WebResourceResponseReceived"/>）：
///     能看到真实的响应头，可信度最高，还能读到 m3u8 正文从而解析出清晰度与分片归属。
///   </description></item>
///   <item><description>
///     <b>JS 注入回传</b>（<see cref="CoreWebView2.WebMessageReceived"/>）：
///     能捕获到尚未发出请求、或响应被浏览器内部处理而未触发响应的地址。
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <b>一个视频只呈现一行</b>：归并逻辑全部委托给 <see cref="VideoFamilyIndex"/>。
/// 关键在于读完清单正文后立刻调用 <see cref="M3u8OwnershipCollector"/> 把「这份清单拥有哪些变体与分片目录」
/// 登记进索引 —— 于是同一个视频的主清单、各档变体、上千个分片最终只会留下<b>唯一一行</b>，
/// 且该行指向清单地址而不是某个分片。若不做这件事，用户很容易选中一个 <c>.ts</c> 分片，
/// 下载出来的「视频」只有几秒钟。
/// </para>
/// </remarks>
public sealed class WebView2Sniffer : IVideoSniffer, IDisposable
{
    private readonly WebView2 _webView;
    private readonly VideoFamilyIndex _index;

    /// <summary>可选的取数器，仅用于探测主清单的变体时长；为 null 时跳过探测。</summary>
    private readonly IMediaFetcher? _mediaFetcher;

    /// <summary>可选的请求上下文提供者，为变体探测提供 Origin / Referer 等来源头。</summary>
    private readonly IRequestContextProvider? _contextProvider;

    /// <summary>
    /// 变体清单时长探测的结果缓存。
    /// </summary>
    /// <remarks>
    /// 播放期间主清单会被反复请求（码率自适应、切片滚动），若每次都去探测变体，
    /// 等于给 CDN 平白增加一倍请求量，还可能触到限流。值为 null 表示「探测过但失败了」，
    /// 同样入缓存以避免对已知拿不到的地址反复重试。
    /// </remarks>
    private readonly ConcurrentDictionary<string, double?> _probedDurations = new(StringComparer.Ordinal);

    private bool _subscribed;
    private bool _disposed;

    /// <summary>已进入回调的响应计数，仅用于诊断日志的存活采样。</summary>
    private int _responseCount;

    /// <summary>
    /// 初始化嗅探器。
    /// </summary>
    /// <param name="webView">被监听的 WebView2 控件。</param>
    /// <param name="sharedIndex">
    /// 共享的家族归并索引。传入时多个嗅探器（多标签页）会共用同一份归并结果，
    /// 于是同一个视频无论在哪一页被捕获，最终都只呈现一行；为 null 时退回自带的独立索引。
    /// </param>
    /// <param name="maxTrackedItems">
    /// 家族数量上限，用于约束长时间浏览时的内存占用。仅在使用独立索引（<paramref name="sharedIndex"/> 为 null）时生效。
    /// </param>
    /// <param name="mediaFetcher">
    /// 可选的取数器，用于主清单的变体时长探测。为 null 时跳过探测，主清单条目将不带时长，
    /// 界面显示「-」。单标签与单元测试场景可保持 null 以省去网络往返。
    /// </param>
    /// <param name="contextProvider">
    /// 可选的请求上下文提供者，为变体探测补齐 Origin / Referer 等反爬请求头。
    /// 为 null 但 <paramref name="mediaFetcher"/> 非 null 时，内部会基于本嗅探器绑定的
    /// <c>WebView2</c> 惰性构造一个默认提供者（沿用浏览器 UA、不套用户自定义 UA）。
    /// </param>
    public WebView2Sniffer(
        WebView2 webView,
        VideoFamilyIndex? sharedIndex = null,
        int maxTrackedItems = VideoFamilyIndex.DefaultCapacity,
        IMediaFetcher? mediaFetcher = null,
        IRequestContextProvider? contextProvider = null)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _index = sharedIndex ?? new VideoFamilyIndex(maxTrackedItems);
        _mediaFetcher = mediaFetcher;
        _contextProvider = contextProvider;

        // WebView2 是异步初始化的：构造时可能尚未就绪，因此必须等初始化完成后再挂监听
        _webView.CoreWebView2InitializationCompleted += OnCoreWebViewInitializationCompleted;
    }

    /// <inheritdoc />
    public event EventHandler<SniffedVideo>? VideoDetected;

    /// <inheritdoc />
    public event EventHandler<SniffedVideo>? VideoRetracted;

    /// <summary>嗅探开关。关闭后仍会保留已捕获的结果，只是不再上报新资源。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>家族数量上限。</summary>
    public int MaxTrackedItems => _index.Capacity;

    /// <summary>已归并出的视频数量（即列表上应有的行数）。</summary>
    public int CapturedCount => _index.FamilyCount;

    /// <inheritdoc />
    public void Start()
    {
        var coreWebView = _webView.CoreWebView2;

        SniffDiagnostics.Write($"sniffer start; coreReady={coreWebView is not null}; Enabled={Enabled}");

        // 控件可能已经初始化完毕（事件不会再触发），因此这里也要主动挂一次
        if (coreWebView is not null)
        {
            Subscribe(coreWebView);
        }
    }

    /// <inheritdoc />
    public void Stop() => Unsubscribe();

    /// <inheritdoc />
    public void Clear() => _index.Clear();

    /// <summary>
    /// 释放嗅探器，解除全部监听。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _webView.CoreWebView2InitializationCompleted -= OnCoreWebViewInitializationCompleted;
        Unsubscribe();
    }

    /// <summary>
    /// WebView2 初始化完成回调：注入脚本并挂载监听。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnCoreWebViewInitializationCompleted(
        object? sender,
        CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            return;
        }

        var coreWebView = _webView.CoreWebView2;
        if (coreWebView is null)
        {
            return;
        }

        // 先注入脚本再挂监听：脚本必须早于页面首个请求生效，否则会漏抓首轮地址。
        // 注入动作在 Subscribe 里还会兜底一次（本回调在晚挂场景下可能根本不触发）
        _ = InjectHookSafelyAsync(coreWebView);
        Subscribe(coreWebView);
    }

    /// <summary>
    /// 挂载两条捕获链路，并确保 JS 注入脚本已注册。
    /// </summary>
    /// <param name="coreWebView">WebView2 核心对象。</param>
    /// <remarks>
    /// 注入动作必须在这里补一次：多标签架构下嗅探器总是在内核就绪<b>之后</b>才被
    /// 构造（<c>TabAttached</c> 晚于 <c>EnsureCoreAsync</c>），此时
    /// <see cref="CoreWebView2InitializationCompleted"/> 早已触发过而不会再等我们；
    /// 若只在初始化回调里注入，JS 捕获链路（XHR / fetch / 媒体元素）将永远不生效。
    /// </remarks>
    private void Subscribe(CoreWebView2 coreWebView)
    {
        if (_subscribed)
        {
            return;
        }

        coreWebView.WebResourceResponseReceived += OnWebResourceResponseReceived;
        coreWebView.WebMessageReceived += OnWebMessageReceived;
        _subscribed = true;

        SniffDiagnostics.Write($"sniffer subscribed; Enabled={Enabled}");

        // 注入对后续新建的文档生效；当前已加载的文档由 SniffPageNowAsync 兜底补挂
        _ = InjectHookSafelyAsync(coreWebView);
    }

    /// <summary>
    /// 立即扫描当前页面：把拦截 Hook 补挂到已加载的文档，并上报现有媒体元素的地址。
    /// </summary>
    /// <returns>扫描结束后兑现的任务。</returns>
    /// <remarks>
    /// 供「下载视频」右键菜单调用。上报同样经过家族索引去重 ——
    /// 列表中已有的地址会被判定为 <see cref="VideoAdmissionKind.Ignored"/> 而不再处理。
    /// </remarks>
    public async Task SniffPageNowAsync()
    {
        var coreWebView = _webView.CoreWebView2;

        if (_disposed || coreWebView is null)
        {
            return;
        }

        // 兜底订阅：调用可能发生在任意时刻，确保两条捕获链活着再扫描
        Subscribe(coreWebView);

        try
        {
            await JsHookInjector.ScanPageAsync(coreWebView).ConfigureAwait(true);
        }
        catch (InvalidOperationException)
        {
            // 页面正在导航/控件已释放：扫描失败不致命
        }
        catch (COMException)
        {
            // 内核调用返回失败 HRESULT 时忽略
        }
    }

    /// <summary>
    /// 注入嗅探脚本并吞掉一切失败。
    /// </summary>
    /// <param name="coreWebView">WebView2 核心对象。</param>
    /// <returns>表示异步注入的任务。</returns>
    private static async Task InjectHookSafelyAsync(CoreWebView2 coreWebView)
    {
        try
        {
            await JsHookInjector.InjectAsync(coreWebView).ConfigureAwait(true);
        }
        catch (InvalidOperationException)
        {
            // 控件已释放或内核不可用：注入失败不影响网络监听链路
        }
        catch (COMException)
        {
            // 同上，忽略 COM 层失败
        }
    }

    /// <summary>
    /// 解除两条捕获链路。
    /// </summary>
    private void Unsubscribe()
    {
        var coreWebView = _webView.CoreWebView2;
        if (coreWebView is null || !_subscribed)
        {
            return;
        }

        try
        {
            coreWebView.WebResourceResponseReceived -= OnWebResourceResponseReceived;
            coreWebView.WebMessageReceived -= OnWebMessageReceived;
        }
        catch (InvalidOperationException)
        {
            // 控件已释放
        }

        _subscribed = false;
    }

    /// <summary>
    /// 网络响应回调。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    /// <remarks>
    /// 采用 <c>async void</c> 是事件处理器的必然要求，因此整个方法体被
    /// <c>try/catch</c> 完全包裹，确保任何异常都不会逃逸到 WebView2 的消息循环而终止进程。
    /// </remarks>
    private async void OnWebResourceResponseReceived(
        object? sender,
        CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        try
        {
            if (!Enabled)
            {
                return;
            }

            var url = e.Request?.Uri;
            if (!NetworkSniffRules.ShouldInspect(url))
            {
                return;
            }

            // 存活采样：确认响应事件确实在触发（若日志里连样本都没有，说明断点出在订阅环节）
            var responseIndex = Interlocked.Increment(ref _responseCount);
            if (responseIndex == 1 || responseIndex % 200 == 0)
            {
                SniffDiagnostics.Write($"resp pipeline alive #{responseIndex}; url={url}");
            }

            var isM3u8Hint = url is not null
                && url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase);

            // 任何一处 WebView2 COM 对象访问都可能在响应被内核回收后抛异常
            // （如访问 Response.Headers / Response.Content），必须逐点兜底 ——
            // 一旦某个调用点抛异常，就会跳过后续所有逻辑（包括 Publish），
            // 导致开发者工具能看到地址但嗅探列表始终为空。

            string? contentType = null;
            try
            {
                contentType = NetworkSniffRules.ReadContentType(e.Response?.Headers);
            }
            catch (Exception ex)
            {
                SniffDiagnostics.Write($"COM at ReadContentType; type={ex.GetType().Name}; msg={ex.Message}");
            }

            if (!NetworkSniffRules.TryIdentify(url, contentType, out var format))
            {
                if (isM3u8Hint)
                {
                    SniffDiagnostics.Write($"resp NOT identified despite m3u8 hint; url={url}; ct={contentType}");
                }

                return;
            }

            if (format == VideoFormat.M3u8)
            {
                SniffDiagnostics.Write($"resp identified m3u8; url={url}; ct={contentType}");
            }

            string? resolution = null;
            long? bandwidth = null;
            double? duration = null;
            M3u8Ownership? ownership = null;

            // 只有清单的正文里才有「谁从属于谁」的信息，读一次即可同时补全清晰度与归属关系。
            // 整个分支独立兜底：解析正文、收集归属、探测变体时长都属于「锦上添花」的增强，
            // 任何一步失败都不能连累清单本身 —— 用户宁可看到一条缺清晰度/时长的清单，
            // 也绝不能眼睁睁看着它在列表里整条消失。
            // 清单正文解析、归属登记、时长探测都属于「锦上添花」的增强：
            // 任何一步抛异常都只能影响附加元数据（清晰度、时长），
            // 绝不能让清单本身在列表里整条消失 —— 否则开发者工具能看到清单但嗅探列表就是空的。
            if (format == VideoFormat.M3u8 && e.Response is not null)
            {
                try
                {
                    var playlist = await TryReadPlaylistAsync(e.Response, url!).ConfigureAwait(true);

                    if (playlist is not null)
                    {
                        ownership = M3u8OwnershipCollector.Collect(playlist, url);

                        var best = playlist.BestVariant;
                        if (best is not null)
                        {
                            resolution = best.Resolution;
                            bandwidth = best.Bandwidth > 0 ? best.Bandwidth : null;
                        }

                        duration = playlist.TotalDuration > TimeSpan.Zero
                            ? playlist.TotalDuration.TotalSeconds
                            : await TryProbeVariantDurationAsync(best?.Uri).ConfigureAwait(true);
                    }
                }
                catch (Exception metadataFailure)
                {
                    // 清单正文读取失败（响应对象被内核回收、COM 无效索引、网络瞬断等）：
                    // 退化为不带元数据上报清单，保证清单条目本身一定能进入 Publish 流程
                    SniffDiagnostics.Write(
                        $"m3u8 metadata failure; type={metadataFailure.GetType().Name}; msg={metadataFailure.Message}; url={url}");
                    ownership = null;
                    resolution = null;
                    bandwidth = null;
                    duration = null;
                }
            }

            long? contentLength = null;
            try
            {
                contentLength = ReadContentLength(e.Response?.Headers);
            }
            catch (Exception ex)
            {
                SniffDiagnostics.Write($"COM at ReadContentLength; type={ex.GetType().Name}; msg={ex.Message}");
            }

            Publish(
                new SniffedVideo
                {
                    Url = url!,
                    NormalizedUrl = VideoUrlMatcher.Normalize(url),
                    Format = format,
                    ContentType = contentType,
                    Resolution = resolution,
                    Bandwidth = bandwidth,
                    DurationSeconds = duration,
                    ContentLength = contentLength,
                    Source = "network"
                },
                ownership);
        }
        catch (Exception failure)
        {
            // 嗅探失败绝不能影响页面正常播放
            SniffDiagnostics.Write($"resp callback exception {failure.GetType().Name}: {failure.Message}");
        }
    }

    /// <summary>
    /// JS 注入脚本回传消息回调。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            if (!Enabled)
            {
                return;
            }

            var json = e.WebMessageAsJson;
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (!root.TryGetProperty("type", out var typeElement)
                || typeElement.GetString() != "bvgb-sniff")
            {
                return;
            }

            if (!root.TryGetProperty("url", out var urlElement))
            {
                return;
            }

            var url = urlElement.GetString();
            if (!NetworkSniffRules.ShouldInspect(url))
            {
                return;
            }

            if (!NetworkSniffRules.TryIdentify(url, null, out var format))
            {
                return;
            }

            var source = root.TryGetProperty("source", out var sourceElement)
                ? sourceElement.GetString() ?? "jshook"
                : "jshook";

            if (format == VideoFormat.M3u8)
            {
                SniffDiagnostics.Write($"jshook reported m3u8; url={url}; source={source}");
            }

            // JS 链路拿不到响应头与正文，因此不带任何归属信息与清晰度；
            // 若网络链路稍后捕获到同一地址，家族索引会以「信息更丰富」为由原地补齐分辨率
            Publish(
                new SniffedVideo
                {
                    Url = url!,
                    NormalizedUrl = VideoUrlMatcher.Normalize(url),
                    Format = format,
                    Source = source,
                    PageTitle = CurrentPageTitle()
                },
                ownership: null);
        }
        catch (JsonException)
        {
            // 页面可能回传了非本工具格式的消息，忽略
        }
        catch (InvalidOperationException)
        {
        }
    }

    /// <summary>
    /// 读取并解析清单正文。
    /// </summary>
    /// <param name="response">WebView2 响应对象。</param>
    /// <param name="url">清单地址。</param>
    /// <returns>解析结果；读取失败或内容无效时返回 null。</returns>
    /// <remarks>
    /// 正文可能未被缓存或响应对象已失效，此时退化为「不带清晰度与归属信息上报」，
    /// 绝不能因此让嗅探中断。
    /// </remarks>
    private static async Task<M3u8Playlist?> TryReadPlaylistAsync(CoreWebView2WebResourceResponseView response, string url)
    {
        try
        {
            using var stream = await response.GetContentAsync().ConfigureAwait(true);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var content = await reader.ReadToEndAsync().ConfigureAwait(true);

            var baseUri = Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed : null;
            var playlist = M3u8Parser.Parse(content, baseUri);

            return playlist.IsValid ? playlist : null;
        }
        catch
        {
            // 响应对象可能被内核回收（COM 无效索引）、网络瞬断、流已关闭 —— 统统兜底吞掉；
            // 返回 null 表示「清单正文拿不到，但这不影响上层退化为无元数据上报」。
            return null;
        }
    }

    /// <summary>
    /// 探测主清单某档变体的总时长。
    /// </summary>
    /// <param name="variantUri">变体清单地址；为 null 时直接返回 null。</param>
    /// <returns>时长（秒）；无法取得时返回 null。</returns>
    /// <remarks>
    /// <para>
    /// 主清单本身不含分片（<c>TotalDuration</c> 恒为 0），要得到时长必须再取一次变体清单。
    /// 但播放期间主清单会被反复请求，若每次都去探测，等于给 CDN 平白增加一倍请求量，
    /// 还可能触到限流。因此结果（含「探测失败」的 null）会缓存，
    /// 同一变体地址只探测一次，后续请求直接复用结论。
    /// </para>
    /// <para>
    /// 探测属于「尽力而为」的增强：拿不到就返回 null，界面显示「-」，
    /// 绝不能因为一次变体探测失败而影响主清单条目本身的呈现。
    /// </para>
    /// </remarks>
    private async Task<double?> TryProbeVariantDurationAsync(string? variantUri)
    {
        if (_mediaFetcher is null || string.IsNullOrWhiteSpace(variantUri))
        {
            return null;
        }

        // 命中缓存（含「探测过但失败」的 null）直接返回，避免对同一地址反复请求
        if (_probedDurations.TryGetValue(variantUri, out var cached))
        {
            return cached;
        }

        double? duration = null;
        try
        {
            var provider = _contextProvider ?? new WebView2RequestContextProvider(_webView);
            var context = await provider.CreateAsync(variantUri).ConfigureAwait(true);
            var result = await _mediaFetcher.GetStringAsync(variantUri, context, CancellationToken.None).ConfigureAwait(true);

            if (result.Success && !string.IsNullOrWhiteSpace(result.Text))
            {
                var baseUri = Uri.TryCreate(variantUri, UriKind.Absolute, out var parsed) ? parsed : null;
                var playlist = M3u8Parser.Parse(result.Text, baseUri);

                if (playlist.IsValid && playlist.TotalDuration > TimeSpan.Zero)
                {
                    duration = playlist.TotalDuration.TotalSeconds;
                }
            }
        }
        catch
        {
            // 探测失败不致命：缓存 null，避免对已知拿不到的地址反复重试。
            // 刻意不用异常过滤器兜住一切 —— 变体时长只是列表上的一列，
            // 无论哪里出了什么错，都不能让异常沿着响应回调逃逸而吞掉清单上报。
        }

        _probedDurations[variantUri] = duration;
        return duration;
    }

    /// <summary>
    /// 从 WebView2 响应头读取 <c>Content-Length</c>。
    /// </summary>
    /// <param name="headers">响应头集合，可为 null。</param>
    /// <returns>字节数；头缺失或无法解析时返回 null。</returns>
    /// <remarks>
    /// 只有单文件资源（如 MP4）的 <c>Content-Length</c> 才代表视频体积；
    /// m3u8 清单的该值只是清单文本大小，会被 <see cref="SniffedVideo.EstimatedBytes"/> 正确地
    /// 忽略（m3u8 走「时长 × 码率」的估算路径而非直接采用本值）。
    /// </remarks>
    private static long? ReadContentLength(CoreWebView2HttpResponseHeaders? headers)
    {
        if (headers is null)
        {
            return null;
        }

        try
        {
            var value = headers.GetHeader("Content-Length");
            return !string.IsNullOrWhiteSpace(value) && long.TryParse(value, out var length)
                ? length
                : null;
        }
        catch (ArgumentException)
        {
            // 头名不合法或不存在
            return null;
        }
    }

    /// <summary>
    /// 提交一个候选资源：归并去重后上报，并处理因归属关系变化而需要撤回的旧条目。
    /// </summary>
    /// <param name="video">候选资源。</param>
    /// <param name="ownership">该资源（若为清单）所拥有的下级资源，可为 null。</param>
    private void Publish(SniffedVideo video, M3u8Ownership? ownership)
    {
        var admission = _index.Consider(video);

        if (video.Format == VideoFormat.M3u8)
        {
            SniffDiagnostics.Write(
                $"publish m3u8; admission={admission.Kind}; familyKey={admission.FamilyKey}; url={video.Url}");
        }

        if (ownership is not null)
        {
            // 即使本条目被忽略（例如是主清单下的一档变体），它声明的分片目录仍要登记到所属家族上，
            // 否则「分片与清单不在同一目录」的站点会在列表里多出一行分片
            var superseded = _index.RegisterOwned(
                admission.FamilyKey,
                ownership.Value.OwnedUrls,
                ownership.Value.OwnedDirectories);

            foreach (var retracted in superseded)
            {
                Raise(VideoRetracted, retracted);
            }
        }

        if (admission.Kind != VideoAdmissionKind.Ignored)
        {
            Raise(VideoDetected, admission.Video);
        }
    }

    /// <summary>
    /// 读取当前页面标题，无效时返回 null。
    /// </summary>
    /// <returns>页面标题；未就绪或为空时返回 null。</returns>
    private string? CurrentPageTitle()
    {
        var title = _webView.CoreWebView2?.DocumentTitle;
        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    /// <summary>
    /// 安全地触发事件。
    /// </summary>
    /// <param name="handler">事件处理器。</param>
    /// <param name="video">条目。</param>
    private void Raise(EventHandler<SniffedVideo>? handler, SniffedVideo video)
    {
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(this, video);
        }
        catch
        {
            // 订阅方异常不得影响嗅探流程
        }
    }
}
