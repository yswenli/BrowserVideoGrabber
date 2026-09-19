/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Infrastructure.Downloads
*文件名： HlsDownloadHandler
*版本号： V1.0.0.0
*唯一标识：9f2c4d17-6e83-4b21-9a4f-0c7b3e2d1a55
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 04:05:00
*描述：HLS 原生下载处理器，编排「取清单→建计划→C# 逐片取片+校验+解密→拼接→ffmpeg 仅做 -c copy 合并」。
*
*=================================================
*修改标记
*修改时间：2026/9/13 04:05:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Globalization;
using BrowserVideoGrabber.Core.Abstractions;
using BrowserVideoGrabber.Core.Downloads;
using BrowserVideoGrabber.Core.Ffmpeg;
using BrowserVideoGrabber.Core.Models;
using BrowserVideoGrabber.Infrastructure.Ffmpeg;

namespace BrowserVideoGrabber.Infrastructure.Downloads;

/// <summary>
/// HLS 原生下载处理器：把「下载链路切到 C#」的整条编排收口在一处。
/// </summary>
/// <remarks>
/// <para>
/// 这是「污染无法移除时接受部分成功」决策的落点。它替代 ffmpeg 直接拉取分片，
/// 改为由 C# 逐片抓取、逐片校验（图片/文本类型、TS 同步字、重复指纹）、对 AES-128 流逐片解密，
/// 再把成功分片拼接为统一 <c>.ts</c>，最后才调用 ffmpeg 做 <c>-c copy</c> 重封装为 mp4。
/// 由于校验与解密都发生在 C# 一侧，坏分片在「写进成品之前」就被丢弃，ffmpeg 直连
/// 「退出码 0 却产出一个被占位污染的坏文件」的隐患因此被彻底消除。
/// </para>
/// <para>
/// <b>与 ffmpeg 处理器的边界</b>：本处理器只接管点播 m3u8（<see cref="VideoFormat.M3u8"/>）。
/// 直播流（清单缺 <c>#EXT-X-ENDLIST</c>）因分片列表没有终点、无法规划完整下载，
/// 直接改走 <see cref="_fallback"/>（ffmpeg 录制）；DRM 保护内容直接拒绝并告知用户。
/// </para>
/// <para>
/// <b>每次下载都重建分片下载器与校验器</b>：<see cref="HlsSegmentDownloader"/> 内部持有
/// <c>_fatalError</c> 字段且 <see cref="SegmentContentValidator"/> 的重复指纹登记表是跨片共享的，
/// 二者都不具备跨并发调用安全性。因此这里每次执行都新建实例，把重复指纹检测的作用域限制在
/// 单次下载内部，既避免任务间串味，也避免校验器无限累积指纹造成内存泄漏。
/// </para>
/// </remarks>
public sealed class HlsDownloadHandler : IDownloadHandler
{
    private readonly IMediaFetcher _fetcher;
    private readonly IFileSystem _fileSystem;
    private readonly FfmpegLocator _locator;
    private readonly IProcessRunner _processRunner;
    private readonly FfmpegOptions _options;
    private readonly IDownloadHandler? _fallback;

    /// <summary>
    /// 初始化处理器。
    /// </summary>
    /// <param name="fetcher">媒体抓取实现（生产环境为 <see cref="HttpMediaFetcher"/>）。</param>
    /// <param name="fileSystem">文件系统抽象。</param>
    /// <param name="locator">ffmpeg 路径定位器（仅用于最后的重封装合并）。</param>
    /// <param name="processRunner">进程运行器（用于运行 ffmpeg）。</param>
    /// <param name="options">ffmpeg 调用配置。为空时使用默认配置。</param>
    /// <param name="fallback">直播流回退处理器，通常传入 <c>FfmpegDownloadHandler</c>。为 null 时直播流直接失败。</param>
    public HlsDownloadHandler(
        IMediaFetcher fetcher,
        IFileSystem fileSystem,
        FfmpegLocator locator,
        IProcessRunner processRunner,
        FfmpegOptions? options = null,
        IDownloadHandler? fallback = null)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _options = options ?? new FfmpegOptions();
        _fallback = fallback;
    }

    /// <inheritdoc />
    /// <remarks>本处理器只接管点播 m3u8；直播与兜底由 ffmpeg 处理器负责。</remarks>
    public bool CanHandle(DownloadTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return task.Format == VideoFormat.M3u8;
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

        if (!Uri.TryCreate(task.Url, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            return DownloadResult.Fail($"无法识别的下载地址：{task.Url}", isRetryable: false);
        }

        EnsureOutputDirectory(task.OutputPath);

        // 分片与拼接中间产物统一落在此工作目录，下载结束后整体清理。
        // 目录延迟到「确定要取片」时才创建：清单获取失败、无可用变体、直播流、DRM、
        // 解析失败这些早期返回路径都不该留下一个空目录。
        var workDirectory = task.OutputPath + ".hls_segments";
        var assembledPath = Path.Combine(workDirectory, "assembled.ts");

        // 1) 取顶层清单：主清单需再选一档清晰度取媒体清单
        var top = await FetchTextAsync(task.Url, task.Context, cancellationToken).ConfigureAwait(false);
        if (!top.Ok)
        {
            return DownloadResult.Fail(top.Error, top.Retryable);
        }

        var topPlaylist = M3u8Parser.Parse(top.Text!, baseUri);
        HlsPlanResult planResult;

        if (topPlaylist.IsMasterPlaylist)
        {
            var variant = topPlaylist.BestVariant;
            if (variant is null)
            {
                return DownloadResult.Fail("主播放列表未包含任何可用的清晰度变体。", isRetryable: false);
            }

            var media = await FetchTextAsync(variant.Uri, task.Context, cancellationToken).ConfigureAwait(false);
            if (!media.Ok)
            {
                return DownloadResult.Fail(media.Error, media.Retryable);
            }

            planResult = HlsPlanBuilder.Build(
                media.Text,
                new Uri(variant.Uri, UriKind.Absolute),
                variant.Resolution,
                variant.Bandwidth);
        }
        else
        {
            planResult = HlsPlanBuilder.Build(top.Text, baseUri);
        }

        // 2) 不可下载的三类情形分流处理
        if (planResult.IsLive)
        {
            // 直播流没有终点，C# 无法规划完整下载，改走 ffmpeg 录制
            return await FallbackAsync(task, progress, cancellationToken, planResult.Error).ConfigureAwait(false);
        }

        if (planResult.IsDrmProtected)
        {
            return DownloadResult.Fail(planResult.Error ?? "该内容受 DRM 保护，无法下载。", isRetryable: false, isDrmProtected: true);
        }

        if (!planResult.Success || planResult.Plan is null)
        {
            return DownloadResult.Fail(planResult.Error ?? "无法解析播放列表。", planResult.IsRetryable);
        }

        var plan = planResult.Plan;

        // 走到这里才真正要取片，此时创建工作目录。
        // 显式建目录而不是依赖 OpenWrite 隐式创建：清理阶段需要「目录确实存在过」这一事实，
        // 否则目录删除无法被验证，残留也就无从察觉
        _fileSystem.CreateDirectory(workDirectory);

        try
        {
            return await DownloadSegmentsAsync(task, progress, cancellationToken, plan, workDirectory, assembledPath).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 取片 / 拼接 / 封装途中抛出 IO 等异常时，工作目录已创建且可能残留半成品，
            // 若不在此兜底清理，输出目录会留下一个 .hls_segments 垃圾目录。
            // 取消（OperationCanceledException）不在此清理：由 DownloadQueue 决定是否保留以支持续传。
            try
            {
                _fileSystem.DeleteDirectory(workDirectory);
            }
            catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
            {
                // 清理失败不掩盖原始异常
            }

            throw;
        }
    }

    /// <summary>
    /// 完成「取片 → 校验 → 解密 → 拼接 → ffmpeg 封装」的核心流程。
    /// </summary>
    /// <param name="task">下载任务。</param>
    /// <param name="progress">进度上报通道。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="plan">取片计划。</param>
    /// <param name="workDirectory">分片与中间产物工作目录。</param>
    /// <param name="assembledPath">拼接产物路径。</param>
    /// <returns>下载结果。</returns>
    /// <remarks>
    /// 从 <see cref="DownloadAsync"/> 中拆出，是为了给「取片及之后的异常」一个统一的清理兜底点：
    /// 早期返回路径（清单失败、无变体、直播、DRM、解析失败）在工作目录创建之前就已返回，
    /// 不会留下空目录；而进入本方法后抛出的异常由外层 catch 负责清理。
    /// </remarks>
    private async Task<DownloadResult> DownloadSegmentsAsync(
        DownloadTask task,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken,
        HlsDownloadPlan plan,
        string workDirectory,
        string assembledPath)
    {
        // 3) C# 逐片取片 + 校验 + 解密（每次下载新建下载器与校验器，见类型备注）
        var validator = new SegmentContentValidator();
        var segmentDownloader = new HlsSegmentDownloader(_fetcher, validator, _fileSystem);
        var report = await segmentDownloader.DownloadAsync(plan, task.Context, workDirectory, cancellationToken).ConfigureAwait(false);

        // 全部失败：要么被 CDN 反爬占位污染，要么地址已失效。回退 ffmpeg 同样拿不到真实分片，直接判定失败
        if (report.FetchedCount == 0)
        {
            CleanupWorkDirectory(workDirectory, report.FetchedFilePaths, assembledPath);
            return DownloadResult.Fail(BuildAllFailedDetail(report), isRetryable: false);
        }

        // 4) 把成功分片按播放顺序裸拼为单个 TS
        var assembler = new HlsAssembler(_fileSystem);
        await assembler.ConcatenateAsync(report.FetchedFilePaths, assembledPath, cancellationToken).ConfigureAwait(false);

        // 5) ffmpeg 仅做 -c copy 重封装为 mp4
        var executablePath = _locator.Locate();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            CleanupWorkDirectory(workDirectory, report.FetchedFilePaths, assembledPath);
            return DownloadResult.Fail(
                "未找到 ffmpeg.exe，无法将分片合并为 mp4。请在「设置」中指定 ffmpeg 路径，或将其所在目录加入系统 PATH。",
                isRetryable: false);
        }

        var arguments = FfmpegArgumentBuilder.BuildLocalRemux(assembledPath, task.OutputPath, _options);
        var parser = new FfmpegProgressParser();
        var exitCode = await _processRunner.RunAsync(
            executablePath,
            arguments,
            line =>
            {
                var parsed = parser.Feed(line);
                if (parsed is not null)
                {
                    progress.Report(parsed);
                }
            },
            cancellationToken).ConfigureAwait(false);

        if (exitCode != 0)
        {
            CleanupWorkDirectory(workDirectory, report.FetchedFilePaths, assembledPath);
            return DownloadResult.Fail($"ffmpeg 合并失败（退出码 {exitCode}），分片可能不完整或格式异常。", isRetryable: true);
        }

        if (!_fileSystem.FileExists(task.OutputPath))
        {
            CleanupWorkDirectory(workDirectory, report.FetchedFilePaths, assembledPath);
            return DownloadResult.Fail("ffmpeg 已正常退出，但未生成输出文件。", isRetryable: true);
        }

        // 6) 成功：完整则 Ok，有缺失则 OkPartial（部分成功）
        var outputBytes = _fileSystem.GetFileLength(task.OutputPath);
        CleanupWorkDirectory(workDirectory, report.FetchedFilePaths, assembledPath);

        if (report.MissingIntervals.Count > 0)
        {
            return DownloadResult.OkPartial(task.OutputPath, outputBytes, BuildPartialDetail(report));
        }

        return DownloadResult.Ok(task.OutputPath, outputBytes);
    }

    /// <summary>
    /// 取回清单文本。
    /// </summary>
    /// <param name="url">清单地址。</param>
    /// <param name="context">请求上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功时 <see cref="Ok"/> 为 true 且携带文本；失败时携带中文错误与是否可重试。</returns>
    private async Task<(bool Ok, string? Text, string Error, bool Retryable)> FetchTextAsync(
        string url,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        var result = await _fetcher.GetStringAsync(url, context, cancellationToken).ConfigureAwait(false);
        if (result.Success && result.Text is not null)
        {
            return (true, result.Text, string.Empty, true);
        }

        return (false, null, result.Error ?? "获取播放列表失败。", result.IsRetryable);
    }

    /// <summary>
    /// 直播流改走回退处理器（ffmpeg 录制）。
    /// </summary>
    /// <param name="task">下载任务。</param>
    /// <param name="progress">进度上报通道。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="liveReason">直播流的诊断信息（来自计划构建结果）。</param>
    /// <returns>回退结果；未配置回退时直接失败。</returns>
    private async Task<DownloadResult> FallbackAsync(
        DownloadTask task,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken,
        string? liveReason)
    {
        if (_fallback is null)
        {
            return DownloadResult.Fail(liveReason ?? "该地址是直播流，未配置可用回退，无法下载。", isRetryable: false);
        }

        return await _fallback.DownloadAsync(task, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 清理工作目录中的分片与拼接产物。
    /// </summary>
    /// <param name="workDirectory">工作目录。</param>
    /// <param name="fetchedFiles">已成功分片的本地路径。</param>
    /// <param name="assembledPath">拼接产物路径。</param>
    /// <remarks>
    /// 仅删除明确知道的文件：失败分片在抓取阶段就不会落盘、被校验/解密判废的分片会被下载器自行删除，
    /// 因此工作目录里残留的只有成功分片与拼接产物。空目录保留，不依赖文件系统删除目录的能力。
    /// </remarks>
    private void CleanupWorkDirectory(string workDirectory, IReadOnlyList<string> fetchedFiles, string assembledPath)
    {
        // 先按已知清单逐个删，让「分片已落盘但记录丢失」这类情况也能被覆盖
        foreach (var file in fetchedFiles)
        {
            TryDelete(file);
        }

        TryDelete(assembledPath);

        // 再整目录删除：工作目录里还会有失败分片留下的半成品、解密后的分片与 AES 密钥，
        // 这些文件并不在 fetchedFiles 中，逐个删必然遗漏，最后只会在输出目录留下一堆垃圾
        try
        {
            _fileSystem.DeleteDirectory(workDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 清理失败不应掩盖下载结果
        }
    }

    /// <summary>
    /// 忽略异常地删除单个文件。
    /// </summary>
    /// <param name="path">文件路径。</param>
    private void TryDelete(string path)
    {
        try
        {
            if (_fileSystem.FileExists(path))
            {
                _fileSystem.DeleteFile(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 清理失败不应掩盖下载结果
        }
    }

    /// <summary>
    /// 确保输出目录存在。
    /// </summary>
    /// <param name="outputPath">输出路径。</param>
    private void EnsureOutputDirectory(string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !_fileSystem.DirectoryExists(directory))
        {
            _fileSystem.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// 构造「全部分片失败」的中文详情。
    /// </summary>
    /// <param name="report">分片下载报告。</param>
    /// <returns>面向用户的失败描述。</returns>
    private static string BuildAllFailedDetail(HlsSegmentDownloadResult report)
    {
        var total = report.TotalSegments;

        if (total == 0)
        {
            return "播放列表中没有可下载的分片条目（清单为空），该地址可能已失效，请在页面重新播放后再次嗅探。";
        }

        var skipped = report.CountBy(SegmentStatus.SkippedByValidation);
        var duplicate = report.CountBy(SegmentStatus.Duplicate);
        var fetchFailed = report.CountBy(SegmentStatus.FetchFailed);
        var decryptFailed = report.CountBy(SegmentStatus.DecryptFailed);

        var placeholder = skipped + duplicate;

        // 情形一：分片全都取得到，但内容全是占位 —— CDN 投毒的典型特征。
        // 必须和「网络失败」分开说，否则用户会一直重试一个根本救不回来的地址。
        if (placeholder == total)
        {
            return $"清单共 {total} 片，能下载但全部被判定为占位内容（{SummarizeReason(report)}）。"
                + "这说明分片已被 CDN 占位污染：格式合法、内容却是同一段占位视频，"
                + "多为动态签名过期所致，重试无效。请在页面重新播放后再次嗅探。";
        }

        // 情形二：清单引用的分片地址一个都不存在 —— 连播放列表本身都是占位产物。
        if (fetchFailed == total)
        {
            return $"清单共 {total} 片，但分片地址全部无法下载（{SummarizeReason(report)}）。"
                + "播放列表本身很可能已是失效的占位内容：真实清单不会引用一批不存在的分片。"
                + "请在页面重新播放视频后立即重新嗅探并下载（动态签名有效期通常很短）。";
        }

        if (decryptFailed == total)
        {
            return $"清单共 {total} 片，全部解密失败（{SummarizeReason(report)}）。"
                + "多为密钥地址失效或密钥不匹配，请在页面重新播放后再次嗅探。";
        }

        var reasons = new List<string>();
        if (placeholder > 0)
        {
            reasons.Add($"被占位污染丢弃 {placeholder} 片");
        }

        if (fetchFailed > 0)
        {
            reasons.Add($"下载失败 {fetchFailed} 片");
        }

        if (decryptFailed > 0)
        {
            reasons.Add($"解密失败 {decryptFailed} 片");
        }

        var detail = reasons.Count > 0 ? string.Join("，", reasons) : "无任何可用分片";
        return $"未能取得任何有效分片（共 {total} 片：{detail}）。该地址可能已被 CDN 反爬占位污染或动态签名已过期，请在页面重新播放后再次嗅探。";
    }

    /// <summary>
    /// 取首个失败分片的原始原因，作为详情里的证据。
    /// </summary>
    /// <param name="report">分片下载报告。</param>
    /// <returns>首条失败原因（已截断）；没有原因文本时给出兜底说明。</returns>
    /// <remarks>
    /// 只取一条：同一批分片失败原因通常一致，罗列全部会让提示变成几十行、反而看不出重点。
    /// </remarks>
    private static string SummarizeReason(HlsSegmentDownloadResult report)
    {
        foreach (var outcome in report.Outcomes)
        {
            if (outcome.Status == SegmentStatus.Fetched || string.IsNullOrWhiteSpace(outcome.Reason))
            {
                continue;
            }

            var reason = outcome.Reason.Trim();

            // 单条原因最长约 60 字，超出部分截断，避免详情变成一整段服务器原文
            return reason.Length <= 60 ? reason : reason[..60] + "…";
        }

        return "无详细原因";
    }

    /// <summary>
    /// 构造「部分成功」的中文说明。
    /// </summary>
    /// <param name="report">分片下载报告。</param>
    /// <returns>缺失时段说明文字。</returns>
    private static string BuildPartialDetail(HlsSegmentDownloadResult report)
    {
        var intervals = string.Join("、", report.MissingIntervals.Select(i => $"{FormatTime(i.Start)}–{FormatTime(i.End)}"));
        return $"本视频共 {report.TotalSegments} 个分片，仅成功取回 {report.FetchedCount} 个，缺失时段：{intervals}。";
    }

    /// <summary>
    /// 把时间跨度格式化为 <c>mm:ss</c>。
    /// </summary>
    /// <param name="value">时间跨度。</param>
    /// <returns>形如 <c>12:34</c> 的文本。</returns>
    private static string FormatTime(TimeSpan value)
        => $"{(int)value.TotalMinutes:D2}:{value.Seconds:D2}";
}
