/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Downloads
*文件名： HttpDownloadHandlerTests
*版本号： V1.0.0.0
*唯一标识：9c37c577-2a5f-4e83-b1d6-7c4a9e2f8b61
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 00:30:00
*描述：HttpDownloadHandler 的单元测试，覆盖分段下载、断点续传、鉴权头、错误映射与回退。
*
*=================================================
*修改标记
*修改时间：2026/9/13 00:30:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Net;
using BrowserVideoGrabber.Core.Abstractions;
using BrowserVideoGrabber.Core.Downloads;
using BrowserVideoGrabber.Core.Models;
using BrowserVideoGrabber.Infrastructure.Downloads;
using BrowserVideoGrabber.Tests.Fakes;

namespace BrowserVideoGrabber.Tests.Downloads;

/// <summary>
/// <see cref="HttpDownloadHandler"/> 的行为验证。
/// </summary>
/// <remarks>
/// 全部用例通过假 HTTP 处理器与内存文件系统完成，既不联网也不落盘，
/// 因此可以稳定复现「服务端不支持 Range」「返回 403」「磁盘写满」等真实环境中的边界场景。
/// </remarks>
public sealed class HttpDownloadHandlerTests
{
    private const string OutputPath = @"D:\out\movie.mp4";

    /// <summary>
    /// 该处理器只接管 mp4 整文件，其余格式应由 ffmpeg 处理器负责。
    /// </summary>
    [Fact]
    public void CanHandle_ShouldOnlyAcceptMp4()
    {
        var handler = CreateHandler(new FakeHttpMessageHandler([]), new FakeFileSystem(), out _);

        Assert.True(handler.CanHandle(CreateTask(VideoFormat.Mp4)));
        Assert.False(handler.CanHandle(CreateTask(VideoFormat.M3u8)));
        Assert.False(handler.CanHandle(CreateTask(VideoFormat.Ts)));
        Assert.False(handler.CanHandle(CreateTask(VideoFormat.M4s)));
        Assert.False(handler.CanHandle(CreateTask(VideoFormat.Mpd)));
    }

    /// <summary>
    /// 服务端不支持 Range 时应退化为单连接下载，并仍能产出完整文件。
    /// 若此时仍强行分片，会因每个分片都收到整文件而导致输出膨胀数倍。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_ShouldUseSingleConnection_WhenServerIgnoresRange()
    {
        var content = FakeHttpMessageHandler.CreatePatternContent(1000);
        var http = new FakeHttpMessageHandler(content) { SupportsRange = false };
        var fileSystem = new FakeFileSystem();
        var handler = CreateHandler(http, fileSystem, out _, segmentCount: 4, minimumSegmentBytes: 1);

        var result = await handler.DownloadAsync(CreateTask(VideoFormat.Mp4), new Progress<DownloadProgress>(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(content, fileSystem.ReadFile(OutputPath));
        Assert.Equal(2, http.RequestCount);

        // 探测一次 + 下载一次；下载请求不应携带 Range，避免老旧网关直接报错
        Assert.Null(http.CapturedRanges[1]);
    }

    /// <summary>
    /// 服务端支持 Range 时应切分为多个区间并并发拉取，拼接结果必须与源逐字节一致。
    /// 这里的内容用「位置相关」的字节序列生成，拼接顺序一旦出错就会被断言捕获。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_ShouldSplitIntoRanges_WhenServerSupportsRange()
    {
        var content = FakeHttpMessageHandler.CreatePatternContent(1000);
        var http = new FakeHttpMessageHandler(content);
        var fileSystem = new FakeFileSystem();
        var handler = CreateHandler(http, fileSystem, out _, segmentCount: 4, minimumSegmentBytes: 1);

        var result = await handler.DownloadAsync(CreateTask(VideoFormat.Mp4), new Progress<DownloadProgress>(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(content, fileSystem.ReadFile(OutputPath));

        // 1 次探测 + 4 次分片
        Assert.Equal(5, http.RequestCount);
        Assert.Contains(http.CapturedRanges, r => r?.From == 0 && r?.To == 249);
        Assert.Contains(http.CapturedRanges, r => r?.From == 250 && r?.To == 499);
        Assert.Contains(http.CapturedRanges, r => r?.From == 500 && r?.To == 749);
        Assert.Contains(http.CapturedRanges, r => r?.From == 750 && r?.To == 999);
    }

    /// <summary>
    /// 已完整落盘的分片必须被识别并跳过，只补齐缺失的部分 —— 这正是断点续传的核心价值。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_ShouldResume_FromExistingPartFile()
    {
        var content = FakeHttpMessageHandler.CreatePatternContent(1000);
        var http = new FakeHttpMessageHandler(content);
        var fileSystem = new FakeFileSystem();
        var handler = CreateHandler(http, fileSystem, out _, segmentCount: 2, minimumSegmentBytes: 1);

        // 预置「第一个分片已下载完成」的半成品现场
        fileSystem.SeedFile(OutputPath + ".part0", content[..500]);

        var result = await handler.DownloadAsync(CreateTask(VideoFormat.Mp4), new Progress<DownloadProgress>(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(content, fileSystem.ReadFile(OutputPath));

        // 1 次探测 + 仅第二个分片的一次请求
        Assert.Equal(2, http.RequestCount);
        Assert.Contains(http.CapturedRanges, r => r?.From == 500 && r?.To == 999);

        // 已经完整的分片不应被重复请求
        Assert.DoesNotContain(http.CapturedRanges, r => r?.From == 0 && r?.To == 499);
    }

    /// <summary>
    /// 进度必须携带总字节数并最终收敛到 100%，否则界面无法给出可信的进度条。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_ShouldReportProgressUpTo100()
    {
        var content = FakeHttpMessageHandler.CreatePatternContent(400);
        var http = new FakeHttpMessageHandler(content);
        var fileSystem = new FakeFileSystem();
        var handler = CreateHandler(http, fileSystem, out _, segmentCount: 4, minimumSegmentBytes: 1);

        var snapshots = new List<DownloadProgress>();
        var progress = new InlineProgress<DownloadProgress>(snapshots.Add);

        var result = await handler.DownloadAsync(CreateTask(VideoFormat.Mp4), progress, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotEmpty(snapshots);
        Assert.Contains(snapshots, s => Math.Abs(s.Percent - 100d) < 0.001);
        Assert.All(snapshots, s => Assert.True(s.HasTotal));
        Assert.Equal(400, snapshots[^1].DownloadedBytes);
    }

    /// <summary>
    /// 403 应给出「需登录 / 缺少 Referer」的针对性提示，并标记为可重试。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_ShouldFailWithLoginHint_WhenForbidden()
    {
        var http = new FakeHttpMessageHandler([]) { StatusCode = HttpStatusCode.Forbidden };
        var fileSystem = new FakeFileSystem();
        var handler = CreateHandler(http, fileSystem, out _);

        var result = await handler.DownloadAsync(CreateTask(VideoFormat.Mp4), new Progress<DownloadProgress>(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.IsRetryable);
        Assert.NotNull(result.Error);
        Assert.Contains("403", result.Error);
        Assert.Contains("登录", result.Error);
        Assert.False(fileSystem.FileExists(OutputPath));
    }

    /// <summary>
    /// 404 属于不可重试的失效地址，此时即便配置了回退也不应触发：
    /// 让 ffmpeg 去尝试一个已失效的链接，只会白白浪费一次重试与用户等待时间。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_ShouldNotFallback_WhenResourceNotFound()
    {
        var http = new FakeHttpMessageHandler([]) { StatusCode = HttpStatusCode.NotFound };
        var fileSystem = new FakeFileSystem();
        var fallback = new FakeDownloadHandler();
        var handler = CreateHandler(http, fileSystem, out _, fallback: fallback);

        var result = await handler.DownloadAsync(CreateTask(VideoFormat.Mp4), new Progress<DownloadProgress>(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.IsRetryable);
        Assert.NotNull(result.Error);
        Assert.Contains("404", result.Error);
        Assert.Equal(0, fallback.AttemptCount);
    }

    /// <summary>
    /// 原生链路可重试的失败应触发 ffmpeg 回退，且回退成功即视为任务成功。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_ShouldFallbackToFfmpeg_WhenNativeFails()
    {
        var http = new FakeHttpMessageHandler([]) { StatusCode = HttpStatusCode.Forbidden };
        var fileSystem = new FakeFileSystem();
        var fallback = new FakeDownloadHandler
        {
            ResultFactory = _ => DownloadResult.Ok(OutputPath, 4096)
        };

        var handler = CreateHandler(http, fileSystem, out _, fallback: fallback);

        var result = await handler.DownloadAsync(CreateTask(VideoFormat.Mp4), new Progress<DownloadProgress>(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(OutputPath, result.OutputPath);
        Assert.Equal(4096, result.OutputBytes);
        Assert.Equal(1, fallback.AttemptCount);
    }

    /// <summary>
    /// 磁盘空间不足必须在写入前就拦截，并标记为不可重试 —— 重试多少次都不会多出空间。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_ShouldFail_WhenDiskIsFull()
    {
        var content = FakeHttpMessageHandler.CreatePatternContent(1000);
        var http = new FakeHttpMessageHandler(content);
        var fileSystem = new FakeFileSystem { AvailableFreeSpace = 0 };
        var handler = CreateHandler(http, fileSystem, out _);

        var result = await handler.DownloadAsync(CreateTask(VideoFormat.Mp4), new Progress<DownloadProgress>(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.IsRetryable);
        Assert.NotNull(result.Error);
        Assert.Contains("空间不足", result.Error);
        Assert.False(fileSystem.FileExists(OutputPath));
    }

    /// <summary>
    /// 取消必须向上传播（而不是被吞成一次失败），并按约定清理全部半成品文件。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_ShouldPropagateCancellation_AndCleanupParts()
    {
        var content = FakeHttpMessageHandler.CreatePatternContent(1000);
        var http = new FakeHttpMessageHandler(content) { Delay = TimeSpan.FromMilliseconds(200) };
        var fileSystem = new FakeFileSystem();
        var handler = CreateHandler(http, fileSystem, out _, segmentCount: 1);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(40));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.DownloadAsync(CreateTask(VideoFormat.Mp4), new Progress<DownloadProgress>(), cts.Token));

        Assert.DoesNotContain(
            fileSystem.FilePaths,
            path => path.Contains(".part", StringComparison.Ordinal) || path.Contains(".assembling", StringComparison.Ordinal));
    }

    /// <summary>
    /// 请求上下文中的 Referer / UA / Cookie 必须出现在每个下载请求上，
    /// 否则直连第三方 CDN 必然 403。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_ShouldSendRequestHeaders()
    {
        var content = FakeHttpMessageHandler.CreatePatternContent(500);
        var http = new FakeHttpMessageHandler(content);
        var fileSystem = new FakeFileSystem();
        var handler = CreateHandler(http, fileSystem, out _);

        var task = CreateTask(VideoFormat.Mp4);
        task.Context.Referer = "https://a.com/watch";
        task.Context.UserAgent = "Mozilla/5.0 Test";
        task.Context.Cookie = "sid=abc";

        var result = await handler.DownloadAsync(task, new Progress<DownloadProgress>(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.All(http.CapturedRequestHeaders, headers =>
        {
            Assert.Equal("https://a.com/watch", headers["Referer"]);
            Assert.Equal("Mozilla/5.0 Test", headers["User-Agent"]);
            Assert.Equal("sid=abc", headers["Cookie"]);
        });
    }

    /// <summary>
    /// 探测超时必须中断本次尝试并交由回退处理：否则僵死地址会让任务永久停在「正在下载」。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_ShouldFallback_WhenProbeTimesOut()
    {
        var http = new FakeHttpMessageHandler([]) { Delay = TimeSpan.FromMilliseconds(300) };
        var fileSystem = new FakeFileSystem();
        var fallback = new FakeDownloadHandler
        {
            ResultFactory = _ => DownloadResult.Ok(OutputPath, 2048)
        };

        var handler = CreateHandler(
            http,
            fileSystem,
            out _,
            fallback: fallback,
            probeTimeout: TimeSpan.FromMilliseconds(50));

        var result = await handler.DownloadAsync(CreateTask(VideoFormat.Mp4), new Progress<DownloadProgress>(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, fallback.AttemptCount);
    }

    /// <summary>
    /// 创建被测处理器。
    /// </summary>
    /// <param name="http">假 HTTP 处理器。</param>
    /// <param name="fileSystem">内存文件系统。</param>
    /// <param name="httpClient">输出参数：构造出的 HTTP 客户端，便于复用。</param>
    /// <param name="segmentCount">分片并发数。</param>
    /// <param name="minimumSegmentBytes">触发分片的最小字节数。</param>
    /// <param name="fallback">回退处理器。</param>
    /// <param name="probeTimeout">探测超时。</param>
    /// <returns>被测处理器实例。</returns>
    private static HttpDownloadHandler CreateHandler(
        FakeHttpMessageHandler http,
        FakeFileSystem fileSystem,
        out HttpClient httpClient,
        int segmentCount = 4,
        long minimumSegmentBytes = 4L * 1024 * 1024,
        IDownloadHandler? fallback = null,
        TimeSpan? probeTimeout = null)
    {
        httpClient = new HttpClient(http) { Timeout = TimeSpan.FromSeconds(5) };

        var options = new HttpDownloadOptions
        {
            SegmentCount = segmentCount,
            MinimumSegmentBytes = minimumSegmentBytes
        };

        if (probeTimeout.HasValue)
        {
            options.ProbeTimeout = probeTimeout.Value;
        }

        return new HttpDownloadHandler(fileSystem, httpClient, options, fallback);
    }

    /// <summary>
    /// 构造一个测试用下载任务。
    /// </summary>
    /// <param name="format">资源格式。</param>
    /// <returns>下载任务实例。</returns>
    private static DownloadTask CreateTask(VideoFormat format)
        => new()
        {
            Url = "https://a.com/video.mp4",
            Format = format,
            OutputPath = OutputPath,
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
}
