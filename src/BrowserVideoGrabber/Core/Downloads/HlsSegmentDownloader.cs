/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Downloads
*文件名： HlsSegmentDownloader
*版本号： V1.0.0.0
*唯一标识：b4c0a02b-8f51-4ac4-befa-4f09dcd8a71a
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 01:12:00
*描述：HLS 分片下载器，并发取片 → 逐片校验 → AES-128 解密 → 产出含缺失区间的报告。
*
*=================================================
*修改标记
*修改时间：2026/9/13 01:12:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using BrowserVideoGrabber.Core.Abstractions;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Core.Downloads;

/// <summary>
/// HLS 分片下载器：把「C# 取片」链路的全部编排收口在一处。
/// </summary>
/// <remarks>
/// <para>
/// 它的职责是：并发抓取每个分片 → 逐片校验内容真伪（图片/文本类型、TS 同步字、重复指纹）
/// → 对 AES-128 流逐片解密 → 把成功分片落盘为可拼接的 <c>.ts</c>，并产出缺失时间段报告。
/// 校验与解密都发生在这里，正是为了让坏分片在「写进成品之前」就被丢弃，
/// 而不是像 ffmpeg 直连那样退出码 0 却产出一个被占位污染的坏文件。
/// </para>
/// <para>
/// 并发度通过信号量控制；单个分片失败不影响其余分片，最终由缺失占比决定整体是否值得保留。
/// </para>
/// </remarks>
public sealed class HlsSegmentDownloader
{
    private const int HeadSampleBytes = 16;

    private readonly IMediaFetcher _fetcher;
    private readonly SegmentContentValidator _validator;
    private readonly IFileSystem _fileSystem;
    private readonly int _maxConcurrency;

    /// <summary>
    /// 构造下载器。
    /// </summary>
    /// <param name="fetcher">媒体抓取实现。</param>
    /// <param name="validator">分片内容校验器。</param>
    /// <param name="fileSystem">文件系统抽象。</param>
    /// <param name="maxConcurrency">最大并发取片数，默认 8。</param>
    public HlsSegmentDownloader(
        IMediaFetcher fetcher,
        SegmentContentValidator validator,
        IFileSystem fileSystem,
        int maxConcurrency = 8)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _maxConcurrency = maxConcurrency > 0 ? maxConcurrency : 8;
    }

    /// <summary>
    /// 按计划下载全部分片。
    /// </summary>
    /// <param name="plan">HLS 下载计划。</param>
    /// <param name="context">请求上下文。</param>
    /// <param name="workDirectory">分片落盘目录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>逐片结局与缺失区间报告。</returns>
    public async Task<HlsSegmentDownloadResult> DownloadAsync(
        HlsDownloadPlan plan,
        RequestContext context,
        string workDirectory,
        CancellationToken cancellationToken)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        _fileSystem.CreateDirectory(workDirectory);

        var key = await ResolveKeyAsync(plan, context, cancellationToken);
        if (key is null && plan.Encryption == M3u8Encryption.Aes128)
        {
            // ResolveKeyAsync 仅在失败时返回 null 并附带 FatalError，这里直接返回整批失败报告
            return new HlsSegmentDownloadResult(
                Array.Empty<SegmentFetchOutcome>(),
                Array.Empty<(TimeSpan, TimeSpan)>(),
                false,
                _fatalError);
        }

        if (plan.SegmentCount == 0)
        {
            return new HlsSegmentDownloadResult(
                Array.Empty<SegmentFetchOutcome>(),
                Array.Empty<(TimeSpan, TimeSpan)>(),
                false);
        }

        var semaphore = new SemaphoreSlim(_maxConcurrency);

        IReadOnlyList<SegmentFetchOutcome> outcomes;
        if (_maxConcurrency == 1)
        {
            // 串行模式必须严格按下标顺序处理：Task.Run 不保证启动顺序，
            // 若占位分片乱序抢跑，「首个占位被取回、其余判重复」的语义会被破坏
            var serial = new SegmentFetchOutcome[plan.SegmentCount];
            for (var i = 0; i < plan.SegmentCount; i++)
            {
                serial[i] = await ProcessSegmentAsync(plan, context, workDirectory, key, i, cancellationToken)
                    .ConfigureAwait(false);
            }

            outcomes = serial;
        }
        else
        {
            var tasks = new List<Task<SegmentFetchOutcome>>(plan.SegmentCount);
            for (var i = 0; i < plan.SegmentCount; i++)
            {
                var index = i;
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        return await ProcessSegmentAsync(plan, context, workDirectory, key, index, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken));
            }

            outcomes = await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        var missingIntervals = BuildMissingIntervals(plan, outcomes);
        var worthKeeping = _validator.IsWorthKeeping(outcomes.Count, outcomes.Count(o => o.Status == SegmentStatus.Fetched));

        return new HlsSegmentDownloadResult(outcomes, missingIntervals, worthKeeping);
    }

    private string? _fatalError;

    /// <summary>
    /// 获取并校验 AES-128 密钥；失败时记录 <see cref="_fatalError"/> 并返回 null。
    /// </summary>
    private async Task<byte[]?> ResolveKeyAsync(HlsDownloadPlan plan, RequestContext context, CancellationToken ct)
    {
        if (plan.Encryption != M3u8Encryption.Aes128)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(plan.KeyUri))
        {
            _fatalError = "AES-128 清单缺少密钥地址，无法解密";
            return null;
        }

        var keyResult = await _fetcher.GetBytesAsync(plan.KeyUri, context, ct).ConfigureAwait(false);
        if (!keyResult.Success || keyResult.Bytes is null || keyResult.Bytes.Length != Aes128Decryptor.KeySizeBytes)
        {
            _fatalError = $"无法获取 AES-128 密钥：{keyResult.Error ?? "密钥长度非法"}";
            return null;
        }

        return keyResult.Bytes;
    }

    /// <summary>
    /// 处理单个分片：抓取 → 校验 → （解密）→ 落盘或判废。
    /// </summary>
    private async Task<SegmentFetchOutcome> ProcessSegmentAsync(
        HlsDownloadPlan plan,
        RequestContext context,
        string workDirectory,
        byte[]? key,
        int index,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var segment = plan.Segments[index];
        var start = plan.SegmentStartOffsets[index];
        var end = start + TimeSpan.FromSeconds(segment.DurationSeconds);
        var localPath = Path.Combine(workDirectory, $"seg_{index:D5}.ts");

        var fetch = await _fetcher.GetToFileAsync(segment.Uri, context, localPath, ct).ConfigureAwait(false);
        if (!fetch.Success)
        {
            return new SegmentFetchOutcome
            {
                Index = index,
                Uri = segment.Uri,
                Start = start,
                End = end,
                Status = SegmentStatus.FetchFailed,
                Reason = fetch.Error
            };
        }

        // 加密流在解密前字节是随机的，不能按 TS 同步字判定；解密后再做同步字复检
        var expectSyncByte = plan.Encryption != M3u8Encryption.Aes128;
        var head = ReadHead(localPath, HeadSampleBytes);
        var tail = ReadTail(localPath, HeadSampleBytes);
        var verdict = _validator.Inspect(fetch.ContentType, fetch.Length, head, expectSyncByte);
        if (!verdict.Usable)
        {
            _fileSystem.DeleteFile(localPath);
            return new SegmentFetchOutcome
            {
                Index = index,
                Uri = segment.Uri,
                Start = start,
                End = end,
                Status = SegmentStatus.SkippedByValidation,
                Reason = verdict.Reason
            };
        }

        var fingerprint = SegmentContentValidator.ComputeFingerprint(head, tail, fetch.Length);
        if (!_validator.TryClaim(fingerprint))
        {
            _fileSystem.DeleteFile(localPath);
            return new SegmentFetchOutcome
            {
                Index = index,
                Uri = segment.Uri,
                Start = start,
                End = end,
                Status = SegmentStatus.Duplicate,
                Reason = "与更早分片内容重复，疑似占位"
            };
        }

        if (key is not null)
        {
            try
            {
                var iv = Aes128Decryptor.ParseIv(plan.KeyIv, plan.MediaSequence + index);
                byte[] cipher;
                using (var readStream = _fileSystem.OpenRead(localPath))
                {
                    var buffer = new MemoryStream();
                    readStream.CopyTo(buffer);
                    cipher = buffer.ToArray();
                }

                var plain = Aes128Decryptor.Decrypt(cipher, key, iv);
                using (var writeStream = _fileSystem.OpenWrite(localPath, false))
                {
                    writeStream.Write(plain, 0, plain.Length);
                }

                // 解密后复检：密钥错误时 PKCS7 填充可能通过，但明文不是合法 TS，必须拦下
                var plainHead = ReadHead(localPath, HeadSampleBytes);
                var after = _validator.Inspect("video/mp2t", plain.Length, plainHead, expectTsSyncByte: true);
                if (!after.Usable)
                {
                    _fileSystem.DeleteFile(localPath);
                    return new SegmentFetchOutcome
                    {
                        Index = index,
                        Uri = segment.Uri,
                        Start = start,
                        End = end,
                        Status = SegmentStatus.DecryptFailed,
                        Reason = after.Reason
                    };
                }
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException)
            {
                _fileSystem.DeleteFile(localPath);
                return new SegmentFetchOutcome
                {
                    Index = index,
                    Uri = segment.Uri,
                    Start = start,
                    End = end,
                    Status = SegmentStatus.DecryptFailed,
                    Reason = $"解密失败：{ex.Message}"
                };
            }
        }

        return new SegmentFetchOutcome
        {
            Index = index,
            Uri = segment.Uri,
            Start = start,
            End = end,
            Status = SegmentStatus.Fetched,
            FilePath = localPath
        };
    }

    /// <summary>
    /// 把相邻缺失分片合并为时间区间。
    /// </summary>
    private static IReadOnlyList<(TimeSpan Start, TimeSpan End)> BuildMissingIntervals(
        HlsDownloadPlan plan,
        IReadOnlyList<SegmentFetchOutcome> outcomes)
    {
        var missing = new List<(TimeSpan, TimeSpan)>();
        var runStart = -1;

        for (var i = 0; i < outcomes.Count; i++)
        {
            if (outcomes[i].Status == SegmentStatus.Fetched)
            {
                if (runStart >= 0)
                {
                    missing.Add(MakeInterval(plan, runStart, i - 1));
                    runStart = -1;
                }
            }
            else if (runStart < 0)
            {
                runStart = i;
            }
        }

        if (runStart >= 0)
        {
            missing.Add(MakeInterval(plan, runStart, outcomes.Count - 1));
        }

        return missing;
    }

    /// <summary>
    /// 由分片下标区间换算时间区间。
    /// </summary>
    private static (TimeSpan Start, TimeSpan End) MakeInterval(HlsDownloadPlan plan, int from, int to)
    {
        var start = plan.SegmentStartOffsets[from];
        var end = to >= plan.SegmentCount - 1
            ? plan.TotalDuration
            : plan.SegmentStartOffsets[to] + TimeSpan.FromSeconds(plan.Segments[to].DurationSeconds);

        return (start, end);
    }

    /// <summary>
    /// 读取文件首部若干字节（用于同步字检查与指纹）。
    /// </summary>
    private byte[] ReadHead(string path, int maxBytes)
    {
        using var stream = _fileSystem.OpenRead(path);
        var count = (int)Math.Min(maxBytes, stream.Length);
        if (count <= 0)
        {
            return Array.Empty<byte>();
        }

        var buffer = new byte[count];
        stream.ReadExactly(buffer);
        return buffer;
    }

    /// <summary>
    /// 读取文件尾部若干字节（用于指纹，占位内容往往在尾部也逐字节相同）。
    /// </summary>
    private byte[] ReadTail(string path, int maxBytes)
    {
        using var stream = _fileSystem.OpenRead(path);
        var take = (int)Math.Min(maxBytes, stream.Length);
        if (take <= 0)
        {
            return Array.Empty<byte>();
        }

        stream.Seek(stream.Length - take, SeekOrigin.Begin);
        var buffer = new byte[take];
        stream.ReadExactly(buffer);
        return buffer;
    }
}
