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
*描述：基于 WebView2 的视频嗅探器，融合网络响应监听与 JS 注入两条捕获链路。
*
*=================================================
*修改标记
*修改时间：2026/9/12 23:46:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
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
///     能看到真实的响应头，可信度最高，还能读到 m3u8 正文从而解析出分辨率。
///   </description></item>
///   <item><description>
///     <b>JS 注入回传</b>（<see cref="CoreWebView2.WebMessageReceived"/>）：
///     能捕获到尚未发出请求、或响应被浏览器内部处理而未触发响应的地址。
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <b>分片折叠</b>：一个两小时的视频可能有上千个 <c>.ts</c> 分片，
/// 若逐条上报会把列表彻底刷屏。因此对分片类格式按「所在目录 + 格式」折叠，
/// 每个分片目录最多只呈现一条记录 —— 用户真正需要的是播放列表而非单个分片。
/// </para>
/// </remarks>
public sealed class WebView2Sniffer : IVideoSniffer, IDisposable
{
    private readonly WebView2 _webView;
    private readonly HashSet<string> _fingerprints = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private bool _subscribed;
    private bool _disposed;

    /// <summary>
    /// 初始化嗅探器。
    /// </summary>
    /// <param name="webView">被监听的 WebView2 控件。</param>
    public WebView2Sniffer(WebView2 webView)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));

        // WebView2 是异步初始化的：构造时可能尚未就绪，因此必须等初始化完成后再挂监听
        _webView.CoreWebView2InitializationCompleted += OnCoreWebViewInitializationCompleted;
    }

    /// <inheritdoc />
    public event EventHandler<SniffedVideo>? VideoDetected;

    /// <summary>嗅探开关。关闭后仍会保留已捕获的结果，只是不再上报新资源。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>去重指纹数量上限，防止长时间浏览导致内存无界增长。</summary>
    public int MaxTrackedItems { get; set; } = 800;

    /// <summary>已捕获的资源数量。</summary>
    public int CapturedCount
    {
        get
        {
            lock (_gate)
            {
                return _fingerprints.Count;
            }
        }
    }

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

    /// <summary>
    /// 清空去重记录。界面点击「清空」时调用，使同一资源可以再次被上报。
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _fingerprints.Clear();
        }
    }

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

            // 主播放列表的正文里才有清晰度信息，读一次正文即可补全展示
            if (format == VideoFormat.M3u8 && e.Response is not null)
            {
                try
                {
                    using var stream = await e.Response.GetContentAsync();
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    var content = await reader.ReadToEndAsync();

                    var baseUri = Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed : null;
                    var playlist = M3u8Parser.Parse(content, baseUri);

                    if (playlist.Variants.Count > 0)
                    {
                        resolution = playlist.Variants[0].Resolution;
                        bandwidth = playlist.Variants[0].Bandwidth;
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or IOException or ObjectDisposedException)
                {
                    // 正文可能未被缓存或响应对象已失效，读不到就退化为不带清晰度上报
                }
            }

            Report(new SniffedVideo
            {
                Url = url!,
                NormalizedUrl = VideoUrlMatcher.Normalize(url),
                Format = format,
                ContentType = contentType,
                Resolution = resolution,
                Bandwidth = bandwidth,
                Source = "network"
            });
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

            Report(new SniffedVideo
            {
                Url = url!,
                NormalizedUrl = VideoUrlMatcher.Normalize(url),
                Format = format,
                Source = source
            });
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
    /// 上报嗅探结果（带去重）。
    /// </summary>
    /// <param name="video">嗅探结果。</param>
    private void Report(SniffedVideo video)
    {
        var fingerprint = BuildFingerprint(video.Url, video.Format);
        if (string.IsNullOrEmpty(fingerprint))
        {
            return;
        }

        lock (_gate)
        {
            if (_fingerprints.Count >= MaxTrackedItems)
            {
                return;
            }

            if (!_fingerprints.Add(fingerprint))
            {
                return;
            }
        }

        var handler = VideoDetected;
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

    /// <summary>
    /// 生成去重指纹。
    /// </summary>
    /// <param name="url">资源地址。</param>
    /// <param name="format">资源格式。</param>
    /// <returns>去重指纹。</returns>
    private static string BuildFingerprint(string url, VideoFormat format)
    {
        var normalized = VideoUrlMatcher.Normalize(url);
        if (string.IsNullOrEmpty(normalized))
        {
            return string.Empty;
        }

        if (format is VideoFormat.Ts or VideoFormat.M4s)
        {
            // 分片按目录折叠：同一目录下成百上千个分片只保留首条
            var lastSlashIndex = normalized.LastIndexOf('/');
            var directory = lastSlashIndex > 0 ? normalized[..lastSlashIndex] : normalized;
            return directory.ToLowerInvariant() + "|" + format;
        }

        return VideoUrlMatcher.Fingerprint(url);
    }
}
