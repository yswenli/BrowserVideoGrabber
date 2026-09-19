/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Sniffing
*文件名： VideoFamilyIndexTests
*版本号： V1.0.0.0
*唯一标识：3de809ed-418c-4ad1-ba9b-0ba63963e06f
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 03:52:00
*描述：VideoFamilyIndex 的单元测试，验证同一个视频的多级资源只在列表上占一行。
*
*=================================================
*修改标记
*修改时间：2026/9/13 03:52:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Models;
using BrowserVideoGrabber.Core.Sniffing;

namespace BrowserVideoGrabber.Tests.Sniffing;

/// <summary>
/// <see cref="VideoFamilyIndex"/> 的行为验证。
/// </summary>
public sealed class VideoFamilyIndexTests
{
    private const string MasterUrl = "https://cdn.test/hls/video/index.m3u8";
    private const string Variant720Url = "https://cdn.test/hls/video/720p/index.m3u8";
    private const string Variant1080Url = "https://cdn.test/hls/video/1080p/index.m3u8";
    private const string Segment720FirstUrl = "https://cdn.test/hls/video/720p/seg-1.ts";
    private const string Segment720SecondUrl = "https://cdn.test/hls/video/720p/seg-2.ts";

    /// <summary>
    /// 典型 HLS 页面（主清单 → 变体 → 分片）最终只能留下一行，且指向主清单。
    /// </summary>
    /// <remarks>
    /// 这正是用户报告的核心问题：页面上只有一个视频，浏览器却发出了一串请求。
    /// 若不做归并，列表里会出现「主清单 + 各档变体 + 分片目录」好几行，
    /// 而用户随手选中的往往是一个分片，下载出来只有几秒钟。
    /// </remarks>
    [Fact]
    public void Should_KeepSingleRow_WhenMasterVariantsAndSegmentsAllSeen()
    {
        var index = new VideoFamilyIndex();

        var master = index.Consider(Create(MasterUrl, VideoFormat.M3u8));
        Assert.Equal(VideoAdmissionKind.New, master.Kind);

        var superseded = index.RegisterOwned(
            master.FamilyKey,
            [Variant720Url, Variant1080Url],
            ["https://cdn.test/hls/video", "https://cdn.test/hls/video/720p", "https://cdn.test/hls/video/1080p"]);

        Assert.Empty(superseded);

        Assert.Equal(VideoAdmissionKind.Ignored, index.Consider(Create(Variant720Url, VideoFormat.M3u8)).Kind);
        Assert.Equal(VideoAdmissionKind.Ignored, index.Consider(Create(Variant1080Url, VideoFormat.M3u8)).Kind);
        Assert.Equal(VideoAdmissionKind.Ignored, index.Consider(Create(Segment720FirstUrl, VideoFormat.Ts)).Kind);
        Assert.Equal(VideoAdmissionKind.Ignored, index.Consider(Create(Segment720SecondUrl, VideoFormat.Ts)).Kind);

        // 主清单自身目录下的分片同样被抑制
        Assert.Equal(
            VideoAdmissionKind.Ignored,
            index.Consider(Create("https://cdn.test/hls/video/seg-0.ts", VideoFormat.Ts)).Kind);

        Assert.Equal(1, index.FamilyCount);
    }

    /// <summary>
    /// 同一个分片目录下的所有分片必须折叠成一个家族，但不直接上报到 UI。
    /// </summary>
    /// <remarks>
    /// 分片本身不可单独下载，创建家族只是为了给后续清单提供归并目标。
    /// 因此 Consider 返回 Ignored，但家族表中仍有记录（FamilyCount = 1）。
    /// </remarks>
    [Fact]
    public void Should_FoldSegmentsOfSameDirectory_IntoOneFamily_NotReported()
    {
        var index = new VideoFamilyIndex();

        var first = index.Consider(Create(Segment720FirstUrl, VideoFormat.Ts));
        var second = index.Consider(Create(Segment720SecondUrl, VideoFormat.Ts));

        // 分片创建家族但不上报（Ignored），同目录后续分片也被抑制
        Assert.Equal(VideoAdmissionKind.Ignored, first.Kind);
        Assert.Equal(VideoAdmissionKind.Ignored, second.Kind);
        Assert.Equal(first.FamilyKey, second.FamilyKey);

        // 家族确实被创建了 —— 后续清单可以复用这个家族键
        Assert.Equal(1, index.FamilyCount);
    }

    /// <summary>
    /// 「先看到分片、后看到清单」时，清单应复用分片的家族键并以更高等级触发 Update。
    /// </summary>
    /// <remarks>
    /// 分片虽然本身不上报到 UI，但它先创建了家族并占位；
    /// 清单到达时 ResolveFamilyKey 复用这个家族键，以更高 rank 触发 Update，
    /// 于是 UI 会原地刷新那一行（从分片 URL 变成清单 URL）。
    /// </remarks>
    [Fact]
    public void Should_UpgradeSegmentFamily_ToPlaylist_WhenPlaylistArrivesLater()
    {
        var index = new VideoFamilyIndex();

        var segment = index.Consider(Create(Segment720FirstUrl, VideoFormat.Ts));
        Assert.Equal(VideoAdmissionKind.Ignored, segment.Kind);  // 分片不上报，但家族已创建

        var playlist = index.Consider(Create(Variant720Url, VideoFormat.M3u8));

        Assert.Equal(VideoAdmissionKind.Update, playlist.Kind);
        Assert.Equal(segment.FamilyKey, playlist.FamilyKey);
        Assert.Equal(segment.Video.Id, playlist.Video.Id);
        Assert.Equal(1, index.FamilyCount);
    }

    /// <summary>
    /// 某档清晰度先被上报、主清单后到达时，那一档必须被撤回。
    /// </summary>
    /// <remarks>
    /// 这与上一条的区别在于家族键不同（变体默认各自成族），因此无法原地升级，
    /// 只能由主清单在登记归属关系时把旧行撤回。
    /// </remarks>
    [Fact]
    public void Should_SupersedeVariantRow_WhenMasterClaimsIt()
    {
        var index = new VideoFamilyIndex();

        var variant = index.Consider(Create(Variant720Url, VideoFormat.M3u8));
        Assert.Equal(VideoAdmissionKind.New, variant.Kind);

        var master = index.Consider(Create(MasterUrl, VideoFormat.M3u8));
        var superseded = index.RegisterOwned(
            master.FamilyKey,
            [Variant720Url, Variant1080Url],
            ["https://cdn.test/hls/video", "https://cdn.test/hls/video/720p"]);

        var retracted = Assert.Single(superseded);
        Assert.Equal(variant.Video.Id, retracted.Id);
        Assert.Equal(1, index.FamilyCount);

        // 撤回之后，同一地址再次出现也不会重新成行
        Assert.Equal(VideoAdmissionKind.Ignored, index.Consider(Create(Variant720Url, VideoFormat.M3u8)).Kind);
        Assert.Equal(1, index.FamilyCount);
    }

    /// <summary>
    /// 同一地址被两条链路先后捕获时，信息更丰富的一条应原地刷新，且标识保持不变。
    /// </summary>
    /// <remarks>
    /// JS 注入链路只能拿到地址，网络响应链路还能拿到响应头与清晰度。
    /// 若只按「等级」比较而忽略信息量，先到的 JS 条目会把后到的网络条目挡掉，
    /// 列表上的「分辨率」列就会长期为空。
    /// </remarks>
    [Fact]
    public void Should_RefreshInPlace_WhenRicherCandidateArrives()
    {
        var index = new VideoFamilyIndex();

        var fromScript = index.Consider(Create(MasterUrl, VideoFormat.M3u8, source: "jshook"));
        Assert.Equal(VideoAdmissionKind.New, fromScript.Kind);
        Assert.Null(fromScript.Video.Resolution);

        var fromNetwork = index.Consider(Create(
            MasterUrl,
            VideoFormat.M3u8,
            resolution: "1920x1080",
            bandwidth: 5_000_000,
            contentType: "application/vnd.apple.mpegurl",
            source: "network"));

        Assert.Equal(VideoAdmissionKind.Update, fromNetwork.Kind);
        Assert.Equal(fromScript.Video.Id, fromNetwork.Video.Id);
        Assert.Equal("1920x1080", fromNetwork.Video.Resolution);

        // 反向再来一次：信息更少的那条不应覆盖已有的
        var poorerAgain = index.Consider(Create(MasterUrl, VideoFormat.M3u8, source: "jshook"));
        Assert.Equal(VideoAdmissionKind.Ignored, poorerAgain.Kind);
        Assert.Equal(1, index.FamilyCount);
    }

    /// <summary>
    /// 与分片同目录的独立整文件不应被并进分片家族。
    /// </summary>
    /// <remarks>
    /// 分片目录里出现一个 mp4 是完全可能的（站点同时提供独立文件）。若允许它复用分片家族，
    /// 就会以「等级更高」为由把分片行覆盖掉 —— 相当于凭空隐藏了一个真实存在的视频。
    /// </remarks>
    [Fact]
    public void Should_KeepUnrelatedWholeFile_InItsOwnFamily()
    {
        var index = new VideoFamilyIndex();

        index.Consider(Create(Segment720FirstUrl, VideoFormat.Ts));
        var movie = index.Consider(Create("https://cdn.test/hls/video/720p/movie.mp4", VideoFormat.Mp4));

        Assert.Equal(VideoAdmissionKind.New, movie.Kind);
        Assert.Equal(2, index.FamilyCount);
    }

    /// <summary>
    /// 不同视频必须各自成行。
    /// </summary>
    [Fact]
    public void Should_KeepDifferentVideosAsSeparateRows()
    {
        var index = new VideoFamilyIndex();

        Assert.Equal(VideoAdmissionKind.New, index.Consider(Create(MasterUrl, VideoFormat.M3u8)).Kind);
        Assert.Equal(
            VideoAdmissionKind.New,
            index.Consider(Create("https://cdn.test/hls/other/index.m3u8", VideoFormat.M3u8)).Kind);
        Assert.Equal(
            VideoAdmissionKind.New,
            index.Consider(Create("https://other.test/movie.mp4", VideoFormat.Mp4)).Kind);

        Assert.Equal(3, index.FamilyCount);
    }

    /// <summary>
    /// 家族数量达到上限时应淘汰最旧的家族，且新资源必须仍然可见。
    /// </summary>
    /// <remarks>
    /// 早期实现是「满了就丢弃新资源」，后果是长时间浏览后嗅探悄悄失效：
    /// 页面里明明有视频，列表却再也不更新，且没有任何提示。
    /// </remarks>
    [Fact]
    public void Should_EvictOldestFamily_WhenCapacityReached()
    {
        var index = new VideoFamilyIndex(capacity: 16);

        for (var i = 0; i < 40; i++)
        {
            index.Consider(Create($"https://cdn.test/hls/v{i}/index.m3u8", VideoFormat.M3u8));
        }

        Assert.True(index.FamilyCount <= index.Capacity, $"家族数 {index.FamilyCount} 超过上限 {index.Capacity}");

        var fresh = index.Consider(Create("https://cdn.test/hls/fresh/index.m3u8", VideoFormat.M3u8));
        Assert.Equal(VideoAdmissionKind.New, fresh.Kind);
    }

    /// <summary>
    /// 容量下限必须被强制抬高，避免上限过小导致去重表频繁淘汰、反而产生重复行。
    /// </summary>
    [Fact]
    public void Should_RaiseTooSmallCapacity()
    {
        var index = new VideoFamilyIndex(capacity: 1);

        Assert.True(index.Capacity >= 16);
    }

    /// <summary>
    /// 家族标识必须由家族键稳定派生：界面正是靠它把同族条目识别为「同一行」。
    /// </summary>
    [Fact]
    public void Should_DeriveStableIdentifier()
    {
        Assert.Equal(VideoFamilyIndex.CreateStableId("dir:a"), VideoFamilyIndex.CreateStableId("dir:a"));
        Assert.NotEqual(VideoFamilyIndex.CreateStableId("dir:a"), VideoFamilyIndex.CreateStableId("dir:b"));
    }

    /// <summary>
    /// 清空后同一资源可以再次被上报（界面「清空列表」按钮依赖该行为）。
    /// </summary>
    [Fact]
    public void Should_AllowSameResourceAgain_AfterClear()
    {
        var index = new VideoFamilyIndex();

        index.Consider(Create(MasterUrl, VideoFormat.M3u8));
        Assert.Equal(VideoAdmissionKind.Ignored, index.Consider(Create(MasterUrl, VideoFormat.M3u8)).Kind);

        index.Clear();

        Assert.Equal(0, index.FamilyCount);
        Assert.Equal(VideoAdmissionKind.New, index.Consider(Create(MasterUrl, VideoFormat.M3u8)).Kind);
    }

    /// <summary>
    /// 格式到优劣等级的映射：清单优于整文件，整文件优于分片。
    /// </summary>
    /// <param name="format">资源格式。</param>
    /// <param name="expected">期望等级。</param>
    [Theory]
    [InlineData(VideoFormat.M3u8, VideoEntryRank.Manifest)]
    [InlineData(VideoFormat.Mpd, VideoEntryRank.Manifest)]
    [InlineData(VideoFormat.Mp4, VideoEntryRank.WholeFile)]
    [InlineData(VideoFormat.Ts, VideoEntryRank.Fragment)]
    [InlineData(VideoFormat.M4s, VideoEntryRank.Fragment)]
    public void Should_MapFormatToRank(VideoFormat format, VideoEntryRank expected)
        => Assert.Equal(expected, VideoFamilyIndex.RankOf(format));

    /// <summary>
    /// 无法解析的地址不应进入索引，避免污染家族表。
    /// </summary>
    [Fact]
    public void Should_IgnoreCandidateWithoutParsableAddress()
    {
        var index = new VideoFamilyIndex();

        var admission = index.Consider(Create(string.Empty, VideoFormat.M3u8));

        Assert.Equal(VideoAdmissionKind.Ignored, admission.Kind);
        Assert.Equal(0, index.FamilyCount);
    }

    /// <summary>
    /// 同一条地址换了新的动态签名（查询串变化）后，必须原地刷新为最新那条，
    /// 而不是沿用第一次的过期地址。
    /// </summary>
    /// <remarks>
    /// 这是真实故障的根因：页面能正常播放，是因为播放器后来用新签名重新请求了清单；
    /// 而旧实现只按「元数据更丰富」判断升级，新签名被判为 Ignored，
    /// 界面上永远留着页面 HTML 里那条已过期的地址，下载必然失败。
    /// </remarks>
    [Fact]
    public void Should_RefreshToLatestSignature_WhenSameUrlReRequested()
    {
        var index = new VideoFamilyIndex();

        var stale = "https://cdn.test/hls/video/playlist.m3u8?verify=1788957992-OLD";
        var fresh = "https://cdn.test/hls/video/playlist.m3u8?verify=1789237987-NEW";

        var first = index.Consider(Create(stale, VideoFormat.M3u8, contentType: "application/vnd.apple.mpegurl"));
        Assert.Equal(VideoAdmissionKind.New, first.Kind);

        var second = index.Consider(Create(fresh, VideoFormat.M3u8, contentType: "application/vnd.apple.mpegurl"));

        Assert.Equal(VideoAdmissionKind.Update, second.Kind);
        Assert.Equal(fresh, second.Video.Url);

        // 必须保持同一个标识，界面才能原地刷新而不是新增一行
        Assert.Equal(first.Video.Id, second.Video.Id);
        Assert.Equal(1, index.FamilyCount);
    }

    /// <summary>
    /// 重新签名刷新时，不能把先前已获得的元数据（分辨率 / 码率 / Content-Type）弄丢。
    /// </summary>
    /// <remarks>
    /// 新签名常来自 JS Hook 链路，其元数据可能比网络响应链路少；
    /// 整体替换会让列表里已显示的信息倒退。
    /// </remarks>
    [Fact]
    public void Should_PreserveRicherMetadata_WhenRefreshingToNewSignature()
    {
        var index = new VideoFamilyIndex();

        var rich = "https://cdn.test/hls/video/playlist.m3u8?verify=1-OLD";
        var bare = "https://cdn.test/hls/video/playlist.m3u8?verify=2-NEW";

        index.Consider(Create(rich, VideoFormat.M3u8, "1280x720", 2_500_000, "application/vnd.apple.mpegurl"));
        var refreshed = index.Consider(Create(bare, VideoFormat.M3u8, source: "jshook"));

        Assert.Equal(bare, refreshed.Video.Url);
        Assert.Equal("1280x720", refreshed.Video.Resolution);
        Assert.Equal(2_500_000, refreshed.Video.Bandwidth);
        Assert.Equal("application/vnd.apple.mpegurl", refreshed.Video.ContentType);
    }

    /// <summary>
    /// 家族索引改写标识时不得丢掉页面标题（下载文件名依赖它）。
    /// </summary>
    [Fact]
    public void Should_KeepPageTitle_WhenAssigningFamilyId()
    {
        var index = new VideoFamilyIndex();
        var url = "https://cdn.test/hls/video/playlist.m3u8";

        var admission = index.Consider(
            new SniffedVideo
            {
                Url = url,
                NormalizedUrl = VideoUrlMatcher.Normalize(url),
                Format = VideoFormat.M3u8,
                PageTitle = "某视频站点 - 精彩片段"
            });

        Assert.Equal("某视频站点 - 精彩片段", admission.Video.PageTitle);
        Assert.NotEqual(Guid.Empty, admission.Video.Id);
    }

    /// <summary>
    /// 构造一个嗅探结果。
    /// </summary>
    /// <param name="url">资源地址。</param>
    /// <param name="format">资源格式。</param>
    /// <param name="resolution">分辨率。</param>
    /// <param name="bandwidth">码率。</param>
    /// <param name="contentType">响应内容类型。</param>
    /// <param name="source">嗅探来源。</param>
    /// <returns>嗅探结果实例。</returns>
    private static SniffedVideo Create(
        string url,
        VideoFormat format,
        string? resolution = null,
        long? bandwidth = null,
        string? contentType = null,
        string source = "network")
        => new()
        {
            Url = url,
            NormalizedUrl = VideoUrlMatcher.Normalize(url),
            Format = format,
            Resolution = resolution,
            Bandwidth = bandwidth,
            ContentType = contentType,
            Source = source
        };
}
