/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Sniffing
*文件名： VideoFamilyIndex
*版本号： V1.0.0.0
*唯一标识：6a57b851-9613-43b8-b2d3-cfcd1754740f
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 03:10:00
*描述：视频家族索引，把同一个视频的多级资源（主清单 / 媒体清单 / 分片）归并为一个家族，保证列表里一个视频只占一行。
*
*=================================================
*修改标记
*修改时间：2026/9/13 03:10:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Security.Cryptography;
using System.Text;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Core.Sniffing;

/// <summary>
/// 同一视频家族内条目的优劣次序。
/// </summary>
/// <remarks>
/// 次序决定「同族多条候选谁留下」：等级高的条目可以覆盖等级低的，
/// 反之则被抑制。这样用户点下载时拿到的永远是该视频当前已知的<b>最优可下载目标</b>。
/// </remarks>
public enum VideoEntryRank
{
    /// <summary>
    /// 分片（<c>ts</c> / <c>m4s</c>）。单独下载一个分片只会得到几秒钟的片段，
    /// 属于该家族中最差的可下载目标，仅在完全拿不到清单时才会作为兜底出现在列表里。
    /// </summary>
    Fragment = 0,

    /// <summary>整文件（<c>mp4</c>）。</summary>
    WholeFile = 1,

    /// <summary>清单（<c>m3u8</c> / <c>mpd</c>）。交给 ffmpeg 能拉取全部分片，是首选目标。</summary>
    Manifest = 2
}

/// <summary>
/// 候选资源的接纳结果。
/// </summary>
public enum VideoAdmissionKind
{
    /// <summary>该家族首次出现，应新增一行。</summary>
    New = 0,

    /// <summary>该家族已有条目，但本次候选更优（等级更高，或带有原先缺失的信息），应原地刷新已有行。</summary>
    Update = 1,

    /// <summary>已被同族的更优条目覆盖，不应上报。</summary>
    Ignored = 2
}

/// <summary>
/// 接纳决定。
/// </summary>
/// <param name="Kind">接纳结果。</param>
/// <param name="FamilyKey">解析出的家族键。即使结果为忽略，也会给出被归入的家族（便于登记下级资源）。</param>
/// <param name="Video">应上报的条目：<see cref="VideoAdmissionKind.New"/> 为带稳定标识的新条目，
/// <see cref="VideoAdmissionKind.Update"/> 为带<b>原标识</b>的替换条目（界面据此原地刷新而不是新增行）。</param>
public readonly record struct VideoAdmission(VideoAdmissionKind Kind, string FamilyKey, SniffedVideo Video);

/// <summary>
/// 视频家族索引：判定「两条嗅探结果是否属于同一个视频」，并决定该上报哪一条。
/// </summary>
/// <remarks>
/// <para>
/// <b>要解决的问题</b>：页面上只有一个视频，但浏览器实际会发出<b>一串</b>请求 ——
/// 主清单（<c>index.m3u8</c>）→ 各档清晰度的媒体清单（<c>720p/index.m3u8</c>）→ 上千个分片。
/// 若按地址逐个上报，列表会被同一个视频刷出好几行；用户随手选中一个分片去下载，
/// 得到的只是几秒钟的片段，于是就成了「明明是同一个视频，下载出来却不完整」。
/// </para>
/// <para>
/// <b>归并策略</b>（家族键）：
/// <list type="bullet">
///   <item><description>
///     <b>分片</b>按所在目录折叠：同一目录下的成百上千个分片共用一个家族，
///     家族键形如 <c>dir:https://host/hls/720p</c>。
///   </description></item>
///   <item><description>
///     <b>清单 / 整文件</b>默认各自成族，家族键形如 <c>url:https://host/hls/720p/index.m3u8</c>。
///   </description></item>
///   <item><description>
///     当某份清单被解析出来后，它声明的<b>变体地址</b>与<b>分片目录</b>会登记为「归属于该清单」，
///     此后落到这些地址/目录上的候选一律被抑制 —— 这是消除重复行的主要手段。
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <b>两种到达顺序都能收敛到一行</b>：
/// <list type="bullet">
///   <item><description>
///     「先清单、后分片」（正常顺序）：分片命中已登记的归属目录，直接被抑制。
///   </description></item>
///   <item><description>
///     「先分片、后清单」（嗅探开关在播放中途才打开）：分片先按 <c>dir:</c> 成族并上报，
///     清单到达时<b>复用同一个家族键</b>并以更高等级触发 <see cref="VideoAdmissionKind.Update"/>，
///     于是那一行被原地升级为清单行，不留残余。
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <b>容量</b>：家族数量达到上限后按到达顺序淘汰最旧的家族（连同它登记过的归属关系一并清理），
/// 而不是像早期实现那样「满了就直接丢掉新资源」—— 后者会让长时间浏览后嗅探悄悄失效。
/// </para>
/// <para>
/// 本类内部自带锁，可被嗅探器的多条链路并发调用。
/// </para>
/// </remarks>
public sealed class VideoFamilyIndex
{
    /// <summary>默认的家族数量上限。</summary>
    public const int DefaultCapacity = 800;

    /// <summary>家族数量下限。过小的上限会让去重表频繁淘汰，反而造成重复上报。</summary>
    private const int MinimumCapacity = 16;

    private readonly object _gate = new();
    private readonly int _capacity;

    /// <summary>家族键到家族对象的映射。</summary>
    private readonly Dictionary<string, Family> _families = new(StringComparer.Ordinal);

    /// <summary>已上报条目的指纹到家族键。</summary>
    private readonly Dictionary<string, string> _familyByReportedUrl = new(StringComparer.Ordinal);

    /// <summary>被某份清单「拥有」的下级地址指纹到家族键。</summary>
    private readonly Dictionary<string, string> _familyByOwnedUrl = new(StringComparer.Ordinal);

    /// <summary>被某份清单「拥有」的目录到家族键。</summary>
    private readonly Dictionary<string, string> _familyByOwnedDirectory = new(StringComparer.Ordinal);

    /// <summary>由分片建立的目录到家族键。</summary>
    private readonly Dictionary<string, string> _familyByFragmentDirectory = new(StringComparer.Ordinal);

    /// <summary>家族键的到达顺序，用于容量淘汰。</summary>
    private readonly Queue<string> _arrivalOrder = new();

    /// <summary>
    /// 初始化家族索引。
    /// </summary>
    /// <param name="capacity">家族数量上限。小于 <see cref="MinimumCapacity"/> 时按下限处理。</param>
    public VideoFamilyIndex(int capacity = DefaultCapacity)
        => _capacity = Math.Max(MinimumCapacity, capacity);

    /// <summary>家族数量上限。</summary>
    public int Capacity => _capacity;

    /// <summary>当前已记录的家族数量。</summary>
    public int FamilyCount
    {
        get
        {
            lock (_gate)
            {
                return _families.Count;
            }
        }
    }

    /// <summary>
    /// 判断一个格式在家族内的优劣等级。
    /// </summary>
    /// <param name="format">资源格式。</param>
    /// <returns>优劣等级。</returns>
    /// <remarks>
    /// 未识别的格式按「清单」处理：宁可多留一条可见的记录，也不要把用户真正想看的东西悄悄抑制掉。
    /// </remarks>
    public static VideoEntryRank RankOf(VideoFormat format) => format switch
    {
        VideoFormat.Ts or VideoFormat.M4s => VideoEntryRank.Fragment,
        VideoFormat.Mp4 => VideoEntryRank.WholeFile,
        _ => VideoEntryRank.Manifest
    };

    /// <summary>
    /// 由家族键派生一个稳定标识。
    /// </summary>
    /// <param name="familyKey">家族键。</param>
    /// <returns>对该键恒定的 GUID。</returns>
    /// <remarks>
    /// 标识必须稳定：界面按标识定位行，同族的后续条目才会原地刷新而不是新增一行。
    /// 这里用 SHA-256 派生（仅作散列用途，不涉及安全性），而不是随机 GUID。
    /// </remarks>
    public static Guid CreateStableId(string familyKey)
    {
        ArgumentNullException.ThrowIfNull(familyKey);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(familyKey));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>
    /// 把<b>目录</b>规范化成目录键。
    /// </summary>
    /// <param name="directory">目录文本，形如 <c>https://host/hls/720p</c>。</param>
    /// <returns>去掉查询串、去掉末尾斜杠并转小写的目录键；输入为空时返回空串。</returns>
    /// <remarks>
    /// 入参必须是目录而不是文件地址：本方法<b>不会</b>去掉最后一段路径。
    /// 若误传 <c>https://host/hls/index.m3u8</c>，得到的是一个永不匹配的键，
    /// 抑制会静默失效且不会有任何报错 —— 目录键请用
    /// <c>M3u8OwnershipCollector</c> 生成，不要手工拼接。
    /// </remarks>
    public static string NormalizeDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return string.Empty;
        }

        return VideoUrlMatcher.Fingerprint(directory).TrimEnd('/');
    }

    /// <summary>
    /// 提交一个候选资源，判定它是否应出现在界面上。
    /// </summary>
    /// <param name="candidate">候选资源。其 <see cref="SniffedVideo.Id"/> 会被忽略，由家族决定。</param>
    /// <returns>接纳决定。</returns>
    public VideoAdmission Consider(SniffedVideo candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var fingerprint = VideoUrlMatcher.Fingerprint(candidate.Url);
        if (fingerprint.Length == 0)
        {
            return new VideoAdmission(VideoAdmissionKind.Ignored, string.Empty, candidate);
        }

        var rank = RankOf(candidate.Format);
        var directory = ResolveDirectory(candidate.Url);

        lock (_gate)
        {
            // ① 已被某份清单收编：变体地址按精确指纹命中，分片按所属目录命中
            if (_familyByOwnedUrl.TryGetValue(fingerprint, out var urlOwner))
            {
                return new VideoAdmission(VideoAdmissionKind.Ignored, urlOwner, candidate);
            }

            if (rank == VideoEntryRank.Fragment
                && directory.Length > 0
                && _familyByOwnedDirectory.TryGetValue(directory, out var directoryOwner))
            {
                return new VideoAdmission(VideoAdmissionKind.Ignored, directoryOwner, candidate);
            }

            // ② 定位家族
            var familyKey = ResolveFamilyKey(fingerprint, rank, directory);

            if (_families.TryGetValue(familyKey, out var family))
            {
                _familyByReportedUrl[fingerprint] = familyKey;

                if (rank > family.Rank || (rank == family.Rank && IsRicher(candidate, family.Video)))
                {
                    // 原地升级：沿用原标识，界面按标识找到旧行并刷新各列
                    family.Video = candidate.WithId(family.Id);
                    family.Rank = rank;

                    return new VideoAdmission(VideoAdmissionKind.Update, familyKey, family.Video);
                }

                return new VideoAdmission(VideoAdmissionKind.Ignored, familyKey, family.Video);
            }

            // ③ 新家族
            EvictForCapacity();

            var id = CreateStableId(familyKey);
            var accepted = candidate.WithId(id);
            var fragmentDirectory = rank == VideoEntryRank.Fragment ? directory : string.Empty;

            _families[familyKey] = new Family
            {
                Id = id,
                Video = accepted,
                Rank = rank,
                FragmentDirectory = fragmentDirectory
            };

            _arrivalOrder.Enqueue(familyKey);
            _familyByReportedUrl[fingerprint] = familyKey;

            if (fragmentDirectory.Length > 0)
            {
                // 分片家族按目录占位：同目录的后续分片归入同一行，
                // 而稍后出现的清单可以复用该家族键，把这一行原地升级掉
                _familyByFragmentDirectory[fragmentDirectory] = familyKey;
            }

            return new VideoAdmission(VideoAdmissionKind.New, familyKey, accepted);
        }
    }

    /// <summary>
    /// 登记某份清单「拥有」的下级资源：它声明的变体地址（精确）与分片所在目录（折叠）。
    /// </summary>
    /// <param name="familyKey">清单所在家族的键，取自 <see cref="Consider"/> 的返回值。</param>
    /// <param name="ownedUrls">被拥有的精确地址（通常是清晰度变体）。</param>
    /// <param name="ownedDirectories">
    /// 被拥有的<b>目录</b>（通常是分片与清单自身所在目录）。
    /// 必须传目录而非文件地址，否则会得到一个永不匹配的键；请使用
    /// <c>M3u8OwnershipCollector</c> 的输出，不要手工拼接。
    /// </param>
    /// <returns>
    /// 因本次登记而需要从界面<b>撤回</b>的旧条目。典型场景是清晰度变体先被上报、主清单后到达，
    /// 此时变体那一行属于噪音，必须撤掉，否则同一个视频仍会留下两行。
    /// </returns>
    public IReadOnlyList<SniffedVideo> RegisterOwned(
        string familyKey,
        IReadOnlyCollection<string>? ownedUrls = null,
        IReadOnlyCollection<string>? ownedDirectories = null)
    {
        if (string.IsNullOrEmpty(familyKey))
        {
            return Array.Empty<SniffedVideo>();
        }

        List<SniffedVideo>? superseded = null;

        lock (_gate)
        {
            if (!_families.TryGetValue(familyKey, out var family))
            {
                return Array.Empty<SniffedVideo>();
            }

            if (ownedUrls is not null)
            {
                foreach (var url in ownedUrls)
                {
                    var fingerprint = VideoUrlMatcher.Fingerprint(url);
                    if (fingerprint.Length == 0)
                    {
                        continue;
                    }

                    // 该下级地址此前若已作为独立条目上报，说明清单晚于它被识别，旧行必须撤回
                    if (_familyByReportedUrl.TryGetValue(fingerprint, out var reportedFamily)
                        && reportedFamily != familyKey
                        && RemoveFamily(reportedFamily) is { } removed)
                    {
                        (superseded ??= new List<SniffedVideo>()).Add(removed);
                    }

                    family.OwnedUrls.Add(fingerprint);
                    _familyByOwnedUrl[fingerprint] = familyKey;
                }
            }

            if (ownedDirectories is not null)
            {
                foreach (var directory in ownedDirectories)
                {
                    var normalized = NormalizeDirectory(directory);
                    if (normalized.Length == 0)
                    {
                        continue;
                    }

                    family.OwnedDirectories.Add(normalized);
                    _familyByOwnedDirectory[normalized] = familyKey;
                }
            }

            // 家族已升级为清单：撤销「分片目录占位」，避免同目录下的独立整文件被误判成同一个视频。
            // 该目录本身已在上面登记为归属目录，后续分片依然会被正确抑制。
            if (family.Rank != VideoEntryRank.Fragment && family.FragmentDirectory.Length > 0)
            {
                if (_familyByFragmentDirectory.TryGetValue(family.FragmentDirectory, out var mapped)
                    && mapped == familyKey)
                {
                    _familyByFragmentDirectory.Remove(family.FragmentDirectory);
                }

                family.FragmentDirectory = string.Empty;
            }
        }

        return superseded ?? (IReadOnlyList<SniffedVideo>)Array.Empty<SniffedVideo>();
    }

    /// <summary>
    /// 清空全部记录，使同一资源可以再次被上报。
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _families.Clear();
            _familyByReportedUrl.Clear();
            _familyByOwnedUrl.Clear();
            _familyByOwnedDirectory.Clear();
            _familyByFragmentDirectory.Clear();
            _arrivalOrder.Clear();
        }
    }

    /// <summary>
    /// 目录级去重索引的大小（用于诊断与测试断言）。
    /// </summary>
    public int OwnedDirectoryCount
    {
        get
        {
            lock (_gate)
            {
                return _familyByOwnedDirectory.Count;
            }
        }
    }

    /// <summary>
    /// 定位候选所属的家族键。
    /// </summary>
    /// <param name="fingerprint">地址指纹。</param>
    /// <param name="rank">优劣等级。</param>
    /// <param name="directory">所在目录键。</param>
    /// <returns>家族键。</returns>
    private string ResolveFamilyKey(string fingerprint, VideoEntryRank rank, string directory)
    {
        if (rank == VideoEntryRank.Fragment)
        {
            if (directory.Length > 0 && _familyByFragmentDirectory.TryGetValue(directory, out var existing))
            {
                return existing;
            }

            return directory.Length > 0 ? "dir:" + directory : "dir:" + fingerprint;
        }

        // 只有清单允许复用「分片目录家族」，这样「先分片、后清单」的顺序下那一行会被原地升级。
        // 刻意不把整文件算进来：与分片同目录的 mp4 通常是与该视频无关的另一个资源，
        // 若让它复用家族，就会以「等级更高」为由把分片行覆盖掉，等于凭空隐藏了一个视频。
        if (rank == VideoEntryRank.Manifest
            && directory.Length > 0
            && _familyByFragmentDirectory.TryGetValue(directory, out var fragmentFamily))
        {
            return fragmentFamily;
        }

        return "url:" + fingerprint;
    }

    /// <summary>
    /// 判断候选是否携带了现有条目所缺少的信息。
    /// </summary>
    /// <param name="candidate">候选条目。</param>
    /// <param name="existing">现有条目。</param>
    /// <returns>更丰富返回 true。</returns>
    /// <remarks>
    /// 同一地址会被两条链路先后捕获：JS 注入只能拿到地址，网络响应监听还能拿到响应头与清晰度。
    /// 若只比较等级，先到的 JS 条目会让后到的网络条目被丢弃，列表上的「分辨率」就会长期为空。
    /// </remarks>
    private static bool IsRicher(SniffedVideo candidate, SniffedVideo existing)
        => (!string.IsNullOrEmpty(candidate.Resolution) && string.IsNullOrEmpty(existing.Resolution))
        || (candidate.Bandwidth.HasValue && !existing.Bandwidth.HasValue)
        || (!string.IsNullOrEmpty(candidate.ContentType) && string.IsNullOrEmpty(existing.ContentType));

    /// <summary>
    /// 取出地址所在目录（指纹形式，已去查询串并转小写）。
    /// </summary>
    /// <param name="url">资源地址。</param>
    /// <returns>目录键；无目录部分时返回空串。</returns>
    private static string ResolveDirectory(string url)
    {
        var normalized = VideoUrlMatcher.Fingerprint(url);
        var slashIndex = normalized.LastIndexOf('/');

        return slashIndex > 0 ? normalized[..slashIndex] : string.Empty;
    }

    /// <summary>
    /// 家族数量达到上限时淘汰最旧的家族。
    /// </summary>
    private void EvictForCapacity()
    {
        while (_families.Count >= _capacity && _arrivalOrder.Count > 0)
        {
            // 淘汰不产生「撤回」通知：被淘汰的多是早已不再出现的冷门条目
            RemoveFamily(_arrivalOrder.Dequeue());
        }
    }

    /// <summary>
    /// 移除一个家族及其登记过的全部归属关系。
    /// </summary>
    /// <param name="familyKey">家族键。</param>
    /// <returns>被移除家族原先上报的条目；家族不存在时返回 null。</returns>
    private SniffedVideo? RemoveFamily(string familyKey)
    {
        if (!_families.Remove(familyKey, out var family))
        {
            return null;
        }

        foreach (var fingerprint in family.OwnedUrls)
        {
            _familyByOwnedUrl.Remove(fingerprint);
        }

        foreach (var directory in family.OwnedDirectories)
        {
            _familyByOwnedDirectory.Remove(directory);
        }

        // 只清理仍指向该家族的登记：同一目录可能已被新家族接管，不能误删
        if (family.FragmentDirectory.Length > 0
            && _familyByFragmentDirectory.TryGetValue(family.FragmentDirectory, out var mapped)
            && mapped == familyKey)
        {
            _familyByFragmentDirectory.Remove(family.FragmentDirectory);
        }

        foreach (var pair in _familyByReportedUrl.Where(pair => pair.Value == familyKey).ToList())
        {
            _familyByReportedUrl.Remove(pair.Key);
        }

        return family.Video;
    }

    /// <summary>
    /// 一个视频家族。
    /// </summary>
    private sealed class Family
    {
        /// <summary>家族标识，等于 <see cref="CreateStableId"/> 由家族键派生的结果。</summary>
        public Guid Id { get; init; }

        /// <summary>当前代表该家族上报的条目。</summary>
        public SniffedVideo Video { get; set; } = null!;

        /// <summary>当前条目的优劣等级。</summary>
        public VideoEntryRank Rank { get; set; }

        /// <summary>若该家族由分片建立，记录其目录键；升级为清单后清空。</summary>
        public string FragmentDirectory { get; set; } = string.Empty;

        /// <summary>该家族声明拥有的下级地址指纹。</summary>
        public HashSet<string> OwnedUrls { get; } = new(StringComparer.Ordinal);

        /// <summary>该家族声明拥有的目录。</summary>
        public HashSet<string> OwnedDirectories { get; } = new(StringComparer.Ordinal);
    }
}
