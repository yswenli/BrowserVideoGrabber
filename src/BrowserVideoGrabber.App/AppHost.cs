/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.App
*文件名： AppHost
*版本号： V1.0.0.0
*唯一标识：fc861e59-0a3d-4c72-b5e9-1f8a6d2c4b70
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 02:12:00
*描述：应用组合根，负责装配设置、ffmpeg 定位、下载处理器与下载队列，并向界面层屏蔽装配细节。
*
*=================================================
*修改标记
*修改时间：2026/9/13 02:12:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Net;
using BrowserVideoGrabber.Core.Abstractions;
using BrowserVideoGrabber.Core.Configuration;
using BrowserVideoGrabber.Core.Downloads;
using BrowserVideoGrabber.Core.Ffmpeg;
using BrowserVideoGrabber.Core.Models;
using BrowserVideoGrabber.Infrastructure.Downloads;
using BrowserVideoGrabber.Infrastructure.Execution;
using BrowserVideoGrabber.Infrastructure.Ffmpeg;
using BrowserVideoGrabber.Infrastructure.Sniffing;
using BrowserVideoGrabber.Infrastructure.Storage;
using Microsoft.Web.WebView2.WinForms;

namespace BrowserVideoGrabber.App;

/// <summary>
/// 应用组合根。
/// </summary>
/// <remarks>
/// <para>
/// 手写组合根而不引入 DI 容器（设计文档第 2 节的 YAGNI 决策）：本应用的依赖图只有十余个节点、
/// 层次清晰且无生命周期交错，容器带来的注册语法与隐式解析反而会让「谁依赖谁」变得不直观。
/// </para>
/// <para>
/// <b>为什么要再广播一次队列事件</b>：修改设置时需要重建整条下载管线（处理器与队列都不可变），
/// 若界面直接订阅队列事件，重建后就必须重新订阅。本类作为稳定的订阅端点，
/// 把重建这件事完全封装在内部，界面侧只订阅一次。
/// </para>
/// </remarks>
public sealed class AppHost : IDisposable
{
    /// <summary>设置变更后的持久化防抖延迟，避免用户连续调整时反复写盘。</summary>
    private static readonly TimeSpan PersistDebounceDelay = TimeSpan.FromSeconds(1);

    private readonly IFileSystem _fileSystem = PhysicalFileSystem.Instance;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _persistGate = new(1, 1);
    private readonly string _taskFilePath;

    private System.Threading.Timer? _persistDebounce;
    private bool _disposed;

    /// <summary>
    /// 初始化应用宿主。
    /// </summary>
    public AppHost()
    {
        SettingsStore = new JsonAppSettingsStore(JsonAppSettingsStore.GetDefaultFilePath(), _fileSystem);
        Settings = SettingsStore.Load();

        var settingsFilePath = JsonAppSettingsStore.GetDefaultFilePath();
        var settingsDirectory = Path.GetDirectoryName(settingsFilePath) ?? AppContext.BaseDirectory;
        _taskFilePath = Path.Combine(settingsDirectory, "tasks.json");

        _httpClient = CreateHttpClient();
        FfmpegLocator = CreateFfmpegLocator();
        Queue = CreateQueue();
    }

    /// <summary>当前生效的应用设置。</summary>
    public AppSettings Settings { get; private set; }

    /// <summary>设置持久化仓储。</summary>
    public JsonAppSettingsStore SettingsStore { get; }

    /// <summary>ffmpeg 定位器。</summary>
    public FfmpegLocator FfmpegLocator { get; private set; }

    /// <summary>下载队列。</summary>
    public DownloadQueue Queue { get; private set; }

    /// <summary>任务状态变更（由底层队列再广播）。</summary>
    public event EventHandler<DownloadTaskEventArgs>? TaskStateChanged;

    /// <summary>任务进度变更（由底层队列再广播）。</summary>
    public event EventHandler<DownloadProgressEventArgs>? TaskProgressChanged;

    /// <summary>
    /// 为指定 WebView2 创建嗅探器。
    /// </summary>
    /// <param name="webView">浏览器控件。</param>
    /// <returns>嗅探器实例。</returns>
    public IVideoSniffer CreateSniffer(WebView2 webView)
    {
        ArgumentNullException.ThrowIfNull(webView);
        return new WebView2Sniffer(webView);
    }

    /// <summary>
    /// 为指定 WebView2 创建请求上下文提供者。
    /// </summary>
    /// <param name="webView">浏览器控件。</param>
    /// <returns>请求上下文提供者实例。</returns>
    /// <remarks>设置变更后需重新创建：提供者持有设置快照，否则用户自定义的 UA 不会生效。</remarks>
    public IRequestContextProvider CreateContextProvider(WebView2 webView)
    {
        ArgumentNullException.ThrowIfNull(webView);
        return new WebView2RequestContextProvider(webView, Settings);
    }

    /// <summary>
    /// 载入历史任务并启动调度。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步载入的任务。</returns>
    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        await Queue.RestoreAsync(cancellationToken).ConfigureAwait(true);
        Queue.Start();
    }

    /// <summary>
    /// 应用新的设置：持久化、重建下载管线，并让设置立即生效。
    /// </summary>
    /// <param name="settings">新设置。</param>
    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Settings = settings.Clone();
        SettingsStore.Save(Settings);

        RebuildPipeline();
    }

    /// <summary>
    /// 探测 ffmpeg 是否可用。
    /// </summary>
    /// <param name="configuredPath">
    /// 用于探测的显式路径。传 null 表示忽略设置中的路径，
    /// 仅按「应用目录 → PATH → 常见安装位置」探测（设置页「自动探测」按钮使用）。
    /// </param>
    /// <returns>定位到的路径；未找到返回 null。</returns>
    public string? DetectFfmpeg(string? configuredPath = null)
        => new FfmpegLocator(_fileSystem, configuredPath, AppContext.BaseDirectory, enableEnvironmentSearch: true).Locate();

    /// <summary>
    /// 当前 ffmpeg 是否可用。
    /// </summary>
    /// <returns>可用返回 true。</returns>
    public bool IsFfmpegAvailable() => FfmpegLocator.IsAvailable();

    /// <summary>
    /// 为嗅探到的资源生成输出文件路径。
    /// </summary>
    /// <param name="video">嗅探结果。</param>
    /// <returns>输出文件的完整路径。</returns>
    /// <remarks>
    /// 统一使用 <c>.mp4</c> 扩展名：m3u8 / ts / m4s 均由 ffmpeg 以流拷贝方式封装为 mp4，
    /// 这样无论来源格式如何，产物都能被播放器直接打开。
    /// </remarks>
    public string BuildOutputPath(SniffedVideo video)
    {
        ArgumentNullException.ThrowIfNull(video);

        var directory = ResolveOutputDirectory();
        var baseName = SanitizeFileName(Path.GetFileNameWithoutExtension(video.DisplayTitle));

        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "video";
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var candidate = Path.Combine(directory, $"{baseName}_{stamp}.mp4");

        // 同一秒内重复下载同名资源时追加序号，避免后一次覆盖前一次
        var counter = 1;
        while (_fileSystem.FileExists(candidate) && counter <= 999)
        {
            candidate = Path.Combine(directory, $"{baseName}_{stamp}({counter}).mp4");
            counter++;
        }

        return candidate;
    }

    /// <summary>
    /// 解析输出目录：优先使用设置中的值，不可用时回退到系统「下载」目录。
    /// </summary>
    /// <returns>可用的输出目录。</returns>
    public string ResolveOutputDirectory()
    {
        var configured = Settings.OutputDirectory;

        if (!string.IsNullOrWhiteSpace(configured))
        {
            try
            {
                _fileSystem.CreateDirectory(configured);
                return configured;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // 目录被删除、无写权限或路径非法时回退到默认位置，而不是让下载直接失败
            }
        }

        var fallback = GetDefaultOutputDirectory();

        try
        {
            _fileSystem.CreateDirectory(fallback);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 连默认目录都建不出来时只能交给下载器去报错，此处不掩盖真实原因
        }

        return fallback;
    }

    /// <summary>
    /// 记忆最后访问的地址。
    /// </summary>
    /// <param name="url">页面地址。</param>
    /// <remarks>
    /// 刻意与 <see cref="ApplySettings"/> 分开：后者会重建下载管线，
    /// 而关闭程序时记忆地址若触发重建，会把正在下载的任务无谓地取消一遍。
    /// </remarks>
    public void RememberLastUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || string.Equals(Settings.LastUrl, url, StringComparison.Ordinal))
        {
            return;
        }

        Settings.LastUrl = url;
        SettingsStore.Save(Settings);
    }

    /// <summary>
    /// 立即把设置与任务列表落盘（关闭程序前调用）。
    /// </summary>
    public void SaveNow()
    {
        try
        {
            SettingsStore.Save(Settings);

            // 切到线程池执行后同步等待：若直接在 UI 线程同步等待含 await 的持久化流程，可能因上下文捕获而死锁
            Task.Run(() => Queue.PersistAsync()).Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception exception) when (exception is AggregateException or ObjectDisposedException)
        {
            // 退出路径上的持久化失败不应阻止用户关闭程序
        }
    }

    /// <summary>
    /// 安排一次任务列表落盘。
    /// </summary>
    /// <remarks>
    /// 供「移除任务 / 清空已结束」这类<b>不触发状态变更事件</b>的操作调用。
    /// 队列的 <c>Remove</c> 与 <c>ClearFinished</c> 不会上报事件（任务已消失，无从通知），
    /// 界面的行是调用方自行同步移除的；若不同时安排落盘，被移除的任务会留在 tasks.json 里，
    /// 下次启动又原样出现，看起来像「删不掉」。
    /// </remarks>
    public void RequestPersist() => SchedulePersist();

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _persistDebounce?.Dispose();
        _persistDebounce = null;

        Queue.TaskStateChanged -= OnQueueTaskStateChanged;
        Queue.TaskProgressChanged -= OnQueueTaskProgressChanged;
        Queue.Dispose();

        _httpClient.Dispose();
        _persistGate.Dispose();
    }

    /// <summary>
    /// 创建共享的 HTTP 客户端。
    /// </summary>
    /// <returns>HTTP 客户端。</returns>
    /// <remarks>
    /// 两处设置直接决定下载器能否正确工作：
    /// 关闭自动解压（分片拼接依赖字节偏移）、总超时设为无限（超时改由下载器的空闲超时承担）。
    /// </remarks>
    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(30),
            AllowAutoRedirect = true,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
        };

        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>
    /// 按当前设置创建 ffmpeg 定位器。
    /// </summary>
    /// <returns>定位器实例。</returns>
    private FfmpegLocator CreateFfmpegLocator()
        => new(_fileSystem, Settings.FfmpegPath, AppContext.BaseDirectory, enableEnvironmentSearch: true);

    /// <summary>
    /// 按当前设置创建下载队列及其全部依赖。
    /// </summary>
    /// <returns>已订阅事件但尚未启动的队列。</returns>
    private DownloadQueue CreateQueue()
    {
        var ffmpegHandler = new FfmpegDownloadHandler(
            new ProcessRunner(),
            _fileSystem,
            FfmpegLocator,
            new FfmpegOptions(),
            _httpClient);

        var httpOptions = new HttpDownloadOptions { SegmentCount = Math.Max(1, Settings.HttpSegmentCount) };
        var httpHandler = new HttpDownloadHandler(_fileSystem, _httpClient, httpOptions, fallback: ffmpegHandler);

        // mp4 走原生多线程下载器，其余格式走 ffmpeg；两者格式集合互不重叠
        var factory = new DownloadHandlerFactory([httpHandler, ffmpegHandler]);

        var repository = new JsonTaskRepository(_taskFilePath, _fileSystem);
        var options = new DownloadQueueOptions { MaxConcurrency = Math.Max(1, Settings.MaxConcurrency) };

        var queue = new DownloadQueue(factory, options, repository);
        queue.TaskStateChanged += OnQueueTaskStateChanged;
        queue.TaskProgressChanged += OnQueueTaskProgressChanged;

        return queue;
    }

    /// <summary>
    /// 重建下载管线，使新设置立即生效。
    /// </summary>
    /// <remarks>
    /// 并发上限在队列构造时固化为信号量容量，无法原地修改，因此只能整体重建。
    /// 重建时把任务列表原样迁移到新队列，并把「下载中」降级为「等待中」，
    /// 因为旧队列释放时已完成取消，原任务已不再运行。
    /// </remarks>
    private void RebuildPipeline()
    {
        var carried = Queue.Tasks;

        Queue.TaskStateChanged -= OnQueueTaskStateChanged;
        Queue.TaskProgressChanged -= OnQueueTaskProgressChanged;
        Queue.Dispose();

        FfmpegLocator = CreateFfmpegLocator();
        Queue = CreateQueue();

        foreach (var task in carried)
        {
            var copy = task.Clone();

            if (copy.Status == DownloadStatus.Running)
            {
                copy.Status = DownloadStatus.Pending;
            }

            Queue.Enqueue(copy);
        }

        Queue.Start();
    }

    /// <summary>
    /// 队列状态变更：安排持久化并向上再广播。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnQueueTaskStateChanged(object? sender, DownloadTaskEventArgs e)
    {
        SchedulePersist();
        TaskStateChanged?.Invoke(this, e);
    }

    /// <summary>
    /// 队列进度变更：直接向上再广播（进度不触发持久化，写入过于频繁且无恢复价值）。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnQueueTaskProgressChanged(object? sender, DownloadProgressEventArgs e)
        => TaskProgressChanged?.Invoke(this, e);

    /// <summary>
    /// 安排一次防抖持久化。
    /// </summary>
    /// <remarks>
    /// 一次下载会产生「等待中 → 下载中 → 已完成」三次状态变更，若每次都立即写盘，
    /// 多个任务并发时磁盘写入会相当频繁；防抖之后只在状态稳定时写一次。
    /// </remarks>
    private void SchedulePersist()
    {
        if (_disposed)
        {
            return;
        }

        _persistDebounce ??= new System.Threading.Timer(
            _ => _ = PersistQuietlyAsync(),
            state: null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);

        _persistDebounce.Change(PersistDebounceDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// 持久化任务列表，吞掉异常。
    /// </summary>
    /// <returns>表示异步写入的任务。</returns>
    private async Task PersistQuietlyAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await _persistGate.WaitAsync().ConfigureAwait(false);

            try
            {
                await Queue.PersistAsync().ConfigureAwait(false);
            }
            finally
            {
                _persistGate.Release();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            // 任务列表持久化只是便利功能，写失败不应影响正在进行的下载
        }
    }

    /// <summary>
    /// 取得默认输出目录。
    /// </summary>
    /// <returns>默认输出目录。</returns>
    private static string GetDefaultOutputDirectory()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
        {
            return Path.Combine(profile, "Downloads");
        }

        var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        return string.IsNullOrWhiteSpace(videos) ? AppContext.BaseDirectory : videos;
    }

    /// <summary>
    /// 清洗文件名，剔除 Windows 不允许的字符。
    /// </summary>
    /// <param name="name">原始文件名（不含扩展名）。</param>
    /// <returns>可用于文件系统的名称。</returns>
    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(name.Length);

        foreach (var character in name)
        {
            builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
        }

        var sanitized = builder.ToString().Trim().Trim('.');

        // 过长的标题会撑爆路径长度上限，截断到 80 字符以留出目录与后缀空间
        return sanitized.Length > 80 ? sanitized[..80] : sanitized;
    }
}
