/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.App.Panes
*文件名： BrowserPane
*版本号： V1.0.0.0
*唯一标识：813ace4d-1f6b-4c28-9d05-3a7e2b8c5f19
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 01:20:00
*描述：左侧浏览器面板，由导航工具栏、多标签条与标签页宿主区组成。
*
*=================================================
*修改标记
*修改时间：2026/9/13 02:10:00
*修改人： yswenli
*版本号： V1.1.0.0
*描述：由单 WebView2 重构为多标签容器：共享一份 CoreWebView2Environment，
*      每个标签一个 WebView2（非活动标签隐藏但保留页面与嗅探订阅），
*      地址栏/前进后退/加载提示器均跟随活动标签；新增 TabAttached / TabDetaching 供上层挂接嗅探器。
*修改时间：2026/9/13 12:30:00
*修改人： yswenli
*版本号： V1.2.0.0
*描述：布局调整为「标签条在上、工具栏居中」使工具栏与浏览器页面连成一体；
*      彻底移除嗅探开关按钮（嗅探常开不可关）；新增 VideoScanRequested 事件，
*      转发标签页右键菜单「下载视频」的主动扫描意图。
*
*****************************************************************************/

using System.ComponentModel;
using BrowserVideoGrabber.App.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BrowserVideoGrabber.App.Panes;

/// <summary>
/// 浏览器面板：标签条在上、导航工具栏居中、标签宿主区在下，三者构成一个浏览器整体。
/// </summary>
/// <remarks>
/// <para>
/// 本面板只负责「浏览器」这一件事：嗅探器与下载器均通过 <see cref="ActiveWebView"/> 与
/// <see cref="TabAttached"/> 暴露出去后由上层装配，因此面板本身对嗅探、下载零依赖。
/// </para>
/// <para>
/// <b>为什么共享一个环境</b>：所有标签都用同一个 <c>CoreWebView2Environment</c>（同一用户数据目录），
/// Cookie 与登录态因此在标签间共享，「登录后可下载需鉴权内容」这一能力得以保留。
/// </para>
/// <para>
/// <b>用户数据目录</b>：显式指向 <c>%LOCALAPPDATA%\BrowserVideoGrabber\WebView2</c>。
/// 若沿用默认位置（可执行文件同级目录），当程序被放在 Program Files 等只读目录时
/// WebView2 将无法创建缓存而直接初始化失败。
/// </para>
/// </remarks>
public sealed class BrowserPane : UserControl
{
    private const string SearchUrlFormat = "https://www.bing.com/search?q={0}";

    private readonly ToolStrip _toolStrip = new()
    {
        GripStyle = ToolStripGripStyle.Hidden,
        RenderMode = ToolStripRenderMode.Professional,
        Renderer = new CapsuleToolStripRenderer(),
        Dock = DockStyle.Top,
        Padding = new Padding(4, 2, 4, 2),
        AutoSize = true
    };

    private readonly BrowserTabStrip _tabStrip = new();

    private readonly Panel _hostPanel = new() { Dock = DockStyle.Fill };

    private readonly ToolStripButton _backButton;
    private readonly ToolStripButton _forwardButton;
    private readonly ToolStripButton _refreshButton;
    private readonly ToolStripButton _stopButton;
    private readonly ToolStripButton _goButton;
    private readonly ToolStripButton _favoriteButton;
    private bool _pageFavorited;
    private readonly ToolStripTextBox _addressBox;

    private readonly ToolStripLabel _loadingLabel;
    private readonly ToolStripProgressBar _loadingBar;

    private readonly List<BrowserTab> _tabs = new();

    private CoreWebView2Environment? _environment;
    private BrowserTab? _activeTab;
    private bool _loading;

    /// <summary>
    /// 初始化浏览器面板。
    /// </summary>
    public BrowserPane()
    {
        BackColor = Color.FromArgb(250, 250, 250);

        _backButton = CreateNavButton("←", "后退", (_, _) => NavigateBack());
        _forwardButton = CreateNavButton("→", "前进", (_, _) => NavigateForward());
        _refreshButton = CreateNavButton("⟳", "刷新", (_, _) => _activeTab?.Reload());
        _stopButton = CreateNavButton("⏹", "停止", (_, _) => _activeTab?.Stop());

        _addressBox = new ToolStripTextBox
        {
            AutoSize = false,
            Width = 380,
            ToolTipText = "输入网址后回车；也可直接输入关键词进行搜索"
        };
        _addressBox.KeyDown += OnAddressBoxKeyDown;

        _goButton = CreateNavButton("↵", "转到", (_, _) => Navigate(_addressBox.Text));

        _favoriteButton = CreateNavButton("☆", "收藏", OnFavoriteButtonClick);

        // 加载提示器：导航开始显示在跑马灯进度条 + 「加载中…」，导航完成后隐藏。
        _loadingLabel = new ToolStripLabel("加载中…") { Visible = false };
        _loadingBar = new ToolStripProgressBar
        {
            Visible = false,
            Style = ProgressBarStyle.Marquee,
            Width = 100,
            MarqueeAnimationSpeed = 30
        };

        _toolStrip.Items.AddRange(
        [
            _backButton,
            _forwardButton,
            _refreshButton,
            _stopButton,
            new ToolStripSeparator(),
            _addressBox,
            _goButton,
            _favoriteButton,
            new ToolStripSeparator(),
            _loadingLabel,
            _loadingBar
        ]);

        _tabStrip.TabSelected += (_, tab) => ActivateTab(tab);
        _tabStrip.TabCloseRequested += (_, tab) => CloseTab(tab);
        _tabStrip.NewTabRequested += OnNewTabRequested;

        // 停靠顺序即层级顺序：先加填充控件，再加边缘控件；
        // 最后加入的 Top 控件最贴近父容器上边缘 —— 因此「标签条」最后加入，
        // 从上到下依次为：标签条 → 导航工具栏 → 浏览器宿主区。
        // 导航工具栏与标签/页面同属一个浏览器整体，故夹在标签条与页面之间。
        Controls.Add(_hostPanel);
        Controls.Add(_toolStrip);
        Controls.Add(_tabStrip);

        UpdateNavigationState();
    }

    /// <summary>同时打开的标签页上限。默认 10。</summary>
    /// <remarks>
    /// 标记为不参与设计器序列化：本控件完全由代码构造，
    /// 若让设计器把该属性写进 InitializeComponent，运行时的赋值顺序会被设计器代码覆盖。
    /// </remarks>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int MaxTabs { get; set; } = 10;

    /// <summary>全部标签页。</summary>
    public IReadOnlyList<BrowserTab> Tabs => _tabs;

    /// <summary>当前活动标签；尚未创建标签时为 null。</summary>
    public BrowserTab? ActiveTab => _activeTab;

    /// <summary>活动标签的浏览器控件，供上层装配请求上下文提供者等。</summary>
    public WebView2? ActiveWebView => _activeTab?.WebView;

    /// <summary>活动标签的内核是否已就绪。</summary>
    public bool IsCoreReady => _activeTab?.IsCoreReady == true;

    /// <summary>活动标签的当前地址。</summary>
    public string CurrentUrl => _activeTab?.Url ?? string.Empty;

    /// <summary>活动标签的当前标题。</summary>
    public string CurrentTitle => _activeTab?.Title ?? string.Empty;

    /// <summary>需要向用户提示一条消息时触发（如导航失败、达到标签上限）。</summary>
    public event EventHandler<string>? MessageReported;

    /// <summary>活动标签导航完成（无论成败）；成功时可用于写入历史。</summary>
    public event EventHandler<BrowserTabNavigatedEventArgs>? Navigated;

    /// <summary>用户请求收藏/取消收藏当前页面。</summary>
    public event EventHandler<FavoriteToggleEventArgs>? FavoriteRequested;

    /// <summary>
    /// 当前活动标签被切换时触发（用户点击标签条切换，或程序内部调用 ActivateTab）。
    /// </summary>
    public event EventHandler? ActiveTabChanged;

    /// <summary>
    /// 新标签的内核已就绪，上层应在此把嗅探器挂到该标签上。
    /// </summary>
    public event EventHandler<BrowserTab>? TabAttached;

    /// <summary>
    /// 标签即将被关闭，上层应在此解除该标签的嗅探挂接。
    /// </summary>
    public event EventHandler<BrowserTab>? TabDetaching;

    /// <summary>
    /// 用户在某个标签的页面右键菜单中点击了「下载视频」。
    /// </summary>
    /// <remarks>
    /// 上层应调用嗅探协调器扫描该标签的当前页面；是否重复上报
    /// （已有地址不再处理）由嗅探器的家族去重索引保证。
    /// </remarks>
    public event EventHandler<BrowserTab>? VideoScanRequested;

    /// <summary>
    /// 初始化 WebView2 环境并打开初始标签。
    /// </summary>
    /// <param name="initialUrls">
    /// 每个元素作为一个标签打开的地址；为空时只打开一个空白标签。
    /// </param>
    /// <returns>表示异步初始化的任务。</returns>
    /// <exception cref="InvalidOperationException">WebView2 运行时缺失导致初始化失败时抛出。</exception>
    public async Task InitializeAsync(IReadOnlyList<string>? initialUrls)
    {
        if (_environment is not null)
        {
            return;
        }

        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrowserVideoGrabber",
            "WebView2");

        Directory.CreateDirectory(userDataFolder);

        try
        {
            _environment = await CoreWebView2Environment
                .CreateAsync(userDataFolder: userDataFolder)
                .ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is WebView2RuntimeNotFoundException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                "WebView2 运行时初始化失败。请先安装 Microsoft Edge WebView2 Runtime（Windows 10/11 通常已内置）。",
                exception);
        }

        if (initialUrls is { Count: > 0 })
        {
            foreach (var url in initialUrls.Take(MaxTabs))
            {
                await AddTabAsync(url).ConfigureAwait(true);
            }
        }
        else
        {
            await AddTabAsync(null).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// 新建一个标签。
    /// </summary>
    /// <param name="url">打开的地址；为空则打开空白页。</param>
    /// <returns>新标签；已达上限或环境未就绪时返回 null。</returns>
    public async Task<BrowserTab?> AddTabAsync(string? url)
    {
        if (_environment is null)
        {
            return null;
        }

        if (_tabs.Count >= MaxTabs)
        {
            MessageReported?.Invoke(this, $"最多同时打开 {MaxTabs} 个标签页，请先关闭一些再新建。");
            return null;
        }

        var tab = new BrowserTab();
        WireTab(tab);

        _tabs.Add(tab);
        _tabStrip.Add(tab);

        await tab.EnsureCoreAsync(_environment).ConfigureAwait(true);

        // 内核就绪后再通知上层：嗅探器要挂到 CoreWebView2 上，早于此会挂空
        TabAttached?.Invoke(this, tab);

        if (!string.IsNullOrWhiteSpace(url))
        {
            tab.Navigate(url);
        }

        ActivateTab(tab);
        return tab;
    }

    /// <summary>
    /// 关闭一个标签。最后一个标签不会被关闭，而是重置为空白页。
    /// </summary>
    /// <param name="tab">待关闭的标签。</param>
    public void CloseTab(BrowserTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        if (!_tabs.Contains(tab))
        {
            return;
        }

        // 保留最后一个标签：全部关掉会让浏览器区域变空，用户无从继续
        if (_tabs.Count == 1)
        {
            tab.Navigate("about:blank");
            return;
        }

        TabDetaching?.Invoke(this, tab);

        var index = _tabs.IndexOf(tab);

        _tabs.Remove(tab);
        _tabStrip.Remove(tab);
        UnwireTab(tab);

        _hostPanel.Controls.Remove(tab.WebView);
        tab.Dispose();

        if (!ReferenceEquals(_activeTab, tab))
        {
            return;
        }

        _activeTab = null;

        var fallback = _tabs[Math.Min(index, _tabs.Count - 1)];
        ActivateTab(fallback);
    }

    /// <summary>
    /// 切换活动标签。
    /// </summary>
    /// <param name="tab">目标标签。</param>
    /// <remarks>
    /// 只把活动标签的 <c>WebView2</c> 放进宿主区；其余标签的控件保留在内存中但不可见，
    /// 这样它们的页面状态与嗅探订阅都不会因为切换而丢失。
    /// </remarks>
    public void ActivateTab(BrowserTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        if (ReferenceEquals(_activeTab, tab))
        {
            return;
        }

        _hostPanel.SuspendLayout();

        if (_activeTab is not null)
        {
            _hostPanel.Controls.Remove(_activeTab.WebView);
            _activeTab.WebView.Visible = false;
        }

        _activeTab = tab;
        _hostPanel.Controls.Add(tab.WebView);
        tab.WebView.Visible = true;

        _hostPanel.ResumeLayout(performLayout: true);

        _tabStrip.ActiveTab = tab;

        _loading = tab.IsLoading;
        SyncAddressBar();
        UpdateNavigationState();

        ActiveTabChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 导航到指定地址。输入的地址若不含协议则自动补全，若不像地址则按关键词搜索。
    /// </summary>
    /// <param name="input">地址或搜索关键词。</param>
    public void Navigate(string? input)
    {
        var target = NormalizeInput(input);
        if (target is null)
        {
            return;
        }

        if (_activeTab is null)
        {
            // 尚未创建任何标签时先记下地址，初始化结束后由上层再次调用
            _addressBox.Text = target;
            return;
        }

        _activeTab.Navigate(target);
    }

    /// <summary>后退。</summary>
    public void NavigateBack() => _activeTab?.GoBack();

    /// <summary>前进。</summary>
    public void NavigateForward() => _activeTab?.GoForward();

    /// <summary>
    /// 收集全部标签的地址，用于关闭时保存会话。
    /// </summary>
    /// <returns>各标签地址（跳过空白页）。</returns>
    public IReadOnlyList<string> GetTabUrls()
        => _tabs
            .Select(x => x.Url)
            .Where(url => !string.IsNullOrWhiteSpace(url)
                && !url.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // WebView2 持有浏览器进程与大量非托管资源，必须逐个显式释放，
            // 否则关闭主窗体后仍会残留若干 msedgewebview2.exe 进程
            foreach (var tab in _tabs)
            {
                UnwireTab(tab);
                tab.Dispose();
            }

            _tabs.Clear();
            _hostPanel.Dispose();
            _tabStrip.Dispose();
            _toolStrip.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// 订阅一个标签的事件。
    /// </summary>
    /// <param name="tab">标签。</param>
    private void WireTab(BrowserTab tab)
    {
        tab.TitleChanged += OnTabTitleChanged;
        tab.UrlChanged += OnTabUrlChanged;
        tab.LoadingChanged += OnTabLoadingChanged;
        tab.Navigated += OnTabNavigated;
        tab.NavigationFailed += OnTabNavigationFailed;
        tab.VideoScanRequested += OnTabVideoScanRequested;
    }

    /// <summary>
    /// 取消订阅一个标签的事件。
    /// </summary>
    /// <param name="tab">标签。</param>
    private void UnwireTab(BrowserTab tab)
    {
        tab.TitleChanged -= OnTabTitleChanged;
        tab.UrlChanged -= OnTabUrlChanged;
        tab.LoadingChanged -= OnTabLoadingChanged;
        tab.Navigated -= OnTabNavigated;
        tab.NavigationFailed -= OnTabNavigationFailed;
        tab.VideoScanRequested -= OnTabVideoScanRequested;
    }

    /// <summary>标签页右键「下载视频」：把意图原样抛给上层。</summary>
    private void OnTabVideoScanRequested(object? sender, BrowserTab tab)
        => VideoScanRequested?.Invoke(this, tab);

    /// <summary>标签标题变化：刷新标签条。</summary>
    private void OnTabTitleChanged(object? sender, BrowserTab tab)
    {
        _tabStrip.Invalidate();

        if (ReferenceEquals(tab, _activeTab))
        {
            SyncAddressBar();
        }
    }

    /// <summary>标签地址变化：活动标签时同步地址栏。</summary>
    private void OnTabUrlChanged(object? sender, BrowserTab tab)
    {
        if (ReferenceEquals(tab, _activeTab))
        {
            SyncAddressBar();
        }
    }

    /// <summary>标签加载状态变化：活动标签时联动加载提示器。</summary>
    private void OnTabLoadingChanged(object? sender, BrowserTab tab)
    {
        if (!ReferenceEquals(tab, _activeTab))
        {
            return;
        }

        _loading = tab.IsLoading;
        UpdateNavigationState();
    }

    /// <summary>
    /// 标签导航完成：仅活动标签向外抛（历史记录不应记入后台标签的跳转）。
    /// </summary>
    private void OnTabNavigated(object? sender, BrowserTabNavigatedEventArgs e)
    {
        if (!ReferenceEquals(e.Tab, _activeTab))
        {
            return;
        }

        Navigated?.Invoke(this, e);
    }

    /// <summary>标签导航失败：仅活动标签提示，避免后台标签刷屏。</summary>
    private void OnTabNavigationFailed(object? sender, string message)
    {
        if (sender is BrowserTab tab && !ReferenceEquals(tab, _activeTab))
        {
            return;
        }

        MessageReported?.Invoke(this, message);
    }

    /// <summary>「新建标签」按钮：新建空白标签。</summary>
    private async void OnNewTabRequested(object? sender, EventArgs e)
        => await AddTabAsync(null).ConfigureAwait(true);

    /// <summary>「收藏当前页」按钮点击：让主窗体决定是添加还是取消。</summary>
    private void OnFavoriteButtonClick(object? sender, EventArgs e)
    {
        FavoriteRequested?.Invoke(this, new FavoriteToggleEventArgs { IsFavorited = _pageFavorited });
    }

    /// <summary>
    /// 设置收藏按钮的状态（图标 + 文字）。不再用 Checked 表达收藏状态，
    /// 因为 Checked 会触发胶囊渲染器绘制品牌紫色填充，导致按钮颜色变化。
    /// </summary>
    /// <param name="isFavorited">true = 已收藏（实心星）；false = 未收藏（空心星）。</param>
    public void SetFavoriteState(bool isFavorited)
    {
        if (_favoriteButton is null) return;

        _pageFavorited = isFavorited;
        _favoriteButton.Text = isFavorited ? "已收藏" : "收藏";
        _favoriteButton.Image = CapsuleToolStripRenderer.CreateGlyphIcon(isFavorited ? "★" : "☆");
        _favoriteButton.ToolTipText = isFavorited ? "点击取消收藏" : "把当前页面加入收藏";
    }

    /// <summary>
    /// 创建导航工具栏的普通按钮（图标 + 文字，胶囊风格）。
    /// </summary>
    /// <param name="glyph">图标字符（emoji 或 Unicode 符号）。</param>
    /// <param name="label">按钮文字。</param>
    /// <param name="onClick">点击回调。</param>
    /// <returns>按钮实例。</returns>
    private static ToolStripButton CreateNavButton(string glyph, string label, EventHandler onClick)
    {
        var button = new ToolStripButton
        {
            Text = label,
            Image = CapsuleToolStripRenderer.CreateGlyphIcon(glyph),
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            AutoSize = true,
            ToolTipText = label,
            Margin = new Padding(4, 0, 4, 0),
            Padding = new Padding(6, 2, 6, 2),
            TextAlign = ContentAlignment.MiddleCenter
        };

        button.Click += onClick;
        return button;
    }

    /// <summary>
    /// 把用户输入规范化为可导航的地址。
    /// </summary>
    /// <param name="input">原始输入。</param>
    /// <returns>导航地址；输入为空时返回 null。</returns>
    /// <remarks>
    /// 判定规则刻意保持简单：含协议或形似域名则当作地址并补全 https，其余按关键词搜索。
    /// 这样「输入 baidu.com」与「输入 张学友」都能得到符合直觉的结果。
    /// </remarks>
    private static string? NormalizeInput(string? input)
    {
        var trimmed = input?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        if (trimmed.Contains("://", StringComparison.Ordinal))
        {
            return trimmed;
        }

        // 形似域名（含点且不含空格）时补全为 https
        if (trimmed.Contains('.', StringComparison.Ordinal)
            && !trimmed.Contains(' ', StringComparison.Ordinal))
        {
            return "https://" + trimmed;
        }

        return string.Format(SearchUrlFormat, Uri.EscapeDataString(trimmed));
    }

    /// <summary>把地址栏同步为活动标签的当前地址。</summary>
    private void SyncAddressBar()
    {
        if (_activeTab is null)
        {
            return;
        }

        var url = _activeTab.Url;

        if (!string.IsNullOrWhiteSpace(url))
        {
            _addressBox.Text = url;
        }
        else
        {
            _addressBox.Text = string.Empty;
        }
    }

    /// <summary>地址栏回车事件。</summary>
    private void OnAddressBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
        {
            return;
        }

        // 抑制回车音效，否则每次回车都会「叮」一声
        e.SuppressKeyPress = true;
        Navigate(_addressBox.Text);
    }

    /// <summary>刷新前进/后退按钮与加载提示器的状态。</summary>
    private void UpdateNavigationState()
    {
        var core = _activeTab?.IsCoreReady == true ? _activeTab!.WebView.CoreWebView2 : null;

        _backButton.Enabled = core?.CanGoBack == true;
        _forwardButton.Enabled = core?.CanGoForward == true;
        _refreshButton.Enabled = _activeTab is not null && !_loading;
        _stopButton.Enabled = _loading;

        // 加载提示器只在导航进行中可见
        _loadingLabel.Visible = _loading;
        _loadingBar.Visible = _loading;
    }
}

/// <summary>
/// 收藏/取消收藏请求的事件参数。
/// </summary>
/// <remarks>
/// 携带当前按钮的 Checked 状态（实心星 = 已收藏），
/// 主窗体据此决定是执行"添加收藏"还是"取消收藏"。
/// </remarks>
public sealed class FavoriteToggleEventArgs : EventArgs
{
    /// <summary>
    /// 操作前页面是否已处于收藏状态。
    /// </summary>
    /// <value>true 表示当前已收藏，按钮为实心星；此时点击应执行"取消收藏"。</value>
    public bool IsFavorited { get; init; }
}
