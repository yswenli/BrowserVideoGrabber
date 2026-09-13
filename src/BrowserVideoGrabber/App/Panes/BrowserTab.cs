/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.App.Panes
*文件名： BrowserTab
*版本号： V1.0.0.0
*唯一标识：5c3d7f5e-8aa8-4676-b3df-7a0a5adb2e0f
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 02:05:00
*描述：浏览器标签页，封装一个独立 WebView2 及其导航状态，并把导航事件向上抛给面板。
*
*=================================================
*修改标记
*修改时间：2026/9/13 02:05:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BrowserVideoGrabber.App.Panes;

/// <summary>
/// 浏览器标签页。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么每个标签自己订阅导航事件</b>：多标签下所有 <see cref="WebView2"/> 都常驻内存
/// （非活动标签只是不可见，页面与嗅探订阅都保留）。若由面板统一订阅，切换标签时就要反复
/// 增删事件处理器，既容易漏删也容易重复订阅。让每个标签订阅自己的内核事件，面板只消费
/// 标签抛出的语义事件，订阅关系在整个生命周期内保持稳定。
/// </para>
/// <para>
/// <b>环境共享</b>：标签本身不创建 <c>CoreWebView2Environment</c>，由面板注入同一份环境，
/// 这样 Cookie / 登录态在所有标签间共享，登录后下载鉴权内容的能力得以保留。
/// </para>
/// </remarks>
public sealed class BrowserTab : IDisposable
{
    private bool _subscribed;
    private bool _disposed;
    private string? _pendingUrl;

    /// <summary>
    /// 初始化一个标签。
    /// </summary>
    public BrowserTab()
    {
        Id = Guid.NewGuid();
        WebView = new WebView2 { Dock = DockStyle.Fill, Visible = false };

        // 内核异步初始化：构造时一定未就绪，必须等该事件（或 EnsureCoreAsync 返回）后才能操作
        WebView.CoreWebView2InitializationCompleted += OnCoreInitializationCompleted;
    }

    /// <summary>稳定标识，供字典索引与标签条定位。</summary>
    public Guid Id { get; }

    /// <summary>本标签的浏览器控件。</summary>
    public WebView2 WebView { get; }

    /// <summary>页面标题；取不到时回退为「新标签页」。</summary>
    public string Title { get; private set; } = "新标签页";

    /// <summary>当前地址。</summary>
    public string Url { get; private set; } = string.Empty;

    /// <summary>是否正在加载。</summary>
    public bool IsLoading { get; private set; }

    /// <summary>内核是否已就绪（就绪后才能导航）。</summary>
    public bool IsCoreReady { get; private set; }

    /// <summary>标题变化（用于刷新标签条文字）。</summary>
    public event EventHandler<BrowserTab>? TitleChanged;

    /// <summary>地址变化（用于同步地址栏）。</summary>
    public event EventHandler<BrowserTab>? UrlChanged;

    /// <summary>加载状态变化（用于显示/隐藏加载提示器）。</summary>
    public event EventHandler<BrowserTab>? LoadingChanged;

    /// <summary>
    /// 一次导航结束。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="UrlChanged"/> 的区别：本事件带成功标记，历史记录只应记录成功的导航，
    /// 失败地址（如打不开的站点）不该污染历史。
    /// </remarks>
    public event EventHandler<BrowserTabNavigatedEventArgs>? Navigated;

    /// <summary>内核就绪。面板在此刻把嗅探器挂到本标签的控件上。</summary>
    public event EventHandler<BrowserTab>? CoreReady;

    /// <summary>导航失败，需要向用户提示。</summary>
    public event EventHandler<string>? NavigationFailed;

    /// <summary>
    /// 用户在页面右键菜单中点击了「下载视频」，请求扫描当前页面并嗅探视频/直播流。
    /// </summary>
    /// <remarks>
    /// 由「下载视频」自定义菜单项触发：是否重复上报（已有地址不再处理）由嗅探器
    /// 内部的家族去重索引负责，标签本身不感知。
    /// </remarks>
    public event EventHandler<BrowserTab>? VideoScanRequested;

    /// <summary>
    /// 页面请求打开新窗口（window.open / target="_blank"）。
    /// </summary>
    /// <remarks>
    /// 我们始终把新窗口请求路由到新标签（见 BrowserPane 的处理），
    /// 绝不允许系统弹出独立 WebView2 窗口 —— 那会脱离统一的会话与嗅探体系。
    /// </remarks>
    public event EventHandler<string>? NewWindowRequested;

    /// <summary>
    /// 让本标签使用指定的共享环境完成内核初始化。
    /// </summary>
    /// <param name="environment">共享的 WebView2 环境。</param>
    /// <returns>表示异步初始化的任务。</returns>
    public async Task EnsureCoreAsync(CoreWebView2Environment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        if (IsCoreReady)
        {
            return;
        }

        await WebView.EnsureCoreWebView2Async(environment).ConfigureAwait(true);

        // EnsureCoreWebView2Async 返回时事件通常已触发，这里再挂一次以覆盖
        // 「事件先于 await 完成」的情况；AttachCore 内部做了去重
        AttachCore();
    }

    /// <summary>
    /// 导航到指定地址。
    /// </summary>
    /// <param name="url">目标地址。</param>
    /// <remarks>
    /// 内核未就绪时先暂存，就绪后自动补上——新建标签立刻导航是常见操作，
    /// 不能因为异步初始化而静默丢弃用户这次导航。
    /// </remarks>
    public void Navigate(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (!IsCoreReady)
        {
            _pendingUrl = url;
            return;
        }

        try
        {
            WebView.CoreWebView2.Navigate(url);
        }
        catch (ArgumentException)
        {
            NavigationFailed?.Invoke(this, $"无法识别的地址：{url}");
        }
    }

    /// <summary>后退。</summary>
    public void GoBack()
    {
        if (IsCoreReady && WebView.CoreWebView2.CanGoBack)
        {
            WebView.CoreWebView2.GoBack();
        }
    }

    /// <summary>前进。</summary>
    public void GoForward()
    {
        if (IsCoreReady && WebView.CoreWebView2.CanGoForward)
        {
            WebView.CoreWebView2.GoForward();
        }
    }

    /// <summary>重新加载。</summary>
    public void Reload()
    {
        if (IsCoreReady)
        {
            WebView.CoreWebView2.Reload();
        }
    }

    /// <summary>停止加载。</summary>
    public void Stop()
    {
        if (IsCoreReady)
        {
            WebView.CoreWebView2.Stop();
        }
    }

    /// <summary>
    /// 释放标签与其浏览器控件。
    /// </summary>
    /// <remarks>
    /// 必须显式释放 <see cref="WebView2"/>：它持有浏览器进程与大量非托管资源，
    /// 否则关闭标签后仍会残留 <c>msedgewebview2.exe</c> 进程。
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        WebView.CoreWebView2InitializationCompleted -= OnCoreInitializationCompleted;
        DetachCore();
        WebView.Dispose();
    }

    /// <summary>内核初始化完成回调。</summary>
    private void OnCoreInitializationCompleted(
        object? sender,
        CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            return;
        }

        AttachCore();
    }

    /// <summary>
    /// 挂接内核事件（幂等）。
    /// </summary>
    private void AttachCore()
    {
        if (_subscribed)
        {
            return;
        }

        var core = WebView.CoreWebView2;
        if (core is null)
        {
            return;
        }

        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.IsStatusBarEnabled = false;

        core.NavigationStarting += OnNavigationStarting;
        core.NavigationCompleted += OnNavigationCompleted;
        core.SourceChanged += OnSourceChanged;
        core.DocumentTitleChanged += OnDocumentTitleChanged;
        core.ContextMenuRequested += OnContextMenuRequested;
        core.NewWindowRequested += OnNewWindowRequested;

        _subscribed = true;
        IsCoreReady = true;

        CoreReady?.Invoke(this, this);

        if (!string.IsNullOrWhiteSpace(_pendingUrl))
        {
            var pending = _pendingUrl;
            _pendingUrl = null;
            Navigate(pending);
        }
    }

    /// <summary>解除内核事件订阅。</summary>
    private void DetachCore()
    {
        var core = WebView.CoreWebView2;
        if (core is null || !_subscribed)
        {
            return;
        }

        try
        {
            core.NavigationStarting -= OnNavigationStarting;
            core.NavigationCompleted -= OnNavigationCompleted;
            core.SourceChanged -= OnSourceChanged;
            core.DocumentTitleChanged -= OnDocumentTitleChanged;
            core.ContextMenuRequested -= OnContextMenuRequested;
            core.NewWindowRequested -= OnNewWindowRequested;
        }
        catch (InvalidOperationException)
        {
            // 控件已释放
        }

        _subscribed = false;
    }

    /// <summary>
    /// 页面请求打开新窗口（a target="_blank" / window.open）：拦截并路由到 BrowserPane 创建新标签。
    /// </summary>
    /// <param name="sender">事件源（内核对象）。</param>
    /// <param name="e">事件参数，携带目标 URL。</param>
    /// <remarks>
    /// 只设 <c>e.Handled = true</c> 阻止 WebView2 默认行为（弹独立窗口或复用当前 tab），
    /// 但<b>不主动把 NewWindow 置空</b> —— 部分 WebView2 版本在 Handled=true + NewWindow=null
    /// 时会把导航完全吞掉或产生异常。让 BrowserPane 在自己的 WebView 上调 Navigate 更可靠。
    /// </remarks>
    private void OnNewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs e)
    {
        try
        {
            // 只要 Handled=true 就足以阻止 WebView2 的自动弹窗/复用当前 tab
            e.Handled = true;

            var targetUrl = e.Uri;
            if (string.IsNullOrWhiteSpace(targetUrl)
                || targetUrl.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            NewWindowRequested?.Invoke(this, targetUrl);
        }
        catch (InvalidOperationException)
        {
            // 控件释放竞态
        }
    }

    /// <summary>
    /// 页面右键菜单弹出前回调：在系统菜单末尾追加「下载视频」自定义项。
    /// </summary>
    /// <param name="sender">事件源（内核对象）。</param>
    /// <param name="e">事件参数，携带即将展示的菜单项集合。</param>
    /// <remarks>
    /// 刻意<b>不</b>把 <c>Handled</c> 置真 —— 系统默认菜单（复制、粘贴、检查元素等）
    /// 对一款内置浏览器仍有价值；自定义项以分隔线隔开后追加在最后，避免干扰用户
    /// 对系统菜单的既有肌肉记忆。
    /// </remarks>
    private void OnContextMenuRequested(
        object? sender,
        CoreWebView2ContextMenuRequestedEventArgs e)
    {
        try
        {
            var core = WebView.CoreWebView2;
            if (core is null)
            {
                return;
            }

            // 末尾加一条分隔线 + 自定义项，视觉上与系统菜单区分开
            var separator = core.Environment.CreateContextMenuItem(
                string.Empty,
                null!,
                CoreWebView2ContextMenuItemKind.Separator);

            var item = core.Environment.CreateContextMenuItem(
                "下载视频",
                null!,
                CoreWebView2ContextMenuItemKind.Command);

            item.CustomItemSelected += (_, _) => VideoScanRequested?.Invoke(this, this);

            e.MenuItems.Add(separator);
            e.MenuItems.Add(item);
        }
        catch (InvalidOperationException)
        {
            // 控件释放竞态等：菜单少一个自定义项不致命
        }
    }

    /// <summary>导航开始：进入加载态并同步地址。</summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        IsLoading = true;
        SetUrl(e.Uri);
        LoadingChanged?.Invoke(this, this);
    }

    /// <summary>导航完成：退出加载态，失败时提示。</summary>
    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        IsLoading = false;

        var url = WebView.Source?.ToString() ?? Url;

        Navigated?.Invoke(this, new BrowserTabNavigatedEventArgs(this, url, e.IsSuccess));
        LoadingChanged?.Invoke(this, this);

        if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
        {
            // 用户主动停止（OperationCanceled）不算错误，不必打扰
            NavigationFailed?.Invoke(this, $"页面加载失败：{e.WebErrorStatus}");
        }
    }

    /// <summary>源地址变化：同步地址。</summary>
    private void OnSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        var url = WebView.Source?.ToString();
        if (!string.IsNullOrWhiteSpace(url))
        {
            SetUrl(url);
        }
    }

    /// <summary>文档标题变化：刷新标签条文字。</summary>
    private void OnDocumentTitleChanged(object? sender, object e)
    {
        var title = WebView.CoreWebView2?.DocumentTitle;
        Title = string.IsNullOrWhiteSpace(title) ? "新标签页" : title;

        TitleChanged?.Invoke(this, this);
    }

    /// <summary>更新地址并向外抛出。</summary>
    /// <param name="url">新地址。</param>
    private void SetUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || string.Equals(Url, url, StringComparison.Ordinal))
        {
            return;
        }

        Url = url;
        UrlChanged?.Invoke(this, this);
    }
}
