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
*描述：左侧浏览器面板，由导航工具栏与 WebView2 组成。
*
*=================================================
*修改标记
*修改时间：2026/9/13 01:20:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BrowserVideoGrabber.App.Panes;

/// <summary>
/// 浏览器面板：地址栏 + 后退/前进/刷新 + 嗅探开关 + WebView2。
/// </summary>
/// <remarks>
/// <para>
/// 本面板只负责「浏览器」这一件事，嗅探器与下载器均通过 <see cref="WebView"/> 暴露出去后
/// 由上层装配，因此面板本身对嗅探、下载零依赖。
/// </para>
/// <para>
/// <b>用户数据目录</b>：显式指向 <c>%LOCALAPPDATA%\BrowserVideoGrabber\WebView2</c>。
/// 若沿用默认位置（可执行文件同级目录），当程序被放在 Program Files 等只读目录时
/// WebView2 将无法创建缓存而直接初始化失败。
/// </para>
/// <para>
/// <b>登录态复用</b>：用户在此面板中登录站点后，会话 Cookie 保存在该用户数据目录中，
/// 后续下载请求由 <c>WebView2RequestContextProvider</c> 从同一会话导出，从而能下载需登录的内容。
/// </para>
/// </remarks>
public sealed class BrowserPane : UserControl
{
    private const string SearchUrlFormat = "https://www.bing.com/search?q={0}";

    private readonly ToolStrip _toolStrip = new()
    {
        GripStyle = ToolStripGripStyle.Hidden,
        RenderMode = ToolStripRenderMode.System,
        Dock = DockStyle.Top
    };

    private readonly ToolStripButton _backButton;
    private readonly ToolStripButton _forwardButton;
    private readonly ToolStripButton _refreshButton;
    private readonly ToolStripButton _stopButton;
    private readonly ToolStripButton _goButton;
    private readonly ToolStripButton _sniffButton;
    private readonly ToolStripTextBox _addressBox;
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };

    private bool _coreReady;
    private bool _loading;

    /// <summary>
    /// 初始化浏览器面板。
    /// </summary>
    public BrowserPane()
    {
        BackColor = Color.FromArgb(250, 250, 250);

        _backButton = CreateButton("←", "后退", (_, _) => NavigateBack());
        _forwardButton = CreateButton("→", "前进", (_, _) => NavigateForward());
        _refreshButton = CreateButton("刷新", "重新加载当前页面", (_, _) => _webView.Reload());
        _stopButton = CreateButton("停止", "停止加载", (_, _) => _webView.Stop());

        _addressBox = new ToolStripTextBox
        {
            AutoSize = false,
            Width = 420,
            ToolTipText = "输入网址后回车；也可直接输入关键词进行搜索"
        };
        _addressBox.KeyDown += OnAddressBoxKeyDown;

        _goButton = CreateButton("转到", "打开地址栏中的地址", (_, _) => Navigate(_addressBox.Text));

        _sniffButton = CreateButton("嗅探：开", "开启或关闭页面视频嗅探", OnSniffButtonClick);
        _sniffButton.CheckOnClick = true;
        _sniffButton.Checked = true;

        _toolStrip.Items.AddRange(
        [
            _backButton,
            _forwardButton,
            _refreshButton,
            _stopButton,
            new ToolStripSeparator(),
            _addressBox,
            _goButton,
            new ToolStripSeparator(),
            _sniffButton
        ]);

        Controls.Add(_webView);
        Controls.Add(_toolStrip);

        _toolStrip.Dock = DockStyle.Top;
        _webView.Dock = DockStyle.Fill;

        UpdateNavigationState();
    }

    /// <summary>底层的 WebView2 控件，供上层装配嗅探器与请求上下文提供者。</summary>
    public WebView2 WebView => _webView;

    /// <summary>WebView2 核心对象是否已初始化完成。</summary>
    public bool IsCoreReady => _coreReady;

    /// <summary>当前页面地址；未初始化时为空字符串。</summary>
    public string CurrentUrl => _coreReady ? _webView.Source?.ToString() ?? string.Empty : string.Empty;

    /// <summary>嗅探开关当前是否处于开启状态。</summary>
    public bool SniffEnabled => _sniffButton.Checked;

    /// <summary>嗅探开关被切换时触发。</summary>
    public event EventHandler<bool>? SniffToggled;

    /// <summary>需要向用户提示一条消息时触发（如导航失败）。</summary>
    public event EventHandler<string>? MessageReported;

    /// <summary>地址栏需要同步为当前页面地址时触发。</summary>
    public event EventHandler<string>? AddressChanged;

    /// <summary>
    /// 初始化 WebView2 环境。
    /// </summary>
    /// <param name="initialUrl">启动后自动打开的地址；为空则不导航。</param>
    /// <returns>表示异步初始化的任务。</returns>
    /// <exception cref="InvalidOperationException">WebView2 运行时缺失导致初始化失败时抛出。</exception>
    public async Task InitializeAsync(string? initialUrl)
    {
        if (_coreReady)
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
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder)
                .ConfigureAwait(true);

            await _webView.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is WebView2RuntimeNotFoundException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                "WebView2 运行时初始化失败。请先安装 Microsoft Edge WebView2 Runtime（Windows 10/11 通常已内置）。",
                exception);
        }

        var core = _webView.CoreWebView2;
        if (core is null)
        {
            throw new InvalidOperationException("WebView2 核心对象创建失败。");
        }

        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.IsStatusBarEnabled = false;

        core.NavigationStarting += OnNavigationStarting;
        core.NavigationCompleted += OnNavigationCompleted;
        core.SourceChanged += OnSourceChanged;

        // 站内弹窗一律在当前视图打开，避免用户在新窗口里迷路导致嗅探面板收不到资源
        core.NewWindowRequested += OnNewWindowRequested;

        _coreReady = true;

        if (!string.IsNullOrWhiteSpace(initialUrl))
        {
            Navigate(initialUrl);
        }
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

        if (!_coreReady)
        {
            // 尚未初始化完成时先记下地址，初始化结束后由上层再次调用
            _addressBox.Text = target;
            return;
        }

        try
        {
            _webView.CoreWebView2.Navigate(target);
        }
        catch (ArgumentException)
        {
            MessageReported?.Invoke(this, $"无法识别的地址：{target}");
        }
    }

    /// <summary>后退。</summary>
    public void NavigateBack()
    {
        if (_coreReady && _webView.CoreWebView2.CanGoBack)
        {
            _webView.CoreWebView2.GoBack();
        }
    }

    /// <summary>前进。</summary>
    public void NavigateForward()
    {
        if (_coreReady && _webView.CoreWebView2.CanGoForward)
        {
            _webView.CoreWebView2.GoForward();
        }
    }

    /// <summary>
    /// 把嗅探开关同步到指定状态而不触发事件（用于按设置初始化）。
    /// </summary>
    /// <param name="enabled">是否开启。</param>
    public void SetSniffEnabledWithoutNotify(bool enabled)
    {
        _sniffButton.Checked = enabled;
        _sniffButton.Text = enabled ? "嗅探：开" : "嗅探：关";
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // WebView2 持有浏览器进程与大量非托管资源，必须显式释放，
            // 否则关闭主窗体后仍会残留若干 msedgewebview2.exe 进程
            _webView.Dispose();
            _toolStrip.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// 创建工具栏按钮。
    /// </summary>
    /// <param name="text">按钮文本。</param>
    /// <param name="tooltip">提示文本。</param>
    /// <param name="onClick">点击回调。</param>
    /// <returns>按钮实例。</returns>
    private static ToolStripButton CreateButton(string text, string tooltip, EventHandler onClick)
    {
        var button = new ToolStripButton(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = tooltip,
            AutoToolTip = false
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

    /// <summary>嗅探开关点击事件。</summary>
    private void OnSniffButtonClick(object? sender, EventArgs e)
    {
        _sniffButton.Text = _sniffButton.Checked ? "嗅探：开" : "嗅探：关";
        SniffToggled?.Invoke(this, _sniffButton.Checked);
    }

    /// <summary>导航开始：同步地址栏并进入加载态。</summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        _loading = true;
        _addressBox.Text = e.Uri;
        UpdateNavigationState();
    }

    /// <summary>导航完成：退出加载态并在失败时提示。</summary>
    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        _loading = false;
        UpdateNavigationState();

        if (!e.IsSuccess)
        {
            // 用户主动停止（OperationCanceled）不算错误，不必打扰
            if (e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
            {
                MessageReported?.Invoke(this, $"页面加载失败：{e.WebErrorStatus}");
            }
        }
    }

    /// <summary>源地址变化：同步地址栏。</summary>
    private void OnSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        var url = _webView.Source?.ToString();
        if (!string.IsNullOrWhiteSpace(url))
        {
            _addressBox.Text = url;
            AddressChanged?.Invoke(this, url);
        }
    }

    /// <summary>新窗口请求：改在当前视图打开。</summary>
    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;

        if (!string.IsNullOrWhiteSpace(e.Uri))
        {
            Navigate(e.Uri);
        }
    }

    /// <summary>刷新前进/后退按钮的可用状态。</summary>
    private void UpdateNavigationState()
    {
        var core = _coreReady ? _webView.CoreWebView2 : null;

        _backButton.Enabled = core?.CanGoBack == true;
        _forwardButton.Enabled = core?.CanGoForward == true;
        _refreshButton.Enabled = !_loading;
        _stopButton.Enabled = _loading;
    }
}
