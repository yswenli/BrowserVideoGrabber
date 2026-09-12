/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Models
*文件名： DownloadTask
*版本号： V1.0.0.0
*唯一标识：f68c20a1-03ca-4e2d-9594-32bdaf9972a3
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:16:00
*描述：下载任务领域模型，承载单个视频资源的下载上下文与运行状态。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:16:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

namespace BrowserVideoGrabber.Core.Models;

/// <summary>
/// 下载任务：单个视频资源的一次下载作业。
/// </summary>
/// <remarks>
/// <para>
/// 该对象同时被 <c>DownloadQueue</c>（调度与状态改写）、<c>IDownloadHandler</c>（执行）、
/// <c>JsonTaskRepository</c>（持久化）三方使用，因此必须是可 JSON 序列化的普通对象，
/// 且不持有任何不可序列化的运行时资源（如 <c>CancellationTokenSource</c>、
/// 流对象等），这些资源一律由队列内部另建字典管理。
/// </para>
/// <para>
/// 状态字段 <see cref="Status"/> 只能由 <c>DownloadQueue</c> 改写，
/// 其它位置读取时须注意可能被后台线程并发修改。
/// </para>
/// </remarks>
public sealed class DownloadTask
{
    /// <summary>任务唯一标识，同时作为断点续传临时文件的命名依据。</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>资源原始地址（含动态签名参数），下载时直接使用。</summary>
    public required string Url { get; init; }

    /// <summary>资源格式，决定由哪个下载处理器处理。</summary>
    public required VideoFormat Format { get; init; }

    /// <summary>请求上下文（Referer / UA / Cookie 等），用于绕过反爬鉴权。</summary>
    public RequestContext Context { get; init; } = new();

    /// <summary>输出文件的完整路径（含扩展名）。</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>展示用标题（一般取资源文件名）。</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>当前状态。仅由 <c>DownloadQueue</c> 改写。</summary>
    public DownloadStatus Status { get; set; } = DownloadStatus.Pending;

    /// <summary>已重试次数。失败重试时自增，达到 <see cref="MaxRetryCount"/> 后不再重试。</summary>
    public int RetryCount { get; set; }

    /// <summary>最大重试次数，默认 3 次。</summary>
    public int MaxRetryCount { get; init; } = 3;

    /// <summary>最近一次失败原因（中文描述），用于界面提示与问题排查。</summary>
    public string? LastError { get; set; }

    /// <summary>任务创建时间。</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>任务进入终态（完成 / 失败 / 取消）的时间。</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>输出文件字节数，完成后回填。</summary>
    public long OutputBytes { get; set; }

    /// <summary>最近一次上报的下载进度（百分比 0~100），仅用于界面展示，不参与持久化决策。</summary>
    public double LastProgressPercent { get; set; }

    /// <summary>
    /// 创建当前任务的深拷贝（仅拷贝数据，不含任何运行时资源）。
    /// </summary>
    /// <returns>字段值完全一致的新 <see cref="DownloadTask"/> 实例。</returns>
    public DownloadTask Clone()
        => new()
        {
            Id = Id,
            Url = Url,
            Format = Format,
            Context = Context.Clone(),
            OutputPath = OutputPath,
            Title = Title,
            Status = Status,
            RetryCount = RetryCount,
            MaxRetryCount = MaxRetryCount,
            LastError = LastError,
            CreatedAt = CreatedAt,
            FinishedAt = FinishedAt,
            OutputBytes = OutputBytes,
            LastProgressPercent = LastProgressPercent
        };
}
