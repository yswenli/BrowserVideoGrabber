/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Downloads
*文件名： HlsSegmentDownloadResult
*版本号： V1.0.0.0
*唯一标识：d86d5bd2-744b-4504-b970-5e03e4ca8448
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 01:12:00
*描述：HLS 分片下载的结果与逐片结局，承载缺失时间段与「是否值得保留」结论。
*
*=================================================
*修改标记
*修改时间：2026/9/13 01:12:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

namespace BrowserVideoGrabber.Core.Downloads;

/// <summary>
/// 单个分片的下载结局。
/// </summary>
public enum SegmentStatus
{
    /// <summary>成功取回、校验通过并（如需）解密，可进入拼接。</summary>
    Fetched = 0,

    /// <summary>内容校验不通过（图片/文本类型、同步字缺失、空内容），被判定为占位。</summary>
    SkippedByValidation = 1,

    /// <summary>与更早分片指纹重复，判定为同一份占位内容。</summary>
    Duplicate = 2,

    /// <summary>网络抓取失败（状态码非 2xx、超时、地址失效等）。</summary>
    FetchFailed = 3,

    /// <summary>解密失败（密钥错误导致填充校验失败，或解密后内容仍非合法 TS）。</summary>
    DecryptFailed = 4
}

/// <summary>
/// 单个分片的下载结局明细。
/// </summary>
public sealed record SegmentFetchOutcome
{
    /// <summary>分片在清单中的下标（按播放顺序）。</summary>
    public int Index { get; init; }

    /// <summary>分片绝对地址。</summary>
    public string Uri { get; init; } = string.Empty;

    /// <summary>该分片在时间轴上的起始位置。</summary>
    public TimeSpan Start { get; init; }

    /// <summary>该分片在时间轴上的结束位置。</summary>
    public TimeSpan End { get; init; }

    /// <summary>结局类型。</summary>
    public SegmentStatus Status { get; init; }

    /// <summary>不可用时（非 Fetched）的原因描述；可用时为空。</summary>
    public string? Reason { get; init; }

    /// <summary>成功解密后的本地分片路径；非 Fetched 时为空。</summary>
    public string? FilePath { get; init; }
}

/// <summary>
/// HLS 分片下载的整体报告。
/// </summary>
/// <remarks>
/// 该对象是「C# 取片」链路对外的唯一结论载体：它既告诉上层「拼成了哪些分片」，
/// 也给出「缺的是哪几段、哪段时间」，使「部分成功」策略能在界面上如实标注缺失区间，
/// 而不是把 1331 片里仅剩的 2 片冒充成完整视频。
/// </remarks>
public sealed class HlsSegmentDownloadResult
{
    /// <summary>逐片结局，与计划中的分片顺序一一对应。</summary>
    public IReadOnlyList<SegmentFetchOutcome> Outcomes { get; }

    /// <summary>缺失时间区间（相邻缺失分片合并为一段）。</summary>
    public IReadOnlyList<(TimeSpan Start, TimeSpan End)> MissingIntervals { get; }

    /// <summary>可用分片占比是否达到保留阈值。</summary>
    public bool WorthKeeping { get; }

    /// <summary>致命错误（如密钥无法获取）。存在时整批下载应回退 ffmpeg。</summary>
    public string? FatalError { get; }

    /// <summary>构造报告。</summary>
    public HlsSegmentDownloadResult(
        IReadOnlyList<SegmentFetchOutcome> outcomes,
        IReadOnlyList<(TimeSpan Start, TimeSpan End)> missingIntervals,
        bool worthKeeping,
        string? fatalError = null)
    {
        Outcomes = outcomes;
        MissingIntervals = missingIntervals;
        WorthKeeping = worthKeeping;
        FatalError = fatalError;
    }

    /// <summary>分片总数。</summary>
    public int TotalSegments => Outcomes.Count;

    /// <summary>成功可取用的分片数。</summary>
    public int FetchedCount => CountBy(SegmentStatus.Fetched);

    /// <summary>按状态统计分片数。</summary>
    /// <param name="status">目标状态。</param>
    /// <returns>该状态的分片数。</returns>
    public int CountBy(SegmentStatus status)
    {
        var count = 0;
        foreach (var outcome in Outcomes)
        {
            if (outcome.Status == status)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>成功分片的本地路径，按播放顺序排列，供拼接器直接消费。</summary>
    public IReadOnlyList<string> FetchedFilePaths
    {
        get
        {
            var paths = new List<string>(Outcomes.Count);
            foreach (var outcome in Outcomes)
            {
                if (outcome.Status == SegmentStatus.Fetched && outcome.FilePath is not null)
                {
                    paths.Add(outcome.FilePath);
                }
            }

            return paths;
        }
    }
}
