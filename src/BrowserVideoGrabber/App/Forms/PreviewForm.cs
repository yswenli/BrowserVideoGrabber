/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.App.Forms
*文件名： PreviewForm
*版本号： V1.0.0.0
*唯一标识：a7b3c9d2-4e5f-6a7b-8c9d-0e1f2a3b4c5d
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/10/9 20:00:00
*描述：视频预览窗口，使用 WebView2 内嵌 HTML5 视频播放器。
*
*=================================================
*修改标记
*修改时间：2026/10/9 20:00:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BrowserVideoGrabber.App.Forms;

/// <summary>
/// 视频预览窗口。
/// </summary>
/// <remarks>
/// <para>
/// 使用 WebView2 内嵌 HTML5 视频播放器，支持 m3u8（通过 hls.js）和 mp4/ts 原生播放。
/// 提供自定义控制栏：进度条、音量控制、播放/暂停按钮。
/// </para>
/// <para>
/// 窗口默认尺寸为 965x560，最小尺寸为 860x500，不限制最大尺寸。
/// </para>
/// </remarks>
public sealed class PreviewForm : Form
{
    private const int DefaultWidth = 965;
    private const int DefaultHeight = 560;
    private const int MinWidth = 860;
    private const int MinHeight = 500;

    private readonly string _videoUrl;
    private readonly string _title;
    private WebView2? _webView;

    /// <summary>
    /// 初始化预览窗口。
    /// </summary>
    /// <param name="videoUrl">视频地址。</param>
    /// <param name="title">视频标题。</param>
    public PreviewForm(string videoUrl, string title)
    {
        _videoUrl = videoUrl ?? throw new ArgumentNullException(nameof(videoUrl));
        _title = string.IsNullOrWhiteSpace(title) ? "视频预览" : title;

        InitializeComponents();
    }

    /// <summary>
    /// 初始化窗口组件。
    /// </summary>
    private void InitializeComponents()
    {
        Text = $"预览: {_title}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(DefaultWidth, DefaultHeight);
        MinimumSize = new Size(MinWidth, MinHeight);
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        ShowInTaskbar = true;
        Icon = AppIcon.Load();
        BackColor = Color.Black;

        // 创建 WebView2 控件
        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.Black
        };

        _webView.CoreWebView2InitializationCompleted += OnWebViewInitialized;

        Controls.Add(_webView);

        // 异步初始化 WebView2
        InitializeWebViewAsync();
    }

    /// <summary>
    /// 异步初始化 WebView2。
    /// </summary>
    private async void InitializeWebViewAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, null, new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required"
            });

            await _webView!.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"初始化 WebView2 失败：{ex.Message}",
                "预览失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Close();
        }
    }

    /// <summary>
    /// WebView2 初始化完成后加载播放器页面。
    /// </summary>
    private void OnWebViewInitialized(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess || _webView?.CoreWebView2 is null)
        {
            MessageBox.Show(
                this,
                "WebView2 初始化失败，无法播放视频。",
                "预览失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Close();
            return;
        }

        // 禁用右键菜单和开发工具
        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;

        // 加载播放器页面
        var html = BuildPlayerHtml();
        _webView.CoreWebView2.NavigateToString(html);
    }

    /// <summary>
    /// 构建 HTML5 视频播放器页面。
    /// </summary>
    /// <returns>HTML 页面内容。</returns>
    private string BuildPlayerHtml()
    {
        var escapedUrl = EscapeJavaScriptString(_videoUrl);
        var escapedTitle = EscapeJavaScriptString(_title);
        var isHls = _videoUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase);

        return $@"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>预览: {System.Net.WebUtility.HtmlEncode(_title)}</title>
    <style>
        * {{
            margin: 0;
            padding: 0;
            box-sizing: border-box;
        }}
        
        body {{
            background: #000;
            color: #fff;
            font-family: 'Microsoft YaHei UI', sans-serif;
            overflow: hidden;
            height: 100vh;
            display: flex;
            flex-direction: column;
        }}
        
        #video-container {{
            flex: 1;
            min-height: 0;
            display: flex;
            align-items: center;
            justify-content: center;
            background: #000;
            position: relative;
            overflow: hidden;
        }}
        
        #video {{
            width: 100%;
            height: 100%;
            object-fit: contain;
            outline: none;
        }}
        
        #controls {{
            flex-shrink: 0;
            background: linear-gradient(to top, rgba(0,0,0,0.9), rgba(0,0,0,0.7));
            padding: 10px 15px;
            display: flex;
            align-items: center;
            gap: 15px;
            min-height: 44px;
        }}
        
        #progress-container {{
            flex: 1;
            height: 6px;
            background: rgba(255,255,255,0.2);
            border-radius: 3px;
            cursor: pointer;
            position: relative;
        }}
        
        #progress-bar {{
            height: 100%;
            background: #e50914;
            border-radius: 3px;
            width: 0%;
            transition: width 0.1s linear;
        }}
        
        #progress-container:hover #progress-bar {{
            height: 8px;
            margin-top: -1px;
        }}
        
        .btn {{
            background: none;
            border: none;
            color: #fff;
            cursor: pointer;
            padding: 5px;
            font-size: 18px;
            opacity: 0.8;
            transition: opacity 0.2s;
        }}
        
        .btn:hover {{
            opacity: 1;
        }}
        
        #volume-container {{
            display: flex;
            align-items: center;
            gap: 8px;
        }}
        
        #volume-slider {{
            width: 80px;
            height: 4px;
            -webkit-appearance: none;
            background: rgba(255,255,255,0.2);
            border-radius: 2px;
            outline: none;
        }}
        
        #volume-slider::-webkit-slider-thumb {{
            -webkit-appearance: none;
            width: 12px;
            height: 12px;
            background: #fff;
            border-radius: 50%;
            cursor: pointer;
        }}
        
        #time-display {{
            font-size: 12px;
            color: rgba(255,255,255,0.8);
            min-width: 100px;
            text-align: center;
        }}
        
        #title-bar {{
            position: absolute;
            top: 10px;
            left: 15px;
            font-size: 14px;
            color: rgba(255,255,255,0.7);
            text-shadow: 0 1px 3px rgba(0,0,0,0.8);
            z-index: 10;
        }}
        
        #loading {{
            position: absolute;
            top: 50%;
            left: 50%;
            transform: translate(-50%, -50%);
            font-size: 16px;
            color: rgba(255,255,255,0.8);
        }}
        
        .spinner {{
            border: 3px solid rgba(255,255,255,0.2);
            border-top: 3px solid #fff;
            border-radius: 50%;
            width: 30px;
            height: 30px;
            animation: spin 1s linear infinite;
            margin: 0 auto 10px;
        }}
        
        @keyframes spin {{
            0% {{ transform: rotate(0deg); }}
            100% {{ transform: rotate(360deg); }}
        }}
        
        #error {{
            position: absolute;
            top: 50%;
            left: 50%;
            transform: translate(-50%, -50%);
            text-align: center;
            color: #e50914;
            display: none;
        }}
    </style>
</head>
<body>
    <div id=""video-container"">
        <div id=""title-bar"">{System.Net.WebUtility.HtmlEncode(_title)}</div>
        <div id=""loading"">
            <div class=""spinner""></div>
            加载中...
        </div>
        <div id=""error"">
            <div style=""font-size: 40px; margin-bottom: 10px;"">⚠️</div>
            <div id=""error-message"">播放失败</div>
        </div>
        <video id=""video"" crossorigin=""anonymous""></video>
    </div>
    <div id=""controls"">
        <button class=""btn"" id=""play-btn"" title=""播放/暂停"">▶️</button>
        <div id=""progress-container"">
            <div id=""progress-bar""></div>
        </div>
        <span id=""time-display"">00:00 / 00:00</span>
        <div id=""volume-container"">
            <button class=""btn"" id=""mute-btn"" title=""静音"">🔊</button>
            <input type=""range"" id=""volume-slider"" min=""0"" max=""100"" value=""100"">
        </div>
    </div>

    {(isHls ? "<script src=\"https://cdn.jsdelivr.net/npm/hls.js@latest\"></script>" : "")}
    
    <script>
        const videoUrl = ""{escapedUrl}"";
        const isHls = {isHls.ToString().ToLower()};
        
        const video = document.getElementById('video');
        const playBtn = document.getElementById('play-btn');
        const progressContainer = document.getElementById('progress-container');
        const progressBar = document.getElementById('progress-bar');
        const timeDisplay = document.getElementById('time-display');
        const volumeSlider = document.getElementById('volume-slider');
        const muteBtn = document.getElementById('mute-btn');
        const loading = document.getElementById('loading');
        const error = document.getElementById('error');
        const errorMessage = document.getElementById('error-message');
        
        let hls = null;
        
        // 初始化播放器
        function initPlayer() {{
            if (isHls) {{
                if (Hls.isSupported()) {{
                    hls = new Hls({{
                        maxBufferLength: 30,
                        maxMaxBufferLength: 60
                    }});
                    hls.loadSource(videoUrl);
                    hls.attachMedia(video);
                    hls.on(Hls.Events.MANIFEST_PARSED, () => {{
                        hideLoading();
                        video.play().catch(() => {{}});
                    }});
                    hls.on(Hls.Events.ERROR, (event, data) => {{
                        if (data.fatal) {{
                            showError('HLS 播放失败: ' + data.details);
                        }}
                    }});
                }} else if (video.canPlayType('application/vnd.apple.mpegurl')) {{
                    // Safari 原生支持 HLS
                    video.src = videoUrl;
                    video.addEventListener('loadedmetadata', () => {{
                        hideLoading();
                        video.play().catch(() => {{}});
                    }});
                }} else {{
                    showError('浏览器不支持 HLS 播放');
                }}
            }} else {{
                video.src = videoUrl;
                video.addEventListener('loadedmetadata', () => {{
                    hideLoading();
                    video.play().catch(() => {{}});
                }});
                video.addEventListener('error', (e) => {{
                    showError('视频加载失败');
                }});
            }}
        }}
        
        // 隐藏加载提示
        function hideLoading() {{
            loading.style.display = 'none';
        }}
        
        // 显示错误
        function showError(msg) {{
            hideLoading();
            errorMessage.textContent = msg;
            error.style.display = 'block';
        }}
        
        // 格式化时间
        function formatTime(seconds) {{
            if (isNaN(seconds)) return '00:00';
            const mins = Math.floor(seconds / 60);
            const secs = Math.floor(seconds % 60);
            return `${{mins.toString().padStart(2, '0')}}:${{secs.toString().padStart(2, '0')}}`;
        }}
        
        // 播放/暂停
        playBtn.addEventListener('click', () => {{
            if (video.paused) {{
                video.play();
            }} else {{
                video.pause();
            }}
        }});
        
        video.addEventListener('play', () => {{
            playBtn.textContent = '⏸️';
        }});
        
        video.addEventListener('pause', () => {{
            playBtn.textContent = '▶️';
        }});
        
        // 进度条
        video.addEventListener('timeupdate', () => {{
            const percent = (video.currentTime / video.duration) * 100;
            progressBar.style.width = percent + '%';
            timeDisplay.textContent = `${{formatTime(video.currentTime)}} / ${{formatTime(video.duration)}}`;
        }});
        
        progressContainer.addEventListener('click', (e) => {{
            const rect = progressContainer.getBoundingClientRect();
            const percent = (e.clientX - rect.left) / rect.width;
            video.currentTime = percent * video.duration;
        }});
        
        // 音量控制
        volumeSlider.addEventListener('input', (e) => {{
            video.volume = e.target.value / 100;
            video.muted = false;
            updateMuteIcon();
        }});
        
        muteBtn.addEventListener('click', () => {{
            video.muted = !video.muted;
            updateMuteIcon();
        }});
        
        function updateMuteIcon() {{
            if (video.muted || video.volume === 0) {{
                muteBtn.textContent = '🔇';
            }} else if (video.volume < 0.5) {{
                muteBtn.textContent = '🔉';
            }} else {{
                muteBtn.textContent = '🔊';
            }}
        }}
        
        // 键盘快捷键
        document.addEventListener('keydown', (e) => {{
            switch(e.key) {{
                case ' ':
                case 'k':
                    e.preventDefault();
                    if (video.paused) video.play();
                    else video.pause();
                    break;
                case 'ArrowLeft':
                    e.preventDefault();
                    video.currentTime = Math.max(0, video.currentTime - 5);
                    break;
                case 'ArrowRight':
                    e.preventDefault();
                    video.currentTime = Math.min(video.duration, video.currentTime + 5);
                    break;
                case 'ArrowUp':
                    e.preventDefault();
                    video.volume = Math.min(1, video.volume + 0.1);
                    volumeSlider.value = video.volume * 100;
                    break;
                case 'ArrowDown':
                    e.preventDefault();
                    video.volume = Math.max(0, video.volume - 0.1);
                    volumeSlider.value = video.volume * 100;
                    break;
                case 'm':
                    video.muted = !video.muted;
                    updateMuteIcon();
                    break;
            }}
        }});
        
        // 启动播放器
        initPlayer();
    </script>
</body>
</html>";
    }

    /// <summary>
    /// 转义 JavaScript 字符串。
    /// </summary>
    /// <param name="value">原始字符串。</param>
    /// <returns>转义后的字符串。</returns>
    private static string EscapeJavaScriptString(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("'", "\\'")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _webView?.Dispose();
        }

        base.Dispose(disposing);
    }
}
