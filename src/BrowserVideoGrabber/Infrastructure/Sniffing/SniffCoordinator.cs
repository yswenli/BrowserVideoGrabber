/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Infrastructure.Sniffing
*文件名： SniffCoordinator
*版本号： V1.0.0.0
*唯一标识：47ff6e07-cdfa-43f3-a08f-88e2136632f3
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 01:50:00
*描述：多标签页嗅探协调器，把每个标签的嗅探器聚合成单一嗅探器表面。
*
*=================================================
*修改标记
*修改时间：2026/9/13 01:50:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Abstractions;
using BrowserVideoGrabber.Core.Models;
using BrowserVideoGrabber.Core.Sniffing;
using Microsoft.Web.WebView2.WinForms;

namespace BrowserVideoGrabber.Infrastructure.Sniffing;

/// <summary>
/// 多标签页嗅探协调器。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要它</b>：界面与 <c>SniffListBinder</c> 只认一个 <see cref="IVideoSniffer"/>。
/// 引入多标签页后，每个标签都要有自己的 <see cref="WebView2Sniffer"/>，但列表只有一个。
/// 本类承担「把多个嗅探器收敛成一个」的职责：对外暴露单一事件面，对内管理每个标签的嗅探器。
/// </para>
/// <para>
/// <b>跨标签归并的关键</b>：所有标签的嗅探器都被注入<b>同一份</b> <see cref="VideoFamilyIndex"/>。
/// 于是同一个视频即便在两个标签里分别被捕获（或一个标签抓到主清单、另一个抓到分片），
/// 家族索引也会把它们归并成一行，界面无需做任何跨标签去重。
/// </para>
/// <para>
/// <b>开关语义</b>：<see cref="Start"/> / <see cref="Stop"/> 是总开关，作用于全部已挂接的标签。
/// 总开关关闭期间新建的标签不会被启动——这样不会出现「用户关了嗅探，开个新标签却又能抓」的矛盾行为。
/// </para>
/// </remarks>
public sealed class SniffCoordinator : IVideoSniffer, IDisposable
{
    private readonly VideoFamilyIndex _index;
    private readonly Func<WebView2, VideoFamilyIndex, IVideoSniffer> _snifferFactory;
    private readonly Dictionary<WebView2, IVideoSniffer> _sniffers = new();

    private bool _enabled = true;
    private bool _disposed;

    /// <summary>
    /// 初始化协调器。
    /// </summary>
    /// <param name="snifferFactory">
    /// 嗅探器工厂（WebView2 + 共享索引 → 嗅探器）。为空时使用 <see cref="WebView2Sniffer"/>。
    /// 该参数主要为单元测试提供注入点，使测试无需真实 WebView2。
    /// </param>
    /// <param name="maxTrackedItems">家族数量上限。</param>
    /// <param name="mediaFetcher">
    /// 可选的取数器，透传给每个标签的嗅探器，用于主清单的变体时长探测。
    /// 为 null 时跳过探测（嗅探列表的时长列显示「-」）。
    /// </param>
    public SniffCoordinator(
        Func<WebView2, VideoFamilyIndex, IVideoSniffer>? snifferFactory = null,
        int maxTrackedItems = VideoFamilyIndex.DefaultCapacity,
        IMediaFetcher? mediaFetcher = null)
    {
        _index = new VideoFamilyIndex(maxTrackedItems);
        _snifferFactory = snifferFactory
            ?? ((webView, index) => new WebView2Sniffer(webView, index, maxTrackedItems, mediaFetcher));

        SniffDiagnostics.Reset();
        SniffDiagnostics.Write($"coordinator created; maxTracked={maxTrackedItems}");
    }

    /// <inheritdoc />
    public event EventHandler<SniffedVideo>? VideoDetected;

    /// <inheritdoc />
    public event EventHandler<SniffedVideo>? VideoRetracted;

    /// <summary>嗅探总开关：关闭时任何标签都不再上报新资源。</summary>
    public bool Enabled => _enabled;

    /// <summary>已挂接的标签数量。</summary>
    public int TabCount => _sniffers.Count;

    /// <summary>已归并出的视频数量（即界面上应有的行数）。</summary>
    public int CapturedCount => _index.FamilyCount;

    /// <summary>
    /// 为一个标签挂接嗅探器。
    /// </summary>
    /// <param name="webView">该标签的 WebView2 控件。</param>
    /// <returns>新挂接返回 true；该控件此前已挂接或已释放时返回 false。</returns>
    public bool Attach(WebView2 webView)
    {
        ArgumentNullException.ThrowIfNull(webView);

        if (_disposed || _sniffers.ContainsKey(webView))
        {
            return false;
        }

        var sniffer = _snifferFactory(webView, _index);
        sniffer.VideoDetected += OnTabVideoDetected;
        sniffer.VideoRetracted += OnTabVideoRetracted;

        _sniffers[webView] = sniffer;

        SniffDiagnostics.Write($"coordinator attach; tabCount={_sniffers.Count}; enabled={_enabled}");

        // 总开关关闭期间新建的标签不启动，保持「关了就是关了」的一致性
        if (_enabled)
        {
            sniffer.Start();
        }

        return true;
    }

    /// <summary>
    /// 解除一个标签的嗅探器（关闭标签时调用）。
    /// </summary>
    /// <param name="webView">该标签的 WebView2 控件。</param>
    /// <returns>成功解除返回 true；本就没有挂接时返回 false。</returns>
    public bool Detach(WebView2 webView)
    {
        ArgumentNullException.ThrowIfNull(webView);

        if (!_sniffers.Remove(webView, out var sniffer))
        {
            return false;
        }

        sniffer.VideoDetected -= OnTabVideoDetected;
        sniffer.VideoRetracted -= OnTabVideoRetracted;
        sniffer.Stop();

        SniffDiagnostics.Write($"coordinator detach; tabCount={_sniffers.Count}");

        if (sniffer is IDisposable disposable)
        {
            disposable.Dispose();
        }

        return true;
    }

    /// <inheritdoc />
    public void Start()
    {
        _enabled = true;

        foreach (var sniffer in _sniffers.Values)
        {
            sniffer.Start();
        }
    }

    /// <summary>
    /// 立即扫描指定标签的当前页面（右键「下载视频」）。
    /// </summary>
    /// <param name="webView">目标标签的浏览器控件。</param>
    /// <returns>扫描结束后兑现的任务；该控件未挂接嗅探器时直接完成。</returns>
    /// <remarks>
    /// 扫描产出的地址同样流经共享家族索引：列表中已有的地址不会再被处理，
    /// 因此重复右键不会刷出重复行。
    /// </remarks>
    public async Task SniffPageAsync(WebView2 webView)
    {
        ArgumentNullException.ThrowIfNull(webView);

        if (_disposed || !_sniffers.TryGetValue(webView, out var sniffer))
        {
            return;
        }

        if (sniffer is WebView2Sniffer concrete)
        {
            await concrete.SniffPageNowAsync().ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        _enabled = false;

        foreach (var sniffer in _sniffers.Values)
        {
            sniffer.Stop();
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        // 共享索引是唯一的归并真相来源，清它即可让所有标签的已捕获结果一并失效
        _index.Clear();
    }

    /// <summary>
    /// 释放协调器，解除全部标签的嗅探。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var webView in _sniffers.Keys.ToList())
        {
            Detach(webView);
        }

        _sniffers.Clear();
    }

    /// <summary>
    /// 转发某个标签的「发现视频」事件。
    /// </summary>
    /// <param name="sender">事件源（标签嗅探器）。</param>
    /// <param name="video">视频条目。</param>
    /// <remarks>
    /// 用协调器自身作为 sender 重新抛出：订阅方（界面绑定器）只关心条目内容，
    /// 暴露内部的单个标签嗅探器反而会让订阅方产生「它代表全部标签」的误解。
    /// </remarks>
    private void OnTabVideoDetected(object? sender, SniffedVideo video)
    {
        if (_enabled)
        {
            VideoDetected?.Invoke(this, video);
        }
    }

    /// <summary>
    /// 转发某个标签的「撤回条目」事件。
    /// </summary>
    /// <param name="sender">事件源（标签嗅探器）。</param>
    /// <param name="video">被撤回的条目。</param>
    /// <remarks>
    /// 撤回不受总开关影响：它是对<b>已展示内容</b>的修正，
    /// 若被开关拦下会让列表残留已被归并掉的噪音行。
    /// </remarks>
    private void OnTabVideoRetracted(object? sender, SniffedVideo video)
        => VideoRetracted?.Invoke(this, video);
}
