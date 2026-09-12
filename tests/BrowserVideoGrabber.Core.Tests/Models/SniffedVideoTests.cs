/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Models
*文件名： SniffedVideoTests
*版本号： V1.0.0.0
*唯一标识：60cffbc2-3ff3-47f0-a7dc-e64fd9a111ae
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 04:08:00
*描述：SniffedVideo 的单元测试，覆盖展示标题的辨识度处理与标识替换。
*
*=================================================
*修改标记
*修改时间：2026/9/13 04:08:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Models;
using BrowserVideoGrabber.Core.Sniffing;

namespace BrowserVideoGrabber.Tests.Models;

/// <summary>
/// <see cref="SniffedVideo"/> 的行为验证。
/// </summary>
public sealed class SniffedVideoTests
{
    /// <summary>
    /// 文件名具备辨识度时，标题就是文件名本身。
    /// </summary>
    /// <param name="url">资源地址。</param>
    /// <param name="expected">期望标题。</param>
    [Theory]
    [InlineData("https://cdn.test/hls/movie.mp4", "movie.mp4")]
    [InlineData("https://cdn.test/hls/video-2024.m3u8", "video-2024.m3u8")]
    [InlineData("https://cdn.test/hls/channel-5/seg-0012.ts", "seg-0012.ts")]
    public void Should_UseFileName_WhenItIsDistinctive(string url, string expected)
        => Assert.Equal(expected, Create(url).DisplayTitle);

    /// <summary>
    /// 文件名是通用名时必须带上父目录名，否则同一页面上的多个视频会显示成同一个名字。
    /// </summary>
    /// <remarks>
    /// <c>index.m3u8</c> 这类名字在流媒体站点上极其普遍。归并之后一个视频只剩一行，
    /// 若标题还是 <c>index.m3u8</c>，用户根本分不清哪一行是哪个视频。
    /// </remarks>
    [Theory]
    [InlineData("https://cdn.test/hls/movie-a/index.m3u8", "movie-a/index.m3u8")]
    [InlineData("https://cdn.test/hls/movie-b/playlist.m3u8", "movie-b/playlist.m3u8")]
    [InlineData("https://cdn.test/hls/movie-c/manifest.mpd", "movie-c/manifest.mpd")]
    [InlineData("https://cdn.test/hls/movie-d/master.m3u8", "movie-d/master.m3u8")]
    public void Should_PrefixParentDirectory_ForGenericNames(string url, string expected)
        => Assert.Equal(expected, Create(url).DisplayTitle);

    /// <summary>
    /// 父目录本身也是通用名时不再叠加，避免出现同样无法分辨的标题。
    /// </summary>
    [Fact]
    public void Should_NotStackGenericParentDirectory()
        => Assert.Equal("playlist.m3u8", Create("https://cdn.test/hls/playlist.m3u8").DisplayTitle);

    /// <summary>
    /// 地址没有目录层级时退回文件名，不会把主机名当成父目录。
    /// </summary>
    [Fact]
    public void Should_NotTreatHostAsParentDirectory()
        => Assert.Equal("index.m3u8", Create("https://cdn.test/index.m3u8").DisplayTitle);

    /// <summary>
    /// 归一化地址为空时回退到原始地址，两者都空时返回空串。
    /// </summary>
    [Fact]
    public void Should_FallBackToRawUrl_WhenNormalizedUrlMissing()
    {
        var video = new SniffedVideo
        {
            Url = "https://cdn.test/hls/movie.mp4",
            Format = VideoFormat.Mp4
        };

        Assert.Equal("movie.mp4", video.DisplayTitle);
    }

    /// <summary>
    /// 替换标识时其余字段必须原样保留，否则界面在原行刷新时会丢掉已展示的信息。
    /// </summary>
    [Fact]
    public void Should_PreserveAllFields_WhenReplacingId()
    {
        var original = new SniffedVideo
        {
            Url = "https://cdn.test/hls/index.m3u8",
            NormalizedUrl = "https://cdn.test/hls/index.m3u8",
            Format = VideoFormat.M3u8,
            ContentType = "application/vnd.apple.mpegurl",
            Resolution = "1920x1080",
            Bandwidth = 5_000_000,
            Source = "network"
        };

        var id = VideoFamilyIndex.CreateStableId("url:x");
        var replaced = original.WithId(id);

        Assert.Equal(id, replaced.Id);
        Assert.Equal(original.Url, replaced.Url);
        Assert.Equal(original.NormalizedUrl, replaced.NormalizedUrl);
        Assert.Equal(original.Format, replaced.Format);
        Assert.Equal(original.ContentType, replaced.ContentType);
        Assert.Equal(original.Resolution, replaced.Resolution);
        Assert.Equal(original.Bandwidth, replaced.Bandwidth);
        Assert.Equal(original.Source, replaced.Source);
        Assert.Equal(original.DetectedAt, replaced.DetectedAt);
    }

    /// <summary>
    /// 构造一个嗅探结果。
    /// </summary>
    /// <param name="url">资源地址。</param>
    /// <returns>嗅探结果实例。</returns>
    private static SniffedVideo Create(string url) => new()
    {
        Url = url,
        NormalizedUrl = VideoUrlMatcher.Normalize(url),
        Format = VideoFormat.M3u8
    };
}
