/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Downloads
*文件名： FfmpegDownloadHandlerTests
*版本号： V1.0.0.0
*唯一标识：caece791-9f7b-40c0-851b-70c9beacdbf2
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:44:00
*描述：FfmpegDownloadHandler 的单元测试，覆盖 DRM 预检、退出码映射、进度上报与取消传播。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:44:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Net;
using System.Text;
using BrowserVideoGrabber.Core.Ffmpeg;
using BrowserVideoGrabber.Core.Models;
using BrowserVideoGrabber.Infrastructure.Downloads;
using BrowserVideoGrabber.Infrastructure.Ffmpeg;
using BrowserVideoGrabber.Tests.Fakes;

namespace BrowserVideoGrabber.Tests.Downloads;

/// <summary>
/// <see cref="FfmpegDownloadHandler"/> 的行为验证。
/// </summary>
/// <remarks>
/// 本组用例全部通过假进程运行器与桩 HTTP 处理器完成，
/// 既不依赖本机安装 ffmpeg，也不会发起任何真实网络请求。
/// </remarks>
public sealed class FfmpegDownloadHandlerTests
{
    /// <summary>
    /// 该处理器只接管分片/清单类格式，MP4 整文件应交给原生下载器。
    /// </summary>
    [Fact]
    public void CanHandle_ShouldOnlyAcceptPlaylistAndSegmentFormats()
    {
        var handler = CreateHandler(new FakeProcessRunner(), new FakeFileSystem(), out _);

        Assert.True(handler.CanHandle(CreateTask(VideoFormat.M3u8, "https://a.com/i.m3u8", @"D:\o.mp4")));
        Assert.True(handler.CanHandle(CreateTask(VideoFormat.Ts, "https://a.com/s.ts", @"D:\o.mp4")));
        Assert.True(handler.CanHandle(CreateTask(VideoFormat.M4s, "https://a.com/s.m4s", @"D:\o.mp4")));
        Assert.True(handler.CanHandle(CreateTask(VideoFormat.Mpd, "https://a.com/m.mpd", @"D:\o.mp4")));
        Assert.False(handler.CanHandle(CreateTask(VideoFormat.Mp4, "https://a.com/v.mp4", @"D:\o.mp4")));
    }

    /// <summary>
    /// 遇到 DRM 保护内容必须立即失败，且不得启动 ffmpeg 进程。
    /// 这既避免浪费重试次数，也让界面能立刻给出「受保护内容」的准确提示。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_ShouldFailFast_ForDrmProtectedPlaylist()
    {
        const string drmPlaylist = """
            #EXTM3U
            #EXT-X-KEY:METHOD=SAMPLE-AES,URI="skd://drm"
            #EXTINF:10.0,
            seg_000.ts
            """;

        var runner = new FakeProcessRunner();
        var fileSystem = new FakeFileSystem();
        var handler = CreateHandler(runner, fileSystem, out _, drmPlaylist);
        var task = CreateTask(VideoFormat.M3u8, "https://a.com/hls/index.m3u8", @"D:\out\movie.mp4");

        var result = await handler.DownloadAsync(task, new Progress<DownloadProgress>(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.IsDrmProtected);
        Assert.False(result.IsRetryable);
        Assert.Empty(runner.Invocations);
    }

    /// <summary>
    /// 成功路径：应调用进程运行器、传入正确的可执行文件与输入地址，并回填输出体积。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_ShouldRunFfmpegAndReturnOutputSize()
    {
        const string outputPath = @"D:\out\movie.mp4";
        var runner = new FakeProcessRunner
        {
            StandardErrorLines =
            {
                "Duration: 00:01:00.00, start: 0.000000, bitrate: 1200 kb/s",
                "out_time=00:01:00.000000"
            }
        };

        var fileSystem = new FakeFileSystem();
        // 预置输出文件，模拟 ffmpeg 已经写出结果
        fileSystem.SeedFile(outputPath, 8192);

        var handler = CreateHandler(runner, fileSystem, out var ffmpegPath);
        var task = CreateTask(VideoFormat.M3u8, "https://a.com/hls/index.m3u8", outputPath);

        var result = await handler.DownloadAsync(task, new Progress<DownloadProgress>(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(outputPath, result.OutputPath);
        Assert.Equal(8192, result.OutputBytes);

        var invocation = Assert.Single(runner.Invocations);
        Assert.Equal(ffmpegPath, invocation.ExecutablePath);
        Assert.Equal("https://a.com/hls/index.m3u8", invocation.GetValueAfter("-i"));
        Assert.Equal(outputPath, invocation.Arguments[^1]);
    }

    /// <summary>
    /// ffmpeg 退出码非 0 时必须判定为失败，并把 stderr 的关键信息带进错误描述，
    /// 否则用户只能看到一个「下载失败」而无从排查。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_ShouldFailWithStderrDetail_WhenExitCodeNonZero()
    {
        var runner = new FakeProcessRunner { ExitCode = 1 };
        runner.StandardErrorLines.Add("https://a.com/seg.ts: HTTP error 403 Forbidden");
        runner.StandardErrorLines.Add("Failed to open segment 3");

        var fileSystem = new FakeFileSystem();
        var handler = CreateHandler(runner, fileSystem, out _);
        var task = CreateTask(VideoFormat.M3u8, "https://a.com/hls/index.m3u8", @"D:\out\movie.mp4");

        var result = await handler.DownloadAsync(task, new Progress<DownloadProgress>(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.IsRetryable);
        Assert.NotNull(result.Error);
        Assert.Contains("退出码 1", result.Error);
        Assert.Contains("403", result.Error);
    }

    /// <summary>
    /// 403 类失败应给出针对性的中文提示，引导用户检查 Referer / 登录态。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_ShouldHintReferer_ForForbiddenError()
    {
        var runner = new FakeProcessRunner { ExitCode = 1 };
        runner.StandardErrorLines.Add("Server returned 403 Forbidden (access denied)");

        var handler = CreateHandler(runner, new FakeFileSystem(), out _);
        var task = CreateTask(VideoFormat.M3u8, "https://a.com/hls/index.m3u8", @"D:\out\movie.mp4");

        var result = await handler.DownloadAsync(task, new Progress<DownloadProgress>(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Referer", result.Error);
    }

    /// <summary>
    /// 进度必须被转发到上层，且百分比应随着 stderr 中的时间码推进。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_ShouldReportProgress()
    {
        const string outputPath = @"D:\out\movie.mp4";
        var runner = new FakeProcessRunner();
        runner.StandardErrorLines.Add("Duration: 00:02:00.00, start: 0.000000, bitrate: 1000 kb/s");
        runner.StandardErrorLines.Add("out_time=00:00:30.000000");
        runner.StandardErrorLines.Add("out_time=00:01:00.000000");

        var fileSystem = new FakeFileSystem();
        fileSystem.SeedFile(outputPath, 1024);

        var handler = CreateHandler(runner, fileSystem, out _);
        var task = CreateTask(VideoFormat.M3u8, "https://a.com/hls/index.m3u8", outputPath);

        // 使用同步进度收集器，保证断言时列表已经完整
        var percents = new List<double>();
        var progress = new InlineProgress<DownloadProgress>(p => percents.Add(p.Percent));

        var result = await handler.DownloadAsync(task, progress, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotEmpty(percents);

        // 30/120 = 25%，60/120 = 50%
        Assert.Contains(percents, p => Math.Abs(p - 25d) < 1d);
        Assert.Contains(percents, p => Math.Abs(p - 50d) < 1d);
    }

    /// <summary>
    /// 取消必须向上抛出 <see cref="OperationCanceledException"/>，而不是被转换成一次普通失败。
    /// 否则队列会把「用户取消」误当成「下载出错」，甚至触发无意义的重试。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_ShouldPropagateCancellation()
    {
        var runner = new FakeProcessRunner { LineDelay = TimeSpan.FromMilliseconds(150) };
        for (var i = 0; i < 10; i++)
        {
            runner.StandardErrorLines.Add($"out_time=00:00:0{i}.000000");
        }

        var handler = CreateHandler(runner, new FakeFileSystem(), out _);
        var task = CreateTask(VideoFormat.M3u8, "https://a.com/hls/index.m3u8", @"D:\out\movie.mp4");

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(120));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.DownloadAsync(task, new Progress<DownloadProgress>(), cts.Token));
    }

    /// <summary>
    /// 请求上下文中的 Referer / Cookie 必须被带到 m3u8 预检请求上，
    /// 否则探测本身就会 403，从而漏掉 DRM 判定。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_ShouldSendRequestHeaders_OnPlaylistProbe()
    {
        var runner = new FakeProcessRunner { ExitCode = 0 };
        var fileSystem = new FakeFileSystem();
        fileSystem.SeedFile(@"D:\out\movie.mp4", 512);

        var probe = new StubHttpMessageHandler("#EXTM3U\n#EXTINF:10.0,\nseg.ts\n");
        var handler = CreateHandler(runner, fileSystem, out _, httpMessageHandler: probe);

        var task = CreateTask(VideoFormat.M3u8, "https://a.com/hls/index.m3u8", @"D:\out\movie.mp4");
        task.Context.Referer = "https://a.com/watch";
        task.Context.UserAgent = "Mozilla/5.0 Test";
        task.Context.Cookie = "sid=abc";

        await handler.DownloadAsync(task, new Progress<DownloadProgress>(), CancellationToken.None);

        var sent = Assert.Single(probe.CapturedHeaders);
        Assert.True(sent.ContainsKey("Referer"));
        Assert.Equal("https://a.com/watch", sent["Referer"]);
        Assert.True(sent.ContainsKey("Cookie"));
    }

    /// <summary>
    /// ffmpeg 可执行文件缺失时应立即失败，并给出明确的安装引导文案。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_ShouldFail_WhenFfmpegNotFound()
    {
        var runner = new FakeProcessRunner();

        // 关闭 PATH / 常见安装目录搜索，确保测试环境即使装了 ffmpeg 也能走到「未找到」分支
        var handler = CreateHandler(
            runner,
            new FakeFileSystem(),
            out _,
            configuredFfmpegPath: null,
            ffmpegExists: false,
            enableEnvironmentSearch: false);

        var task = CreateTask(VideoFormat.M3u8, "https://a.com/hls/index.m3u8", @"D:\out\movie.mp4");

        var result = await handler.DownloadAsync(task, new Progress<DownloadProgress>(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("ffmpeg", result.Error);
        Assert.Empty(runner.Invocations);
    }

    /// <summary>
    /// 创建被测处理器。
    /// </summary>
    /// <param name="runner">进程运行器假实现。</param>
    /// <param name="fileSystem">文件系统假实现。</param>
    /// <param name="ffmpegPath">输出参数：定位到的 ffmpeg 路径，便于断言。</param>
    /// <param name="playlistContent">m3u8 预检接口返回的内容；为空表示不启用预检。</param>
    /// <param name="httpMessageHandler">自定义 HTTP 处理器。</param>
    /// <param name="configuredFfmpegPath">设置项中配置的 ffmpeg 路径。</param>
    /// <param name="ffmpegExists">是否让 ffmpeg 路径在假文件系统中真实存在。</param>
    /// <param name="enableEnvironmentSearch">是否允许在 PATH 与常见安装目录中搜索 ffmpeg。</param>
    /// <returns>被测处理器实例。</returns>
    private static FfmpegDownloadHandler CreateHandler(
        FakeProcessRunner runner,
        FakeFileSystem fileSystem,
        out string? ffmpegPath,
        string? playlistContent = null,
        HttpMessageHandler? httpMessageHandler = null,
        string? configuredFfmpegPath = @"C:\tools\ffmpeg.exe",
        bool ffmpegExists = true,
        bool enableEnvironmentSearch = true)
    {
        if (ffmpegExists && configuredFfmpegPath is not null)
        {
            fileSystem.SeedFile(configuredFfmpegPath, 1);
        }

        var locator = new FfmpegLocator(fileSystem, configuredFfmpegPath, appDirectory: null, enableEnvironmentSearch);
        ffmpegPath = locator.Locate();

        HttpClient? httpClient = null;
        if (playlistContent is not null || httpMessageHandler is not null)
        {
            httpClient = new HttpClient(httpMessageHandler ?? new StubHttpMessageHandler(playlistContent ?? string.Empty))
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
        }

        return new FfmpegDownloadHandler(runner, fileSystem, locator, new FfmpegOptions(), httpClient);
    }

    /// <summary>
    /// 构造一个测试用下载任务。
    /// </summary>
    /// <param name="format">资源格式。</param>
    /// <param name="url">资源地址。</param>
    /// <param name="outputPath">输出路径。</param>
    /// <returns>下载任务实例。</returns>
    private static DownloadTask CreateTask(VideoFormat format, string url, string outputPath)
        => new()
        {
            Url = url,
            Format = format,
            OutputPath = outputPath,
            Title = "movie"
        };

    /// <summary>
    /// 同步执行的进度收集器。
    /// </summary>
    /// <typeparam name="T">进度数据类型。</typeparam>
    /// <remarks>
    /// 框架自带的 <see cref="Progress{T}"/> 会把回调投递到线程池，
    /// 导致断言时回调可能尚未执行；本实现改为同步回调，让测试完全确定。
    /// </remarks>
    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _onReport;

        public InlineProgress(Action<T> onReport) => _onReport = onReport;

        public void Report(T value) => _onReport(value);
    }

    /// <summary>
    /// 桩 HTTP 处理器：返回固定内容，并记录每次请求的请求头。
    /// </summary>
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _content;
        private readonly HttpStatusCode _statusCode;

        public StubHttpMessageHandler(string content, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _content = content;
            _statusCode = statusCode;
        }

        /// <summary>已捕获的请求头快照，按请求顺序排列。</summary>
        public List<Dictionary<string, string>> CapturedHeaders { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var header in request.Headers)
            {
                headers[header.Key] = string.Join(",", header.Value);
            }

            CapturedHeaders.Add(headers);

            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content, Encoding.UTF8, "application/vnd.apple.mpegurl")
            };

            return Task.FromResult(response);
        }
    }
}
