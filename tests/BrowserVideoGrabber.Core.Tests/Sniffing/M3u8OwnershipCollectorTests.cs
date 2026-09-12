/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Sniffing
*文件名： M3u8OwnershipCollectorTests
*版本号： V1.0.0.0
*唯一标识：348d433b-92ed-4a62-a37d-aa7f850b587c
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 04:02:00
*描述：M3u8OwnershipCollector 与 M3u8Playlist.BestVariant 的单元测试。
*
*=================================================
*修改标记
*修改时间：2026/9/13 04:02:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Downloads;
using BrowserVideoGrabber.Core.Sniffing;

namespace BrowserVideoGrabber.Tests.Sniffing;

/// <summary>
/// <see cref="M3u8OwnershipCollector"/> 的行为验证。
/// </summary>
public sealed class M3u8OwnershipCollectorTests
{
    private const string MasterUrl = "https://cdn.test/hls/video/index.m3u8";

    /// <summary>
    /// 主清单应把各档变体登记为「拥有的精确地址」，并把它们的目录一并登记。
    /// </summary>
    /// <remarks>
    /// 变体必须精确登记：它们是独立可下载的清单，若不登记就会各自成行，
    /// 同一个视频在列表里就会按清晰度出现好几条。
    /// </remarks>
    [Fact]
    public void Should_CollectVariantsAndTheirDirectories_FromMasterPlaylist()
    {
        const string body = """
            #EXTM3U
            #EXT-X-STREAM-INF:BANDWIDTH=5000000,RESOLUTION=1920x1080
            1080p/index.m3u8
            #EXT-X-STREAM-INF:BANDWIDTH=2000000,RESOLUTION=1280x720
            720p/index.m3u8
            """;

        var playlist = M3u8Parser.Parse(body, new Uri(MasterUrl));
        var ownership = M3u8OwnershipCollector.Collect(playlist, MasterUrl);

        Assert.Contains("https://cdn.test/hls/video/1080p/index.m3u8", ownership.OwnedUrls);
        Assert.Contains("https://cdn.test/hls/video/720p/index.m3u8", ownership.OwnedUrls);

        // 清单自身目录也要登记：部分站点的分片与清单同级摆放
        Assert.Contains("https://cdn.test/hls/video", ownership.OwnedDirectories);
        Assert.Contains("https://cdn.test/hls/video/1080p", ownership.OwnedDirectories);
        Assert.Contains("https://cdn.test/hls/video/720p", ownership.OwnedDirectories);
    }

    /// <summary>
    /// 媒体清单应把分片所在目录登记为归属目录，把 init 段登记为精确地址。
    /// </summary>
    /// <remarks>
    /// 分片数量可达上千，逐个登记地址既占内存也无必要，按目录折叠一次即可覆盖全部。
    /// </remarks>
    [Fact]
    public void Should_CollectSegmentDirectories_FromMediaPlaylist()
    {
        const string body = """
            #EXTM3U
            #EXT-X-TARGETDURATION:10
            #EXT-X-MAP:URI="init.mp4"
            #EXTINF:10.0,
            seg/seg-1.m4s
            #EXTINF:10.0,
            seg/seg-2.m4s
            """;

        var playlist = M3u8Parser.Parse(body, new Uri(MasterUrl));
        var ownership = M3u8OwnershipCollector.Collect(playlist, MasterUrl);

        Assert.Contains("https://cdn.test/hls/video/init.mp4", ownership.OwnedUrls);
        Assert.Contains("https://cdn.test/hls/video/seg", ownership.OwnedDirectories);

        // 分片本身不应被逐个登记成精确地址
        Assert.DoesNotContain("https://cdn.test/hls/video/seg/seg-1.m4s", ownership.OwnedUrls);
    }

    /// <summary>
    /// 无法解析的内容不产生任何归属关系。
    /// </summary>
    [Fact]
    public void Should_ReturnEmptyOwnership_WhenPlaylistInvalid()
    {
        var ownership = M3u8OwnershipCollector.Collect(M3u8Playlist.Invalid, MasterUrl);

        Assert.Empty(ownership.OwnedUrls);
        Assert.Empty(ownership.OwnedDirectories);
    }

    /// <summary>
    /// 空参数不应抛异常，只返回空归属。
    /// </summary>
    [Fact]
    public void Should_HandleNullPlaylist()
    {
        var ownership = M3u8OwnershipCollector.Collect(null);

        Assert.Empty(ownership.OwnedUrls);
        Assert.Empty(ownership.OwnedDirectories);
    }
}

/// <summary>
/// <see cref="M3u8Playlist.BestVariant"/> 的行为验证。
/// </summary>
public sealed class M3u8PlaylistTests
{
    private const string Url = "https://cdn.test/hls/video/index.m3u8";

    /// <summary>
    /// 「最佳清晰度」不能想当然取第一档：主清单里各档的排列顺序没有规范约束。
    /// </summary>
    [Fact]
    public void Should_PickHighestBandwidthVariant_RegardlessOfOrder()
    {
        const string body = """
            #EXTM3U
            #EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360
            360p/index.m3u8
            #EXT-X-STREAM-INF:BANDWIDTH=5000000,RESOLUTION=1920x1080
            1080p/index.m3u8
            #EXT-X-STREAM-INF:BANDWIDTH=2000000,RESOLUTION=1280x720
            720p/index.m3u8
            """;

        var playlist = M3u8Parser.Parse(body, new Uri(Url));
        var best = playlist.BestVariant;

        Assert.NotNull(best);
        Assert.Equal("1920x1080", best.Resolution);
        Assert.Equal(5_000_000, best.Bandwidth);
    }

    /// <summary>
    /// 带分辨率的变体优先于未声明分辨率的变体，即使后者码率更高。
    /// </summary>
    /// <remarks>
    /// 界面上「分辨率」列的价值高于码率：用户看到 <c>1920x1080</c> 立刻知道是什么画质，
    /// 而一个码率数字需要换算才有意义。
    /// </remarks>
    [Fact]
    public void Should_PreferVariantWithResolution()
    {
        const string body = """
            #EXTM3U
            #EXT-X-STREAM-INF:BANDWIDTH=9000000
            audio-only/index.m3u8
            #EXT-X-STREAM-INF:BANDWIDTH=2000000,RESOLUTION=1280x720
            720p/index.m3u8
            """;

        var playlist = M3u8Parser.Parse(body, new Uri(Url));

        Assert.Equal("1280x720", playlist.BestVariant?.Resolution);
    }

    /// <summary>
    /// 媒体清单没有变体，最佳变体应为空。
    /// </summary>
    [Fact]
    public void Should_ReturnNullBestVariant_ForMediaPlaylist()
    {
        const string body = """
            #EXTM3U
            #EXTINF:10.0,
            seg-1.ts
            """;

        var playlist = M3u8Parser.Parse(body, new Uri(Url));

        Assert.Null(playlist.BestVariant);
    }
}
