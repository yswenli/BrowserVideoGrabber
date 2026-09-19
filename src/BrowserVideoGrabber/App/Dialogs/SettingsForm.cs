/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.App.Dialogs
*文件名： SettingsForm
*版本号： V1.0.0.0
*唯一标识：7e4a9d63-2c5f-4b18-a6e3-8d1b4f7c2a09
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 02:30:00
*描述：设置对话框，供用户配置 ffmpeg 路径、输出目录、并发数与分片数等运行参数。
*
*=================================================
*修改标记
*修改时间：2026/9/13 02:30:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.App;
using BrowserVideoGrabber.Core.Configuration;

namespace BrowserVideoGrabber.App.Dialogs;

/// <summary>
/// 设置对话框。
/// </summary>
/// <remarks>
/// <para>
/// 本窗体是纯视图 + 输入校验，不直接读写设置文件：读写由调用方（主窗体）通过
/// <c>AppHost.ApplySettings</c> 完成。这样窗体可以在测试中独立构造，
/// 也避免「界面组件偷偷写盘」这种难以追踪的副作用。
/// </para>
/// <para>
/// ffmpeg 路径留空即表示「自动探测」，这也是推荐用法 ——
/// 探测顺序覆盖了应用目录、PATH 与常见安装位置，手工指定只在探测失败时才需要。
/// </para>
/// </remarks>
public sealed class SettingsForm : Form
{
    private const int LabelLeft = 16;
    private const int InputLeft = 124;
    private const int InputWidth = 410;
    private const int ButtonLeft = 542;
    private const int ButtonWidth = 90;

    private readonly Func<string?> _detectFfmpeg;

    private readonly TextBox _ffmpegPathBox;
    private readonly TextBox _outputDirectoryBox;
    private readonly NumericUpDown _concurrencyBox;
    private readonly NumericUpDown _segmentBox;
    private readonly TextBox _userAgentBox;
    private readonly CheckBox _restoreTabsBox;
    private readonly NumericUpDown _maxTabsBox;
    private readonly NumericUpDown _maxHistoryBox;
    private readonly Label _detectionLabel;

    /// <summary>
    /// 初始化设置对话框。
    /// </summary>
    /// <param name="settings">当前设置，用于初始化各输入框。</param>
    /// <param name="detectFfmpeg">执行 ffmpeg 自动探测的委托，返回探测到的路径或 null。</param>
    public SettingsForm(AppSettings settings, Func<string?> detectFfmpeg)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _detectFfmpeg = detectFfmpeg ?? throw new ArgumentNullException(nameof(detectFfmpeg));

        Text = "设置";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(650, 470);
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Font;

        // 统一窗体图标：与主控窗体保持一致，避免设置对话框出现不同的任务栏图标
        Icon = AppIcon.Load();

        _ffmpegPathBox = CreateTextBox(18, settings.FfmpegPath);
        var browseFfmpegButton = CreateButton("浏览…", 18, OnBrowseFfmpegClick);

        _detectionLabel = new Label
        {
            Left = InputLeft,
            Top = 52,
            Width = InputWidth,
            Height = 20,
            ForeColor = Color.FromArgb(110, 110, 110),
            Text = "留空表示自动探测（应用目录 → 系统 PATH → 常见安装位置）。"
        };

        var detectButton = CreateButton("自动探测", 52, OnDetectClick);

        _outputDirectoryBox = CreateTextBox(86, settings.OutputDirectory);
        var browseOutputButton = CreateButton("浏览…", 86, OnBrowseOutputClick);

        _concurrencyBox = CreateNumeric(120, 1, 10, settings.MaxConcurrency);
        var concurrencyHint = CreateHint("同时下载的任务数。过高可能触发站点限流，建议 2~3。", 120);

        _segmentBox = CreateNumeric(154, 1, 16, settings.HttpSegmentCount);
        var segmentHint = CreateHint("MP4 多线程下载的分片数。设为 1 表示单连接下载。", 154);

        _userAgentBox = CreateTextBox(188, settings.UserAgent);
        var userAgentHint = CreateHint("留空则沿用内置浏览器当前的 User-Agent。", 220);

        _restoreTabsBox = new CheckBox
        {
            Left = InputLeft,
            Top = 252,
            Width = InputWidth,
            Height = 24,
            Text = "启动时恢复上次关闭时的标签页",
            Checked = settings.RestoreTabs
        };

        _maxTabsBox = CreateNumeric(310, 1, 20, settings.MaxTabs);
        var maxTabsHint = CreateHint("同时打开的标签页上限。每个标签都会占用内存。", 310);

        _maxHistoryBox = CreateNumeric(344, 50, 5000, settings.MaxHistoryEntries);
        var maxHistoryHint = CreateHint("历史记录保留条数，超出后淘汰最旧。", 344);

        var okButton = new Button
        {
            Text = "确定",
            Left = ClientSize.Width - 200,
            Top = ClientSize.Height - 40,
            Width = 84,
            Height = 28,
            DialogResult = DialogResult.None
        };
        okButton.Click += OnConfirmClick;

        var cancelButton = new Button
        {
            Text = "取消",
            Left = ClientSize.Width - 104,
            Top = ClientSize.Height - 40,
            Width = 84,
            Height = 28,
            DialogResult = DialogResult.Cancel
        };

        Controls.AddRange(
        [
            CreateLabel("ffmpeg 路径", 21),
            _ffmpegPathBox,
            browseFfmpegButton,
            _detectionLabel,
            detectButton,
            CreateLabel("输出目录", 89),
            _outputDirectoryBox,
            browseOutputButton,
            CreateLabel("并发下载数", 123),
            _concurrencyBox,
            concurrencyHint,
            CreateLabel("MP4 分片数", 157),
            _segmentBox,
            segmentHint,
            CreateLabel("User-Agent", 191),
            _userAgentBox,
            userAgentHint,
            _restoreTabsBox,
            CreateLabel("标签页上限", 313),
            _maxTabsBox,
            maxTabsHint,
            CreateLabel("历史上限", 347),
            _maxHistoryBox,
            maxHistoryHint,
            okButton,
            cancelButton
        ]);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        Result = settings.Clone();
    }

    /// <summary>用户确认后的设置；点击取消时保持为传入的原值。</summary>
    public AppSettings Result { get; private set; }

    /// <summary>
    /// 创建左列标签。
    /// </summary>
    /// <param name="text">标签文本。</param>
    /// <param name="top">纵向位置。</param>
    /// <returns>标签控件。</returns>
    private static Label CreateLabel(string text, int top) => new()
    {
        Text = text,
        Left = LabelLeft,
        Top = top,
        Width = InputLeft - LabelLeft - 8,
        Height = 20,
        TextAlign = ContentAlignment.MiddleLeft
    };

    /// <summary>
    /// 创建提示文字。
    /// </summary>
    /// <param name="text">提示文本。</param>
    /// <param name="top">纵向位置。</param>
    /// <returns>标签控件。</returns>
    private static Label CreateHint(string text, int top) => new()
    {
        Text = text,
        Left = InputLeft + 90,
        Top = top + 3,
        Width = InputWidth - 90,
        Height = 20,
        ForeColor = Color.FromArgb(110, 110, 110)
    };

    /// <summary>
    /// 创建输入框。
    /// </summary>
    /// <param name="top">纵向位置。</param>
    /// <param name="initialValue">初始值。</param>
    /// <returns>输入框控件。</returns>
    private static TextBox CreateTextBox(int top, string? initialValue) => new()
    {
        Left = InputLeft,
        Top = top,
        Width = InputWidth,
        Text = initialValue ?? string.Empty
    };

    /// <summary>
    /// 创建数值输入框。
    /// </summary>
    /// <param name="top">纵向位置。</param>
    /// <param name="minimum">最小值。</param>
    /// <param name="maximum">最大值。</param>
    /// <param name="value">初始值。</param>
    /// <returns>数值输入框控件。</returns>
    private static NumericUpDown CreateNumeric(int top, int minimum, int maximum, int value) => new()
    {
        Left = InputLeft,
        Top = top,
        Width = 80,
        Minimum = minimum,
        Maximum = maximum,
        Value = Math.Clamp(value, minimum, maximum)
    };

    /// <summary>
    /// 创建按钮。
    /// </summary>
    /// <param name="text">按钮文本。</param>
    /// <param name="top">纵向位置。</param>
    /// <param name="onClick">点击回调。</param>
    /// <returns>按钮控件。</returns>
    private static Button CreateButton(string text, int top, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            Left = ButtonLeft,
            Top = top,
            Width = ButtonWidth,
            Height = 26
        };

        button.Click += onClick;
        return button;
    }

    /// <summary>浏览 ffmpeg 可执行文件。</summary>
    private void OnBrowseFfmpegClick(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择 ffmpeg.exe",
            Filter = "ffmpeg 可执行文件|ffmpeg.exe|可执行文件|*.exe|所有文件|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _ffmpegPathBox.Text = dialog.FileName;
            _detectionLabel.Text = "已手动指定 ffmpeg 路径。";
        }
    }

    /// <summary>浏览输出目录。</summary>
    private void OnBrowseOutputClick(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择下载文件的保存目录",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        if (!string.IsNullOrWhiteSpace(_outputDirectoryBox.Text))
        {
            dialog.SelectedPath = _outputDirectoryBox.Text;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _outputDirectoryBox.Text = dialog.SelectedPath;
        }
    }

    /// <summary>执行 ffmpeg 自动探测。</summary>
    private void OnDetectClick(object? sender, EventArgs e)
    {
        string? detected;

        try
        {
            detected = _detectFfmpeg();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            detected = null;
        }

        _detectionLabel.ForeColor = detected is null
            ? Color.FromArgb(180, 90, 40)
            : Color.FromArgb(30, 120, 60);

        _detectionLabel.Text = detected is null
            ? "未检测到 ffmpeg。请下载后加入系统 PATH，或在上方手动指定完整路径。"
            : $"已检测到：{detected}";
    }

    /// <summary>确认保存。</summary>
    private void OnConfirmClick(object? sender, EventArgs e)
    {
        var ffmpegPath = _ffmpegPathBox.Text.Trim();

        // 指定的路径不存在时给出确认机会：允许保存（用户可能想先配好再放文件），但不能静默忽略
        if (ffmpegPath.Length > 0 && !File.Exists(ffmpegPath))
        {
            var choice = MessageBox.Show(
                this,
                $"指定的 ffmpeg 路径不存在：\n{ffmpegPath}\n\n仍要保存吗？",
                "路径不存在",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (choice != DialogResult.Yes)
            {
                return;
            }
        }

        Result = new AppSettings
        {
            FfmpegPath = ffmpegPath.Length == 0 ? null : ffmpegPath,
            OutputDirectory = NullIfEmpty(_outputDirectoryBox.Text),
            MaxConcurrency = (int)_concurrencyBox.Value,
            HttpSegmentCount = (int)_segmentBox.Value,
            UserAgent = NullIfEmpty(_userAgentBox.Text),
            RestoreTabs = _restoreTabsBox.Checked,
            MaxTabs = (int)_maxTabsBox.Value,
            MaxHistoryEntries = (int)_maxHistoryBox.Value
        };

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// 空白字符串归一化为 null，避免设置文件里出现无意义的空串。
    /// </summary>
    /// <param name="value">原始文本。</param>
    /// <returns>去空白后的文本；为空时返回 null。</returns>
    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

