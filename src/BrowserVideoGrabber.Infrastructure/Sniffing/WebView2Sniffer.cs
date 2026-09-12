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

    private bool _subscribed;
    private bool _disposed;

    /// <summary>
    /// 初始化嗅探器。
    /// </summary>
    /// <param name="webView">被监听的 WebView2 控件。</param>
    /// <param name="maxTrackedItems">家族数量上限，用于约束长时间浏览时的内存占用。</param>
    public WebView2Sniffer(WebView2 webView, int maxTrackedItems = VideoFamilyIndex.DefaultCapacity)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _index = new VideoFamilyIndex(maxTrackedItems);

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

        // 先注入脚本再挂监听：脚本必须早于页面首个请求生效，否则会漏抓首轮地址
        _ = JsHookInjector.InjectAsync(coreWebView);
        Subscribe(coreWebView);
    }

    /// <summary>
    /// 挂载两条捕获链路。
    /// </summary>
    /// <param name="coreWebView">WebView2 核心对象。</param>
    private void Subscribe(CoreWebView2 coreWebView)
    {
        if (_subscribed)
        {
            return;
        }

        coreWebView.WebResourceResponseReceived += OnWebResourceResponseReceived;
        coreWebView.WebMessageReceived += OnWebMessageReceived;
        _subscribed = true;
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

            var contentType = NetworkSniffRules.ReadContentType(e.Response?.Headers);
            if (!NetworkSniffRules.TryIdentify(url, contentType, out var format))
            {
                return;
            }

            string? resolution = null;
            long? bandwidth = null;
            M3u8Ownership? ownership = null;

            // 只有清单的正文里才有「谁从属于谁」的信息，读一次即可同时补全清晰度与归属关系
            if (format == VideoFormat.M3u8 && e.Response is not null)
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
                }
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
                    Source = "network"
                },
                ownership);
        }
        catch
        {
            // 嗅探失败绝不能影响页面正常播放
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

            // JS 链路拿不到响应头与正文，因此不带任何归属信息与清晰度；
            // 若网络链路稍后捕获到同一地址，家族索引会以「信息更丰富」为由原地补齐分辨率
            Publish(
                new SniffedVideo
                {
                    Url = url!,
                    NormalizedUrl = VideoUrlMatcher.Normalize(url),
                    Format = format,
                    Source = source
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
        catch (Exception exception) when (exception is InvalidOperationException or IOException or ObjectDisposedException or HttpRequestException)
        {
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
