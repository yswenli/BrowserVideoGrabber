/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Infrastructure.Downloads
*文件名： HttpDownloadHandler
*版本号： V1.0.0.0
*唯一标识：8368ee85-4b1d-4a77-8f2c-9e6d3a5c1b48
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 00:12:00
*描述：原生 HTTP 下载处理器，面向 mp4 整文件实现多线程分片与断点续传，失败时回退 ffmpeg。
*
*=================================================
*修改标记
*修改时间：2026/9/13 00:12:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using BrowserVideoGrabber.Core.Abstractions;
using BrowserVideoGrabber.Core.Downloads;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Infrastructure.Downloads;

/// <summary>
/// 基于 <see cref="HttpClient"/> 的原生下载处理器，负责 mp4 整文件。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不交给 ffmpeg 一并处理 mp4？</b> ffmpeg 下载单文件是单连接、无断点续传的，
/// 而 mp4 直链恰恰是最适合多线程分片的场景：服务端普遍支持 <c>Accept-Ranges</c>，
/// 分片后带宽利用率与容错能力都显著优于单连接。
/// </para>
/// <para>
/// <b>断点续传的实现</b>：采用「每分片一个 <c>.partN</c> 文件」而非「预分配整文件 + 偏移写入」。
/// 后者的缺陷是中断后文件里存在空洞，无法区分「已下载的零」与「未下载的零」；
/// 前者让每个分片的完整性可以仅凭文件长度判定，续传逻辑因此变得平凡可靠。
/// 全部分片就绪后再顺序拼接为最终文件，把「拼接」与「下载」解耦。
/// </para>
/// <para>
/// <b>取消语义</b>：按设计约定，用户取消会清理全部 <c>.partN</c> 半成品；
/// 而网络错误导致的重试则保留分片，以便续传。二者的区分依据是
/// <paramref name="cancellationToken"/> 是否真的被触发。
/// </para>
/// <para>
/// <b>对注入的 HttpClient 的要求</b>：
/// <list type="bullet">
///   <item><description>
///     请勿启用自动解压（<c>AutomaticDecompression</c>）。分片下载依赖字节偏移，
///     一旦响应体被透明解压，拼接结果就会错位。
///   </description></item>
///   <item><description>
///     请把 <c>Timeout</c> 设为 <see cref="Timeout.InfiniteTimeSpan"/>。
///     该属性约束的是「整个请求的总时长」，会把正常的大文件慢速下载误判为超时；
///     超时控制已由本类的探测超时与空闲超时（见 <see cref="HttpDownloadOptions"/>）承担。
///   </description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class HttpDownloadHandler : IDownloadHandler
{
    private readonly IFileSystem _fileSystem;
    private readonly HttpClient _httpClient;
    private readonly HttpDownloadOptions _options;
    private readonly IDownloadHandler? _fallback;

    /// <summary>
    /// 初始化处理器。
    /// </summary>
    /// <param name="fileSystem">文件系统抽象。</param>
    /// <param name="httpClient">
    /// HTTP 客户端。请勿启用自动解压，参数详见类型备注。
    /// </param>
    /// <param name="options">下载配置。为空时使用默认配置（4 分片）。</param>
    /// <param name="fallback">
    /// 原生链路失败时的回退处理器，通常传入 <c>FfmpegDownloadHandler</c>。
    /// 为 null 时不做回退，直接返回失败。
    /// </param>
    public HttpDownloadHandler(
        IFileSystem fileSystem,
        HttpClient httpClient,
        HttpDownloadOptions? options = null,
        IDownloadHandler? fallback = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? new HttpDownloadOptions();
        _fallback = fallback;
    }

    /// <inheritdoc />
    /// <remarks>本处理器只接管 mp4 整文件，分片与清单类格式由 ffmpeg 处理器负责。</remarks>
    public bool CanHandle(DownloadTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        return task.Format == VideoFormat.Mp4;
    }

    /// <inheritdoc />
    public async Task<DownloadResult> DownloadAsync(
        DownloadTask task,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(progress);

        if (string.IsNullOrWhiteSpace(task.OutputPath))
        {
            return DownloadResult.Fail("下载任务的输出路径为空，无法确定保存位置。", isRetryable: false);
        }

        if (!Uri.TryCreate(task.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return DownloadResult.Fail($"无法识别的下载地址：{task.Url}", isRetryable: false);
        }

        EnsureOutputDirectory(task.OutputPath);

        // 记录本次尝试创建的全部半成品路径，供取消时清理
        var artifacts = new List<string>();

        ProbeResult probe;
        try
        {
            probe = await ProbeAsync(uri, task.Context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            return await TryFallbackAsync(task, progress, cancellationToken, Describe(exception)).ConfigureAwait(false);
        }

        if (!probe.Success)
        {
            // 不可重试的错误（404 失效、地址非法）即便回退 ffmpeg 也无意义，直接失败
            return probe.IsRetryable
                ? await TryFallbackAsync(task, progress, cancellationToken, probe.Error!).ConfigureAwait(false)
                : DownloadResult.Fail(probe.Error!, isRetryable: false);
        }

        if (probe.TotalLength is > 0 && !HasEnoughFreeSpace(task.OutputPath, probe.TotalLength.Value))
        {
            // 空间不足时回退 ffmpeg 同样写不下，直接判定失败并要求用户清理磁盘
            var free = _fileSystem.GetAvailableFreeSpace(GetDirectory(task.OutputPath));
            return DownloadResult.Fail(
                $"磁盘可用空间不足：需要约 {FormatBytes(probe.TotalLength.Value)}，当前可用 {FormatBytes(free)}。",
                isRetryable: false);
        }

        try
        {
            return await DownloadCoreAsync(task, uri, probe, progress, artifacts, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 用户主动取消：清掉半成品，避免遗留大量无人认领的 .part 文件
            Cleanup(artifacts);
            throw;
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            return await TryFallbackAsync(task, progress, cancellationToken, Describe(exception)).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return DownloadResult.Fail(Describe(exception), isRetryable: false);
        }
    }

    /// <summary>
    /// 探测目标资源的长度与是否支持 Range。
    /// </summary>
    /// <param name="uri">资源地址。</param>
    /// <param name="context">请求上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>探测结果。</returns>
    /// <remarks>
    /// 只请求第 0 个字节并立刻中止读取（<see cref="HttpCompletionOption.ResponseHeadersRead"/>），
    /// 因此无论文件多大，探测开销都是一个往返。
    /// </remarks>
    private async Task<ProbeResult> ProbeAsync(Uri uri, RequestContext context, CancellationToken cancellationToken)
    {
        // 探测必须有独立超时：地址僵死时若不设限，任务会永久停在「正在下载」而不给任何反馈
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_options.ProbeTimeout > TimeSpan.Zero)
        {
            timeout.CancelAfter(_options.ProbeTimeout);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        HttpRequestHeaders.Apply(request, context);
        request.Headers.Range = new RangeHeaderValue(0, 0);

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);

        // 206：服务端按分片语义响应，长度取自 Content-Range 的总量字段
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            return ProbeResult.Ok(response.Content.Headers.ContentRange?.Length, supportsRange: true);
        }

        // 200：服务端忽略了 Range，退化为单连接整文件下载
        if (response.IsSuccessStatusCode)
        {
            return ProbeResult.Ok(response.Content.Headers.ContentLength, supportsRange: false);
        }

        return ProbeResult.FromStatus(response.StatusCode);
    }

    /// <summary>
    /// 执行分片下载与拼接。
    /// </summary>
    /// <param name="task">下载任务。</param>
    /// <param name="uri">资源地址。</param>
    /// <param name="probe">探测结果。</param>
    /// <param name="progress">进度上报通道。</param>
    /// <param name="artifacts">半成品路径收集器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>下载结果。</returns>
    private async Task<DownloadResult> DownloadCoreAsync(
        DownloadTask task,
        Uri uri,
        ProbeResult probe,
        IProgress<DownloadProgress> progress,
        List<string> artifacts,
        CancellationToken cancellationToken)
    {
        var total = probe.TotalLength;

        // 服务端明确声明是空文件：产出空文件即视为成功
        if (total == 0L)
        {
            using (_fileSystem.OpenWrite(task.OutputPath, append: false))
            {
                // 仅创建文件
            }

            return DownloadResult.Ok(task.OutputPath, 0);
        }

        var segments = BuildSegments(task.OutputPath, probe);
        foreach (var segment in segments)
        {
            artifacts.Add(segment.PartPath);
        }

        var reporter = new ProgressReporter(segments, total, progress, _options.ProgressIntervalMilliseconds);
        await DownloadSegmentsAsync(task, uri, segments, reporter, cancellationToken).ConfigureAwait(false);

        // 先拼接到临时文件再改名，避免「拼到一半」的半成品被误认为已完成
        var assemblingPath = task.OutputPath + ".assembling";
        artifacts.Add(assemblingPath);

        await using (var destination = _fileSystem.OpenWrite(assemblingPath, append: false))
        {
            foreach (var segment in segments.OrderBy(s => s.Index))
            {
                await using var source = _fileSystem.OpenRead(segment.PartPath);
                await source.CopyToAsync(destination, _options.BufferSize, cancellationToken).ConfigureAwait(false);
            }
        }

        var assembledLength = _fileSystem.GetFileLength(assemblingPath);
        if (total.HasValue && assembledLength != total.Value)
        {
            // 保留分片，让队列的下一次重试可以续传而不是从头再来
            _fileSystem.DeleteFile(assemblingPath);
            return DownloadResult.Fail(
                $"下载未完整：期望 {total.Value} 字节，实际 {assembledLength} 字节。已保留分片，重试将继续续传。",
                isRetryable: true);
        }

        _fileSystem.MoveFile(assemblingPath, task.OutputPath, overwrite: true);
        artifacts.Remove(assemblingPath);

        // 成功之后分片不再需要，立即释放磁盘
        foreach (var segment in segments)
        {
            _fileSystem.DeleteFile(segment.PartPath);
            artifacts.Remove(segment.PartPath);
        }

        reporter.Report(force: true);
        return DownloadResult.Ok(task.OutputPath, assembledLength);
    }

    /// <summary>
    /// 按探测结果切分下载区间。
    /// </summary>
    /// <param name="outputPath">最终输出路径，用于派生分片文件名。</param>
    /// <param name="probe">探测结果。</param>
    /// <returns>分片列表，至少含一个元素。</returns>
    private List<Segment> BuildSegments(string outputPath, ProbeResult probe)
    {
        var total = probe.TotalLength;
        var segments = new List<Segment>();

        var canSegment = total is > 0
            && probe.SupportsRange
            && _options.SegmentCount > 1
            && total.Value > _options.MinimumSegmentBytes;

        if (canSegment)
        {
            // 既不超过用户设定的并发数，也不让单个分片小于阈值，避免碎片化
            var count = Math.Max(1, (int)Math.Min(_options.SegmentCount, total!.Value / Math.Max(1L, _options.MinimumSegmentBytes)));
            var chunk = (total.Value + count - 1) / count;

            for (var index = 0; index < count; index++)
            {
                var start = index * chunk;
                if (start >= total.Value)
                {
                    break;
                }

                segments.Add(new Segment
                {
                    Index = segments.Count,
                    Start = start,
                    End = Math.Min(start + chunk - 1, total.Value - 1),
                    PartPath = BuildPartPath(outputPath, segments.Count),
                    Resumable = true
                });
            }
        }

        if (segments.Count == 0)
        {
            // 单连接：长度未知时 End 为 null，表示一直读到流结束
            segments.Add(new Segment
            {
                Index = 0,
                Start = 0,
                End = total.HasValue ? total.Value - 1 : null,
                PartPath = BuildPartPath(outputPath, 0),
                Resumable = probe.SupportsRange
            });
        }

        return segments;
    }

    /// <summary>
    /// 并发下载全部分片。
    /// </summary>
    /// <param name="task">下载任务。</param>
    /// <param name="uri">资源地址。</param>
    /// <param name="segments">分片列表。</param>
    /// <param name="reporter">进度上报器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// 采用「首个失败即取消其余分片」的快速失败策略：网络整体不可用时，
    /// 让剩余分片继续跑完只是白白等待，反而拖长用户看到错误提示的时间。
    /// </remarks>
    private async Task DownloadSegmentsAsync(
        DownloadTask task,
        Uri uri,
        List<Segment> segments,
        ProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        if (segments.Count == 1)
        {
            await DownloadSegmentAsync(task, uri, segments[0], reporter, cancellationToken).ConfigureAwait(false);
            return;
        }

        var concurrency = Math.Min(_options.SegmentCount, segments.Count);
        using var gate = new SemaphoreSlim(concurrency, concurrency);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Exception? firstError = null;

        var workers = new List<Task>(segments.Count);
        foreach (var segment in segments)
        {
            workers.Add(Task.Run(async () =>
            {
                try
                {
                    await gate.WaitAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                try
                {
                    if (Volatile.Read(ref firstError) is not null)
                    {
                        return;
                    }

                    await DownloadSegmentAsync(task, uri, segment, reporter, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // 由外层统一判定是「用户取消」还是「兄弟分片失败触发的快速失败」
                }
                catch (Exception exception)
                {
                    Interlocked.CompareExchange(ref firstError, exception, null);
                    linked.Cancel();
                }
                finally
                {
                    gate.Release();
                }
            }));
        }

        await Task.WhenAll(workers).ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (firstError is not null)
        {
            throw firstError;
        }
    }

    /// <summary>
    /// 下载单个分片，并对瞬时错误做有限次就地重试。
    /// </summary>
    /// <param name="task">下载任务。</param>
    /// <param name="uri">资源地址。</param>
    /// <param name="segment">目标分片。</param>
    /// <param name="reporter">进度上报器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task DownloadSegmentAsync(
        DownloadTask task,
        Uri uri,
        Segment segment,
        ProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await DownloadSegmentOnceAsync(task, uri, segment, reporter, cancellationToken).ConfigureAwait(false);
                reporter.Report(force: true);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsTransient(exception) && attempt < _options.MaxRetryPerSegment)
            {
                // 退避后就地重试：已落盘的字节会被下一次请求以 Range 续上
                await Task.Delay(TimeSpan.FromMilliseconds(200 * (attempt + 1)), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 下载单个分片的一次尝试。
    /// </summary>
    /// <param name="task">下载任务。</param>
    /// <param name="uri">资源地址。</param>
    /// <param name="segment">目标分片。</param>
    /// <param name="reporter">进度上报器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task DownloadSegmentOnceAsync(
        DownloadTask task,
        Uri uri,
        Segment segment,
        ProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        var existing = _fileSystem.FileExists(segment.PartPath) ? _fileSystem.GetFileLength(segment.PartPath) : 0L;

        // 断点续传：该分片上次已完整落盘，直接复用
        if (segment.ExpectedLength is { } expected && existing == expected)
        {
            Interlocked.Exchange(ref segment.Downloaded, existing);
            return;
        }

        // 不支持 Range，或半成品长度异常（超过应有长度），都只能从头重下
        if (!segment.Resumable || (segment.ExpectedLength is { } limit && existing > limit))
        {
            if (existing > 0)
            {
                _fileSystem.DeleteFile(segment.PartPath);
            }

            existing = 0L;
        }

        // 分片起点 + 已有长度 = 本次请求的起始偏移
        var from = segment.Start + existing;

        // 空闲超时：每读到数据就重置，只有「长时间一个字节都没到」才判定连接僵死
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_options.IdleTimeout > TimeSpan.Zero)
        {
            idle.CancelAfter(_options.IdleTimeout);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        HttpRequestHeaders.Apply(request, task.Context);

        // 仅在「需要续传」或「服务端确认支持 Range」时才附加分段头：
        // 对不支持 Range 的服务端发送该头毫无意义，部分老旧网关反而会因此直接报错
        if (existing > 0 || (segment.Resumable && segment.End.HasValue))
        {
            request.Headers.Range = new RangeHeaderValue(from, segment.End);
        }

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, idle.Token)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.OK && from > 0)
        {
            // 探测阶段声称支持 Range，实际却整文件重发：续传假设不成立，交由上层处理
            throw new HttpRequestException("服务器未按 Range 返回分段数据，无法继续断点续传。");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"服务器返回异常状态：HTTP {(int)response.StatusCode} {response.ReasonPhrase}。");
        }

        await using var source = await response.Content.ReadAsStreamAsync(idle.Token).ConfigureAwait(false);
        await using var destination = _fileSystem.OpenWrite(segment.PartPath, append: existing > 0);

        var buffer = new byte[_options.BufferSize];
        var written = existing;
        Interlocked.Exchange(ref segment.Downloaded, written);

        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), idle.Token).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            if (_options.IdleTimeout > TimeSpan.Zero)
            {
                // 有数据到达即视为连接存活，把空闲计时重新拉满
                idle.CancelAfter(_options.IdleTimeout);
            }

            // 写盘使用原始取消令牌：磁盘不是可能僵死的网络资源，不应被空闲超时打断
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            written += read;
            Interlocked.Exchange(ref segment.Downloaded, written);
            reporter.Report();
        }

        if (segment.ExpectedLength is { } wanted && written != wanted)
        {
            throw new IOException($"分片 {segment.Index} 数据不完整：期望 {wanted} 字节，实际 {written} 字节。");
        }
    }

    /// <summary>
    /// 原生链路失败后回退到备用处理器（通常为 ffmpeg）。
    /// </summary>
    /// <param name="task">下载任务。</param>
    /// <param name="progress">进度上报通道。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="primaryError">原生链路的失败原因。</param>
    /// <returns>回退结果；未配置回退时返回原生链路的失败。</returns>
    private async Task<DownloadResult> TryFallbackAsync(
        DownloadTask task,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken,
        string primaryError)
    {
        if (_fallback is null)
        {
            return DownloadResult.Fail(primaryError, isRetryable: true);
        }

        DownloadResult fallbackResult;
        try
        {
            fallbackResult = await _fallback.DownloadAsync(task, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }

        if (fallbackResult.Success)
        {
            return fallbackResult;
        }

        return DownloadResult.Fail(
            $"原生下载失败（{primaryError}）已回退 ffmpeg，仍然失败（{fallbackResult.Error}）",
            isRetryable: fallbackResult.IsRetryable,
            isDrmProtected: fallbackResult.IsDrmProtected);
    }

    /// <summary>
    /// 判断异常是否属于「值得重试」的瞬时错误。
    /// </summary>
    /// <param name="exception">待判定的异常。</param>
    /// <returns>属于瞬时错误返回 true。</returns>
    /// <remarks>
    /// 计入 <see cref="TaskCanceledException"/> 是为了覆盖 HttpClient 超时：
    /// 超时与用户取消同为取消异常，但前者可重试、后者必须向上传播，二者的区分在外层完成。
    /// </remarks>
    private static bool IsTransient(Exception exception)
        => exception is HttpRequestException
            or TaskCanceledException
            or IOException
            or SocketException;

    /// <summary>
    /// 把异常转换为面向用户的中文描述。
    /// </summary>
    /// <param name="exception">异常。</param>
    /// <returns>中文描述。</returns>
    private static string Describe(Exception exception) => exception switch
    {
        HttpRequestException => $"网络请求失败：{exception.Message}",
        TaskCanceledException => "请求超时：服务器在限定时间内未响应。",
        SocketException => $"网络连接中断：{exception.Message}",
        IOException => $"数据读写失败：{exception.Message}",
        _ => $"下载出错：{exception.Message}"
    };

    /// <summary>
    /// 校验目标磁盘剩余空间是否满足需求。
    /// </summary>
    /// <param name="outputPath">输出路径。</param>
    /// <param name="requiredBytes">所需字节数。</param>
    /// <returns>空间充足返回 true。</returns>
    private bool HasEnoughFreeSpace(string outputPath, long requiredBytes)
        => _fileSystem.GetAvailableFreeSpace(GetDirectory(outputPath)) >= requiredBytes;

    /// <summary>
    /// 确保输出目录存在。
    /// </summary>
    /// <param name="outputPath">输出路径。</param>
    private void EnsureOutputDirectory(string outputPath)
    {
        var directory = GetDirectory(outputPath);
        if (!string.IsNullOrEmpty(directory) && !_fileSystem.DirectoryExists(directory))
        {
            _fileSystem.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// 取得输出路径所在目录，容错非法路径字符。
    /// </summary>
    /// <param name="outputPath">输出路径。</param>
    /// <returns>目录路径；无法判定时返回空字符串。</returns>
    private static string GetDirectory(string outputPath)
    {
        try
        {
            return Path.GetDirectoryName(outputPath) ?? string.Empty;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 派生分片临时文件路径。
    /// </summary>
    /// <param name="outputPath">最终输出路径。</param>
    /// <param name="index">分片序号。</param>
    /// <returns>形如 <c>movie.mp4.part0</c> 的路径。</returns>
    private static string BuildPartPath(string outputPath, int index) => $"{outputPath}.part{index}";

    /// <summary>
    /// 删除一组半成品文件。
    /// </summary>
    /// <param name="paths">待删除路径。</param>
    private void Cleanup(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                _fileSystem.DeleteFile(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 清理失败不应掩盖原始的取消/失败原因
            }
        }
    }

    /// <summary>
    /// 把字节数格式化为便于阅读的文本。
    /// </summary>
    /// <param name="bytes">字节数。</param>
    /// <returns>形如 <c>12.5 MB</c> 的文本。</returns>
    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    /// <summary>
    /// 分片描述。
    /// </summary>
    private sealed class Segment
    {
        /// <summary>分片序号，同时决定拼接顺序。</summary>
        public required int Index { get; init; }

        /// <summary>起始字节偏移（含）。</summary>
        public required long Start { get; init; }

        /// <summary>结束字节偏移（含）；为 null 表示读到流结束（长度未知）。</summary>
        public long? End { get; init; }

        /// <summary>分片临时文件路径。</summary>
        public required string PartPath { get; init; }

        /// <summary>服务端是否支持 Range；为 false 时不尝试续传。</summary>
        public bool Resumable { get; init; }

        /// <summary>已落盘字节数，由下载线程以原子方式更新。</summary>
        public long Downloaded;

        /// <summary>分片应有的字节数；长度未知时为 null。</summary>
        public long? ExpectedLength => End.HasValue ? End.Value - Start + 1 : null;
    }

    /// <summary>
    /// 探测结果。
    /// </summary>
    private sealed record ProbeResult
    {
        /// <summary>探测是否成功。</summary>
        public bool Success { get; init; }

        /// <summary>资源总字节数；未知时为 null。</summary>
        public long? TotalLength { get; init; }

        /// <summary>服务端是否支持 Range 分段。</summary>
        public bool SupportsRange { get; init; }

        /// <summary>失败原因（中文）。</summary>
        public string? Error { get; init; }

        /// <summary>该失败是否值得重试（含回退 ffmpeg）。</summary>
        public bool IsRetryable { get; init; }

        /// <summary>构造成功结果。</summary>
        /// <param name="totalLength">资源总字节数。</param>
        /// <param name="supportsRange">是否支持 Range。</param>
        /// <returns>成功结果。</returns>
        public static ProbeResult Ok(long? totalLength, bool supportsRange)
            => new() { Success = true, TotalLength = totalLength, SupportsRange = supportsRange };

        /// <summary>
        /// 依据 HTTP 状态码构造失败结果，并给出针对性的中文提示。
        /// </summary>
        /// <param name="statusCode">响应状态码。</param>
        /// <returns>失败结果。</returns>
        public static ProbeResult FromStatus(HttpStatusCode statusCode)
        {
            var code = (int)statusCode;

            return statusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new ProbeResult
                {
                    Error = $"目标服务器拒绝访问（HTTP {code}）。请确认已在浏览器中登录该站点，然后重新嗅探并下载。",
                    IsRetryable = true
                },
                HttpStatusCode.NotFound or HttpStatusCode.Gone => new ProbeResult
                {
                    Error = $"资源地址已失效（HTTP {code}），通常为动态签名过期。请在页面重新播放后再次嗅探。",
                    IsRetryable = false
                },
                HttpStatusCode.RequestedRangeNotSatisfiable => new ProbeResult
                {
                    Error = "服务器拒绝了分段请求（HTTP 416）。",
                    IsRetryable = true
                },
                _ => new ProbeResult
                {
                    Error = code >= 500
                        ? $"服务器暂时不可用（HTTP {code}），请稍后重试。"
                        : $"服务器返回异常状态：HTTP {code}。",
                    IsRetryable = true
                }
            };
        }
    }

    /// <summary>
    /// 进度上报器：聚合各分片进度并按时间节流上报。
    /// </summary>
    /// <remarks>
    /// 分片下载会产生大量细碎的字节增量，若逐次上报会让 UI 线程疲于奔命。
    /// 这里以「固定最小间隔 + 分片完成时强制上报」的方式在流畅度与开销间取平衡。
    /// </remarks>
    private sealed class ProgressReporter
    {
        private readonly IReadOnlyList<Segment> _segments;
        private readonly long? _total;
        private readonly IProgress<DownloadProgress> _progress;
        private readonly long _minIntervalStopwatchTicks;
        private readonly object _sync = new();

        private long _lastReportTimestamp;
        private long _lastSampleTimestamp;
        private long _lastBytes;

        /// <summary>
        /// 初始化进度上报器。
        /// </summary>
        /// <param name="segments">分片列表。</param>
        /// <param name="total">资源总字节数。</param>
        /// <param name="progress">下游进度通道。</param>
        /// <param name="intervalMilliseconds">最小上报间隔（毫秒）。</param>
        public ProgressReporter(
            IReadOnlyList<Segment> segments,
            long? total,
            IProgress<DownloadProgress> progress,
            int intervalMilliseconds)
        {
            _segments = segments;
            _total = total;
            _progress = progress;

            // 把「毫秒」换算为 Stopwatch 的计时单位，避免每次上报都做浮点运算
            var intervalTicks = TimeSpan.FromMilliseconds(Math.Max(1, intervalMilliseconds)).Ticks;
            _minIntervalStopwatchTicks = intervalTicks * Stopwatch.Frequency / TimeSpan.TicksPerSecond;

            _lastSampleTimestamp = Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// 上报一次进度。
        /// </summary>
        /// <param name="force">为 true 时忽略节流间隔，立即上报（用于分片完成等关键节点）。</param>
        public void Report(bool force = false)
        {
            var now = Stopwatch.GetTimestamp();

            lock (_sync)
            {
                if (!force && now - _lastReportTimestamp < _minIntervalStopwatchTicks)
                {
                    return;
                }

                _lastReportTimestamp = now;
            }

            long downloaded = 0;
            foreach (var segment in _segments)
            {
                downloaded += Interlocked.Read(ref segment.Downloaded);
            }

            var elapsedSeconds = (now - _lastSampleTimestamp) / (double)Stopwatch.Frequency;
            var bytesPerSecond = 0d;

            // 采样窗口太短会让速度读数剧烈跳动，因此只在窗口足够长时才更新
            if (elapsedSeconds > 0.05)
            {
                bytesPerSecond = (downloaded - _lastBytes) / elapsedSeconds;
                _lastSampleTimestamp = now;
                _lastBytes = downloaded;
            }

            var percent = 0d;
            if (_total is > 0)
            {
                percent = Math.Clamp(downloaded * 100d / _total.Value, 0d, 100d);
            }

            _progress.Report(new DownloadProgress
            {
                Percent = percent,
                HasTotal = _total.HasValue,
                DownloadedBytes = downloaded,
                TotalBytes = _total,
                BytesPerSecond = bytesPerSecond
            });
        }
    }
}
