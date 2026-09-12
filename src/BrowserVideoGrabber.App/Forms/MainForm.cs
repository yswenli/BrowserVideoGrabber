/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.App.Forms
*文件名： MainForm
*版本号： V1.0.0.0
*唯一标识：364916fd-8b2c-4d19-a7f4-2e5c9b1d6a38
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 02:52:00
*描述：主窗体，按「左浏览器 + 右上嗅探 + 右下下载」的三区布局装配全部面板并串联业务动作。
*
*=================================================
*修改标记
*修改时间：2026/9/13 02:52:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Diagnostics;
using System.Runtime.InteropServices;
using BrowserVideoGrabber.App.Binding;
using BrowserVideoGrabber.App.Dialogs;
using BrowserVideoGrabber.App.Panes;
using BrowserVideoGrabber.Core.Abstractions;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.App.Forms;

/// <summary>
/// 主窗体。
/// </summary>
/// <remarks>
/// <para>
/// 布局由两级 <see cref="SplitContainer"/> 构成：外层左右切分（浏览器 / 功能面板），
/// 右侧再上下切分（嗅探 / 下载）。相比固定面板，拆分条让用户可以按自己的习惯分配空间，
/// 例如抓长播放列表时把嗅探面板拉大。
/// </para>
/// <para>
/// <b>职责边界</b>：本窗体是唯一把「用户意图」翻译成「队列操作」的地方。
/// 三个面板都只抛意图（<see cref="DownloadAction"/>、<see cref="SniffPane.DownloadRequested"/>），
/// 不直接触碰队列；绑定器只负责同步数据，不参与决策。
/// </para>
/// </remarks>
public sealed class MainForm : Form
{
    private const string DefaultHomeUrl = "https://www.bing.com";

    private readonly AppHost _host;
    private readonly BrowserPane _browserPane = new() { Dock = DockStyle.Fill };
    private readonly SniffPane _sniffPane = new() { Dock = DockStyle.Fill };
    private readonly DownloadPane _downloadPane = new() { Dock = DockStyle.Fill };

    private readonly SplitContainer _outerSplit;
    private readonly SplitContainer _innerSplit;
    private readonly ToolStripStatusLabel _statusLabel;
    private readonly ToolStripStatusLabel _ffmpegLabel;
    private readonly Panel _warningBanner;
    private readonly Label _warningLabel;

    private SniffListBinder? _sniffBinder;
    private DownloadListBinder? _downloadBinder;
    private IVideoSniffer? _sniffer;
    private IRequestContextProvider? _contextProvider;
    private bool _closingHandled;

    /// <summary>
    /// 初始化主窗体。
    /// </summary>
    /// <param name="host">应用宿主。</param>
    public MainForm(AppHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));

        Text = "BrowserVideoGrabber · 页面视频嗅探下载器";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1440, 900);
        MinimumSize = new Size(1024, 680);
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Font;

        // 刻意不在初始化器里设置 Panel1MinSize / Panel2MinSize：
        // 此刻控件宽度仍是默认的 150，而 WinForms 在设置最小尺寸时会连带校验
        // SplitterDistance 是否落在 [Panel1MinSize, Width - Panel2MinSize - SplitterWidth] 内，
        // 该区间此时为空集，必然抛出 InvalidOperationException。
        // 约束因此统一延后到窗体载入、控件拿到真实尺寸后由 ApplyInitialSplitterDistances 施加。
        _outerSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6
        };

        _innerSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 6
        };

        _outerSplit.Panel1.Controls.Add(_browserPane);
        _innerSplit.Panel1.Controls.Add(_sniffPane);
        _innerSplit.Panel2.Controls.Add(_downloadPane);
        _outerSplit.Panel2.Controls.Add(_innerSplit);

        _statusLabel = new ToolStripStatusLabel("就绪") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _ffmpegLabel = new ToolStripStatusLabel("ffmpeg：检测中…");

        var statusStrip = new StatusStrip { SizingGrip = true };
        statusStrip.Items.AddRange([_statusLabel, _ffmpegLabel]);

        var mainToolStrip = BuildMainToolStrip();

        _warningLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(150, 60, 20),
            Padding = new Padding(12, 0, 0, 0),
            Text = "未检测到 ffmpeg：M3U8 / TS / M4S / DASH 等分片格式将无法下载（MP4 直链不受影响）。" +
                   "请在「设置」中指定 ffmpeg.exe 路径，或将其所在目录加入系统 PATH。"
        };

        var warningSettingsButton = new Button
        {
            Text = "去设置…",
            Dock = DockStyle.Right,
            Width = 96,
            FlatStyle = FlatStyle.System
        };
        warningSettingsButton.Click += OnSettingsClick;

        _warningBanner = new Panel
        {
            Dock = DockStyle.Top,
            Height = 40,
            BackColor = Color.FromArgb(255, 244, 229),
            Visible = false
        };
        _warningBanner.Controls.Add(_warningLabel);
        _warningBanner.Controls.Add(warningSettingsButton);

        // 停靠顺序即层级顺序：先加填充控件，再加边缘控件；
        // 最后加入的 Top 控件最贴近窗体上边缘（警告横幅须位于工具栏之上）
        Controls.Add(_outerSplit);
        Controls.Add(mainToolStrip);
        Controls.Add(statusStrip);
        Controls.Add(_warningBanner);

        WireEvents();

        _downloadBinder = new DownloadListBinder(_host, _downloadPane);
        _downloadBinder.RefreshAll();

        Load += OnFormLoadAsync;
        FormClosing += OnFormClosingHandler;
    }

    /// <summary>
    /// 构建主工具栏。
    /// </summary>
    /// <returns>工具栏控件。</returns>
    private ToolStrip BuildMainToolStrip()
    {
        var settingsButton = new ToolStripButton("设置")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = "配置 ffmpeg 路径、输出目录、并发数与分片数"
        };
        settingsButton.Click += OnSettingsClick;

        var openOutputButton = new ToolStripButton("打开输出目录")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = "在资源管理器中打开下载文件的保存目录"
        };
        openOutputButton.Click += OnOpenOutputDirectoryClick;

        var openTaskFileButton = new ToolStripButton("打开任务文件")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = "打开 tasks.json 所在目录，便于排查任务持久化问题"
        };
        openTaskFileButton.Click += OnOpenSettingsDirectoryClick;

        var toolbar = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.System,
            Dock = DockStyle.Top
        };

        toolbar.Items.AddRange(
        [
            settingsButton,
            openOutputButton,
            openTaskFileButton
        ]);

        return toolbar;
    }

    /// <summary>
    /// 订阅各面板与宿主的事件。
    /// </summary>
    private void WireEvents()
    {
        _browserPane.SniffToggled += OnSniffToggled;
        _browserPane.MessageReported += (_, message) => SetStatus(message);
        _browserPane.AddressChanged += (_, url) => SetStatus($"已打开：{url}");

        _sniffPane.DownloadRequested += OnDownloadRequested;

        // 清空列表必须同时清掉嗅探器的去重记录，否则「清空后可以重新捕获」不成立
        _sniffPane.ClearRequested += OnSniffClearRequested;

        _downloadPane.ActionRequested += OnDownloadAction;
    }

    /// <summary>
    /// 处理嗅探面板的清空请求。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnSniffClearRequested(object? sender, EventArgs e)
    {
        _sniffer?.Clear();
        SetStatus("已清空嗅探列表。");
    }

    /// <summary>
    /// 窗体载入：设置拆分条位置、初始化浏览器、装配嗅探器并恢复历史任务。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    /// <remarks>事件处理器必须是 async void，因此整体以 try/catch 包裹，避免异常逃逸导致进程崩溃。</remarks>
    private async void OnFormLoadAsync(object? sender, EventArgs e)
    {
        try
        {
            ApplyInitialSplitterDistances();
            UpdateFfmpegStatus();

            await InitializeBrowserAsync();

            _browserPane.SetSniffEnabledWithoutNotify(_host.Settings.SniffEnabled);

            await _host.RestoreAsync().ConfigureAwait(true);
            _downloadBinder?.RefreshAll();

            ApplySniffEnabled(_host.Settings.SniffEnabled);

            SetStatus("就绪。输入网址后浏览页面，右上角将自动列出检测到的视频资源。");
        }
        catch (Exception exception)
        {
            StartupDiagnostics.Write("主窗体初始化失败", exception);
            SetStatus($"初始化失败：{exception.Message}");
            MessageBox.Show(
                this,
                $"{exception.Message}{Environment.NewLine}{Environment.NewLine}详细信息已记录到：{Environment.NewLine}{StartupDiagnostics.LogFilePath}",
                "初始化失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 初始化浏览器并装配嗅探器。
    /// </summary>
    /// <returns>表示异步初始化的任务。</returns>
    private async Task InitializeBrowserAsync()
    {
        var initialUrl = string.IsNullOrWhiteSpace(_host.Settings.LastUrl)
            ? DefaultHomeUrl
            : _host.Settings.LastUrl;

        await _browserPane.InitializeAsync(initialUrl).ConfigureAwait(true);

        _sniffer = _host.CreateSniffer(_browserPane.WebView);
        _contextProvider = _host.CreateContextProvider(_browserPane.WebView);
        _sniffBinder = new SniffListBinder(_sniffer, _sniffPane);
    }

    /// <summary>
    /// 设置拆分条的初始比例与最小尺寸。
    /// </summary>
    /// <remarks>
    /// 必须等控件拥有真实尺寸后再设置：在构造函数里设置会因宽度仍为默认值而触发
    /// 「SplitterDistance 超出有效范围」的异常。
    /// </remarks>
    private void ApplyInitialSplitterDistances()
    {
        // 左浏览器占约 62%，右侧功能面板占 38%
        PlaceSplitter(_outerSplit, 0.62, panel1MinSize: 360, panel2MinSize: 360);

        // 右上嗅探面板占约 42%，右下下载列表占 58%
        PlaceSplitter(_innerSplit, 0.42, panel1MinSize: 140, panel2MinSize: 180);
    }

    /// <summary>
    /// 为拆分条设定比例与最小尺寸。
    /// </summary>
    /// <param name="split">目标拆分容器。</param>
    /// <param name="panel1Ratio">第一面板占比（0~1）。</param>
    /// <param name="panel1MinSize">第一面板最小尺寸。</param>
    /// <param name="panel2MinSize">第二面板最小尺寸。</param>
    /// <remarks>
    /// 赋值顺序是本方法的关键：<b>先解除最小尺寸 → 再设位置 → 最后施加最小尺寸</b>。
    /// 反过来做会在「旧约束拒绝新位置」时抛异常，这是 WinForms 拆分容器最容易踩的坑。
    /// </remarks>
    private static void PlaceSplitter(SplitContainer split, double panel1Ratio, int panel1MinSize, int panel2MinSize)
    {
        var total = split.Orientation == Orientation.Vertical ? split.Width : split.Height;

        split.Panel1MinSize = 0;
        split.Panel2MinSize = 0;

        var upperBound = total - panel2MinSize - split.SplitterWidth;
        if (upperBound <= panel1MinSize)
        {
            // 控件尺寸过小，无法同时满足两侧最小尺寸：放弃约束，交给用户自行拖动
            return;
        }

        split.SplitterDistance = Math.Clamp((int)(total * panel1Ratio), panel1MinSize, upperBound);

        // 位置已落地，此时施加最小尺寸必然通过校验
        split.Panel1MinSize = panel1MinSize;
        split.Panel2MinSize = panel2MinSize;
    }

    /// <summary>
    /// 刷新 ffmpeg 状态标签与缺失警告横幅。
    /// </summary>
    private void UpdateFfmpegStatus()
    {
        var path = _host.FfmpegLocator.Locate();

        if (string.IsNullOrWhiteSpace(path))
        {
            _ffmpegLabel.Text = "ffmpeg：未找到";
            _ffmpegLabel.ForeColor = Color.FromArgb(200, 60, 40);
            _warningBanner.Visible = true;
            return;
        }

        _ffmpegLabel.Text = $"ffmpeg：{path}";
        _ffmpegLabel.ForeColor = Color.FromArgb(30, 120, 60);
        _warningBanner.Visible = false;
    }

    /// <summary>
    /// 按开关状态启动或停止嗅探。
    /// </summary>
    /// <param name="enabled">是否开启。</param>
    private void ApplySniffEnabled(bool enabled)
    {
        if (_sniffer is null)
        {
            return;
        }

        if (enabled)
        {
            _sniffer.Start();
        }
        else
        {
            _sniffer.Stop();
        }
    }

    /// <summary>嗅探开关切换。</summary>
    private void OnSniffToggled(object? sender, bool enabled)
    {
        ApplySniffEnabled(enabled);
        SetStatus(enabled ? "已开启页面视频嗅探。" : "已关闭页面视频嗅探（已捕获的结果保留）。");
    }

    /// <summary>
    /// 把嗅探到的资源加入下载队列。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="video">嗅探结果。</param>
    private async void OnDownloadRequested(object? sender, SniffedVideo video)
    {
        try
        {
            if (!ConfirmFragmentDownload(video))
            {
                SetStatus("已取消下载该分片地址。");
                return;
            }

            var outputPath = _host.BuildOutputPath(video);

            // 入队时冻结浏览器会话的鉴权信息：下载可能在其后才真正开始，
            // 届时用户可能已切换页面，再取 Referer 就不准了
            var context = _contextProvider is null
                ? RequestContext.CreateDefault(_host.Settings.UserAgent)
                : await _contextProvider.CreateAsync(video.Url).ConfigureAwait(true);

            var task = new DownloadTask
            {
                Url = video.Url,
                Format = video.Format,
                OutputPath = outputPath,
                Title = video.DisplayTitle,
                Context = context
            };

            _host.Queue.Enqueue(task);
            _host.Queue.Start();

            SetStatus($"已加入下载：{Path.GetFileName(outputPath)}");
        }
        catch (Exception exception)
        {
            SetStatus($"加入下载失败：{exception.Message}");
            MessageBox.Show(
                this,
                $"加入下载失败：{exception.Message}",
                "下载",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// 确认是否下载一个分片条目。
    /// </summary>
    /// <param name="video">嗅探结果。</param>
    /// <returns>用户确认继续下载返回 true；其余格式无需确认，直接返回 true。</returns>
    /// <remarks>
    /// 分片条目只有在「同一个视频的清单始终没被捕获」时才会出现在列表里（例如嗅探开关是在播放中途才打开的）。
    /// 这种情况下最容易发生的事就是用户选中一个 <c>.ts</c> 下载，得到一个只有几秒钟的「视频」，
    /// 却以为工具把完整视频下载坏了。与其静默产出残缺文件，不如先把话说清楚。
    /// </remarks>
    private bool ConfirmFragmentDownload(SniffedVideo video)
    {
        if (video.Format is not (VideoFormat.Ts or VideoFormat.M4s))
        {
            return true;
        }

        var answer = MessageBox.Show(
            this,
            $"当前选中的是一个视频分片，而不是完整视频：{Environment.NewLine}" +
            $"{video.Url}{Environment.NewLine}{Environment.NewLine}" +
            "单独下载分片只能得到很短的片段。若列表中还有同一个视频的播放列表条目" +
            "（格式显示为 M3U8 或 DASH 索引），请改选那一条，才能下载完整视频。" +
            $"{Environment.NewLine}{Environment.NewLine}" +
            "提示：如果在页面开始播放之后才打开嗅探开关，可能会漏掉播放列表。" +
            "重新加载页面并让其开始播放，通常就能抓到。",
            "该地址是视频分片",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        return answer == DialogResult.OK;
    }

    /// <summary>
    /// 处理下载列表上抛的操作意图。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnDownloadAction(object? sender, DownloadActionEventArgs e)
    {
        switch (e.Action)
        {
            case DownloadAction.Pause:
                _host.Queue.Pause(e.Task!.Id);
                SetStatus($"已暂停：{e.Task.Title}");
                break;

            case DownloadAction.Resume:
                _host.Queue.Resume(e.Task!.Id);
                SetStatus($"已恢复：{e.Task.Title}");
                break;

            case DownloadAction.Cancel:
                _host.Queue.Cancel(e.Task!.Id);
                SetStatus($"已取消：{e.Task.Title}");
                break;

            case DownloadAction.Retry:
                RetryTask(e.Task!);
                break;

            case DownloadAction.Remove:
                RemoveTask(e.Task!);
                break;

            case DownloadAction.OpenFile:
                OpenOutputFile(e.Task!);
                break;

            case DownloadAction.OpenFolder:
                OpenOutputFolder(e.Task!);
                break;

            case DownloadAction.CopyUrl:
                CopyTaskUrl(e.Task!);
                break;

            case DownloadAction.ClearFinished:
                var removed = _host.Queue.ClearFinished();
                _downloadBinder?.RefreshAll();

                // 同 RemoveTask：清空同样不触发事件，需显式安排落盘
                _host.RequestPersist();
                SetStatus($"已清空 {removed} 个已结束的任务。");
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// 重试一个已失败或已取消的任务。
    /// </summary>
    /// <param name="task">原任务。</param>
    /// <remarks>
    /// 队列没有「原地复活终态任务」的能力，因此这里移除旧任务后以同源信息新建一个，
    /// 这样界面上看到的是「失败行被新的下载行取代」，而不是凭空多出一行造成困惑。
    /// </remarks>
    private void RetryTask(DownloadTask task)
    {
        var retry = new DownloadTask
        {
            Url = task.Url,
            Format = task.Format,
            OutputPath = task.OutputPath,
            Title = task.Title,
            Context = task.Context.Clone()
        };

        if (_host.Queue.Remove(task.Id))
        {
            _downloadBinder?.RemoveTask(task.Id);
        }

        _host.Queue.Enqueue(retry);
        _host.Queue.Start();

        SetStatus($"已重新加入下载：{task.Title}");
    }

    /// <summary>
    /// 从队列与列表中移除一个任务。
    /// </summary>
    /// <param name="task">目标任务。</param>
    private void RemoveTask(DownloadTask task)
    {
        // 队列的 Remove 不触发事件（任务已消失，无从通知），界面需显式同步
        if (!_host.Queue.Remove(task.Id))
        {
            // 唯一会失败的情形是任务正在下载：此时文件仍在增长，必须先取消才能安全移除
            SetStatus($"「{task.Title}」正在下载，请先取消再移除。");
            MessageBox.Show(
                this,
                $"「{task.Title}」正在下载中，无法直接移除。\n\n请先执行「取消」，等下载停止后再把它从列表移除。",
                "从列表移除",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _downloadBinder?.RemoveTask(task.Id);

        // 移除不触发状态变更事件，必须显式安排落盘，
        // 否则这条任务会留在 tasks.json 里，下次启动又原样出现，看起来像「删不掉」
        _host.RequestPersist();

        SetStatus($"已从列表移除：{task.Title}");
    }

    /// <summary>打开已下载的文件。</summary>
    private void OpenOutputFile(DownloadTask task)
    {
        if (!File.Exists(task.OutputPath))
        {
            MessageBox.Show(
                this,
                $"文件不存在，可能已被移动或删除：\n{task.OutputPath}",
                "打开文件",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        StartShellTarget(task.OutputPath);
    }

    /// <summary>在资源管理器中定位已下载的文件。</summary>
    private void OpenOutputFolder(DownloadTask task)
    {
        var directory = Path.GetDirectoryName(task.OutputPath);

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            MessageBox.Show(
                this,
                $"所在文件夹不存在：\n{directory}",
                "打开文件夹",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        StartShellTarget(directory);
    }

    /// <summary>复制任务地址到剪贴板。</summary>
    private void CopyTaskUrl(DownloadTask task)
    {
        try
        {
            Clipboard.SetText(task.Url);
            SetStatus("下载地址已复制到剪贴板。");
        }
        catch (ExternalException)
        {
            // 剪贴板被其它进程占用，属于偶发情况
            SetStatus("复制失败：剪贴板被其它程序占用。");
        }
    }

    /// <summary>
    /// 用系统默认程序打开路径或目录。
    /// </summary>
    /// <param name="target">文件或目录路径。</param>
    private void StartShellTarget(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            SetStatus($"打开失败：{exception.Message}");
        }
    }

    /// <summary>打开设置对话框。</summary>
    private void OnSettingsClick(object? sender, EventArgs e)
    {
        using var dialog = new SettingsForm(_host.Settings, () => _host.DetectFfmpeg(null));

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _host.ApplySettings(dialog.Result);

        // 设置变更会重建整条下载管线，绑定器需要按新队列重新同步一次界面
        _downloadBinder?.RefreshAll();
        UpdateFfmpegStatus();

        // 请求上下文提供者持有设置快照，重建后新的 User-Agent 才会生效
        if (_browserPane.IsCoreReady)
        {
            _contextProvider = _host.CreateContextProvider(_browserPane.WebView);
        }

        SetStatus("设置已保存并立即生效。");
    }

    /// <summary>打开输出目录。</summary>
    private void OnOpenOutputDirectoryClick(object? sender, EventArgs e)
        => StartShellTarget(_host.ResolveOutputDirectory());

    /// <summary>打开设置与任务文件所在目录。</summary>
    private void OnOpenSettingsDirectoryClick(object? sender, EventArgs e)
    {
        var directory = Path.GetDirectoryName(_host.SettingsStore.FilePath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            SetStatus("无法定位设置文件目录。");
            return;
        }

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        StartShellTarget(directory);
    }

    /// <summary>
    /// 窗体关闭：记忆最后访问的地址、落盘、释放监听资源。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnFormClosingHandler(object? sender, FormClosingEventArgs e)
    {
        if (_closingHandled)
        {
            return;
        }

        _closingHandled = true;

        try
        {
            _host.RememberLastUrl(_browserPane.CurrentUrl);

            // 先停掉界面侧的刷新，再让队列停摆，避免关闭过程中还在往已销毁的控件投递刷新
            _downloadBinder?.FlushProgress();
            _downloadBinder?.Dispose();
            _downloadBinder = null;

            _sniffBinder?.Dispose();
            _sniffBinder = null;

            // 嗅探器持有 WebView2 事件订阅，必须先于控件释放
            if (_sniffer is IDisposable disposableSniffer)
            {
                disposableSniffer.Dispose();
            }

            _sniffer = null;

            _host.SaveNow();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            // 退出路径上的清理异常不应阻止关闭
        }
    }

    private void InitializeComponent()
    {

    }

    /// <summary>
    /// 更新状态栏文本。
    /// </summary>
    /// <param name="message">待显示的文本。</param>
    private void SetStatus(string message) => _statusLabel.Text = message;
}
