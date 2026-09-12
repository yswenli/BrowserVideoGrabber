/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Infrastructure.Downloads
*文件名： FfmpegDownloadHandler
*版本号： V1.0.0.0
*唯一标识：6632565a-3c78-41b2-abd9-58edbc7d7c1a
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 23:18:00
*描述：基于 ffmpeg 的下载处理器，负责 m3u8 / ts / m4s / mpd 的分片拉取、解密与合并。
*
*=================================================
*修改标记
*修改时间：2026/9/12 23:18:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Text;
using BrowserVideoGrabber.Core.Abstractions;
using BrowserVideoGrabber.Core.Downloads;
using BrowserVideoGrabber.Core.Ffmpeg;
using BrowserVideoGrabber.Core.Models;
using BrowserVideoGrabber.Infrastructure.Ffmpeg;

namespace BrowserVideoGrabber.Infrastructure.Downloads;

/// <summary>
/// 基于 ffmpeg 的下载处理器。
/// </summary>
/// <remarks>
/// <para>
/// 处理 m3u8 / ts / m4s / mpd 四类资源。选择交给 ffmpeg 而不是自研下载器，
/// 原因是这几类格式的分片拉取、AES-128 解密、fMP4 拼装、时间戳修正逻辑极其琐碎，
/// 自研的出错概率远高于直接使用成熟工具。
/// </para>
/// <para>
/// <b>DRM 预检</b>：在启动 ffmpeg 之前会先拉取 m3u8 清单做一次解析，
/// 若发现 SAMPLE-AES 或 SESSION-KEY 则立即失败。这样做的价值在于给出准确的错误原因：
/// 若让 ffmpeg 去跑，DRM 内容会以一堆难以理解的解复用错误告终，
/// 用户只会看到「下载失败」而无从判断，还会白白耗尽全部重试次数。
/// </para>
/// </remarks>
public sealed class FfmpegDownloadHandler : IDownloadHandler
{
    /// <summary>错误信息中保留的 stderr 行数上限，避免错误描述过长。</summary>
    private const int MaxErrorLines = 20;

    private readonly IProcessRunner _processRunner;
    private readonly IFileSystem _fileSystem;
    private readonly FfmpegLocator _locator;
    private readonly FfmpegOptions _options;
    private readonly HttpClient? _httpClient;

    /// <summary>
    /// 初始化处理器。
    /// </summary>
    /// <param name="processRunner">进程运行器。</param>
    /// <param name="fileSystem">文件系统抽象。</param>
    /// <param name="locator">ffmpeg 路径定位器。</param>
    /// <param name="options">ffmpeg 调用配置。为空时使用默认配置。</param>
    /// <param name="httpClient">
    /// 用于 DRM 预检的 HTTP 客户端。为 null 时跳过预检，
    /// 直接交给 ffmpeg 处理（此时 DRM 内容会以 ffmpeg 的报错形式失败）。
    /// </param>
    public FfmpegDownloadHandler(
        IProcessRunner processRunner,
        IFileSystem fileSystem,
        FfmpegLocator locator,
        FfmpegOptions? options = null,
        HttpClient? httpClient = null)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _options = options ?? new FfmpegOptions();
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public bool CanHandle(DownloadTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        return task.Format is VideoFormat.M3u8 or VideoFormat.Ts or VideoFormat.M4s or VideoFormat.Mpd;
    }

    /// <inheritdoc />
    public async Task<DownloadResult> DownloadAsync(
        DownloadTask task,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(progress);

        var executablePath = _locator.Locate();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return DownloadResult.Fail(
                "未找到 ffmpeg.exe。请在「设置」中指定 ffmpeg 路径，或将其所在目录加入系统 PATH 环境变量。",
                isRetryable: false);
        }

        // 分片类格式在启动进程前先做一次 DRM 预检
        if (task.Format == VideoFormat.M3u8)
        {
            var playlist = await TryProbePlaylistAsync(task, cancellationToken).ConfigureAwait(false);
            if (playlist?.IsDrmProtected == true)
            {
                return DownloadResult.Fail(
                    "该内容受 DRM 保护（检测到 SAMPLE-AES 或 SESSION-KEY），本工具无法下载受保护的加密内容。",
                    isRetryable: false,
                    isDrmProtected: true);
            }
        }

        var parser = new FfmpegProgressParser();
        var diagnosticLines = new List<string>();

        var arguments = FfmpegArgumentBuilder.Build(task, _options);

        var exitCode = await _processRunner.RunAsync(
            executablePath,
            arguments,
            line => HandleStandardErrorLine(line, parser, progress, diagnosticLines),
            cancellationToken).ConfigureAwait(false);

        if (exitCode != 0)
        {
            return DownloadResult.Fail(BuildErrorDetail(exitCode, diagnosticLines), isRetryable: true);
        }

        if (!_fileSystem.FileExists(task.OutputPath))
        {
            return DownloadResult.Fail($"ffmpeg 已正常退出，但未生成输出文件：{task.OutputPath}", isRetryable: true);
        }

        return DownloadResult.Ok(task.OutputPath, _fileSystem.GetFileLength(task.OutputPath));
    }

    /// <summary>
    /// 处理一行 stderr 输出：能解析为进度则上报，否则留作诊断信息。
    /// </summary>
    /// <param name="line">stderr 的一行。</param>
    /// <param name="parser">进度解析器。</param>
    /// <param name="progress">进度上报通道。</param>
    /// <param name="diagnosticLines">诊断行缓冲区。</param>
    private static void HandleStandardErrorLine(
        string line,
        FfmpegProgressParser parser,
        IProgress<DownloadProgress> progress,
        List<string> diagnosticLines)
    {
        var parsed = parser.Feed(line);

        if (parsed is not null)
        {
            progress.Report(parsed);
            return;
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (diagnosticLines)
        {
            diagnosticLines.Add(line.Trim());

            // 只保留最近的若干行：ffmpeg 失败时真正有用的信息集中在末尾
            if (diagnosticLines.Count > MaxErrorLines)
            {
                diagnosticLines.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// 构建失败描述文本。
    /// </summary>
    /// <param name="exitCode">ffmpeg 退出码。</param>
    /// <param name="diagnosticLines">诊断行缓冲区。</param>
    /// <returns>面向用户的中文失败描述。</returns>
    /// <remarks>
    /// 除了带上退出码与 stderr 尾部，还会针对最常见的 401/403 给出针对性建议：
    /// 这类失败的根因几乎总是缺少有效 Cookie 或 Referer，而非文件本身有问题。
    /// </remarks>
    private static string BuildErrorDetail(int exitCode, List<string> diagnosticLines)
    {
        string tail;

        lock (diagnosticLines)
        {
            tail = diagnosticLines.Count == 0
                ? "（ffmpeg 未输出任何错误信息）"
                : string.Join(" | ", diagnosticLines.TakeLast(3));
        }

        var builder = new StringBuilder();
        builder.Append("ffmpeg 执行失败（退出码 ").Append(exitCode).Append("）：").Append(tail);

        if (tail.Contains("403", StringComparison.Ordinal) || tail.Contains("401", StringComparison.Ordinal))
        {
            builder.Append("。该错误通常表示缺少有效的 Referer / Cookie，请在浏览器中登录该站点后重新嗅探并下载。");
        }
        else if (tail.Contains("404", StringComparison.Ordinal))
        {
            builder.Append("。该错误通常表示地址已失效（动态签名过期），请在页面重新播放后再次嗅探。");
        }

        return builder.ToString();
    }

    /// <summary>
    /// 拉取并解析 m3u8 清单，用于 DRM 预检。
    /// </summary>
    /// <param name="task">下载任务。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>解析结果；无法预检时返回 null。</returns>
    private async Task<M3u8Playlist?> TryProbePlaylistAsync(DownloadTask task, CancellationToken cancellationToken)
    {
        if (_httpClient is null)
        {
            return null;
        }

        if (!Uri.TryCreate(task.Url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            ApplyRequestHeaders(request, task.Context);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // 预检失败不阻断下载：可能是仅不支持 HEAD/GET 探测，也可能需要特殊头，
                // 让 ffmpeg 带着完整请求头去尝试才是更可靠的路径
                return null;
            }

            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return M3u8Parser.Parse(text, uri);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 用户主动取消必须向上传播，不能被当成「预检失败」吞掉
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// 把请求上下文写入 HTTP 请求头。
    /// </summary>
    /// <param name="request">目标请求。</param>
    /// <param name="context">请求上下文。</param>
    private static void ApplyRequestHeaders(HttpRequestMessage request, RequestContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.Referer)
            && Uri.TryCreate(context.Referer, UriKind.Absolute, out var referer))
        {
            request.Headers.Referrer = referer;
        }

        if (!string.IsNullOrWhiteSpace(context.UserAgent))
        {
            // 使用 TryAddWithoutValidation：UA 串中常含括号与分号，严格校验会直接抛异常
            request.Headers.TryAddWithoutValidation("User-Agent", context.UserAgent);
        }

        if (!string.IsNullOrWhiteSpace(context.Cookie))
        {
            request.Headers.TryAddWithoutValidation("Cookie", context.Cookie);
        }

        foreach (var header in context.ExtraHeaders)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }
}
