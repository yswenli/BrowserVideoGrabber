/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Sniffing
*文件名： VideoUrlMatcherTests
*版本号： V1.0.0.0
*唯一标识：45daa0a2-ec90-4074-a39b-b7ea2db74a0f
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:34:00
*描述：VideoUrlMatcher 的单元测试，覆盖格式识别、归一化与去重指纹。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:34:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Models;
using BrowserVideoGrabber.Core.Sniffing;

namespace BrowserVideoGrabber.Tests.Sniffing;

/// <summary>
/// <see cref="VideoUrlMatcher"/> 的行为验证。
/// </summary>
public sealed class VideoUrlMatcherTests
{
    /// <summary>
    /// 依据 URL 后缀识别资源格式，且不受 query 参数、fragment 与大小写干扰。
    /// </summary>
    /// <param name="url">待识别的地址。</param>
    /// <param name="expected">期望识别出的格式。</param>
    [Theory]
    [InlineData("https://a.com/x/index.m3u8", VideoFormat.M3u8)]
    [InlineData("https://a.com/x/seg.ts?token=1", VideoFormat.Ts)]
    [InlineData("https://a.com/v.mp4#t=10", VideoFormat.Mp4)]
    [InlineData("https://a.com/d/init-stream0.m4s", VideoFormat.M4s)]
    [InlineData("https://a.com/d/manifest.mpd", VideoFormat.Mpd)]
    [InlineData("https://a.com/INDEX.M3U8", VideoFormat.M3u8)]
    [InlineData("https://a.com/SEG.TS", VideoFormat.Ts)]
    public void TryMatch_ShouldIdentifyFormatFromUrl(string url, VideoFormat expected)
    {
        var matched = VideoUrlMatcher.TryMatch(url, contentType: null, out var format);

        Assert.True(matched);
        Assert.Equal(expected, format);
    }

    /// <summary>
    /// 非视频资源（图片、脚本、样式表、页面文档）必须被拒绝，避免界面被误报刷屏。
    /// </summary>
    /// <param name="url">待识别的地址。</param>
    [Theory]
    [InlineData("https://a.com/a.jpg")]
    [InlineData("https://a.com/app.js")]
    [InlineData("https://a.com/style.css")]
    [InlineData("https://a.com/index.html")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url at all")]
    public void TryMatch_ShouldRejectNonVideoUrl(string url)
    {
        var matched = VideoUrlMatcher.TryMatch(url, contentType: null, out var format);

        Assert.False(matched);
        Assert.Equal(VideoFormat.Unknown, format);
    }

    /// <summary>
    /// 当 URL 本身没有可识别后缀时，应回退到 Content-Type 判断。
    /// </summary>
    /// <param name="contentType">响应头中的 Content-Type。</param>
    /// <param name="expected">期望识别出的格式。</param>
    [Theory]
    [InlineData("application/vnd.apple.mpegurl", VideoFormat.M3u8)]
    [InlineData("application/x-mpegURL", VideoFormat.M3u8)]
    [InlineData("video/mp4", VideoFormat.Mp4)]
    [InlineData("video/mp2t", VideoFormat.Ts)]
    [InlineData("application/dash+xml", VideoFormat.Mpd)]
    public void TryMatch_ShouldIdentifyFromContentType(string contentType, VideoFormat expected)
    {
        var matched = VideoUrlMatcher.TryMatch("https://a.com/stream", contentType, out var format);

        Assert.True(matched);
        Assert.Equal(expected, format);
    }

    /// <summary>
    /// Content-Type 带 charset 等参数后缀时仍应正确识别。
    /// </summary>
    [Fact]
    public void TryMatch_ShouldIgnoreContentTypeParameters()
    {
        var matched = VideoUrlMatcher.TryMatch(
            "https://a.com/stream",
            "application/vnd.apple.mpegurl; charset=utf-8",
            out var format);

        Assert.True(matched);
        Assert.Equal(VideoFormat.M3u8, format);
    }

    /// <summary>
    /// 无意义的通用 Content-Type（如 octet-stream）不得被误判为视频，
    /// 此时应继续回退到 URL 后缀判断。
    /// </summary>
    [Fact]
    public void TryMatch_ShouldFallBackToUrl_WhenContentTypeIsGeneric()
    {
        var matched = VideoUrlMatcher.TryMatch(
            "https://a.com/x/index.m3u8",
            "application/octet-stream",
            out var format);

        Assert.True(matched);
        Assert.Equal(VideoFormat.M3u8, format);
    }

    /// <summary>
    /// 归一化应小写协议与主机、去掉默认端口、去掉 query 与 fragment，但保留路径大小写。
    /// </summary>
    [Fact]
    public void Normalize_ShouldLowercaseSchemeAndHost_AndStripQueryFragment()
    {
        var normalized = VideoUrlMatcher.Normalize("HTTPS://A.COM:443/X/Index.M3U8?token=abc#frag");

        Assert.Equal("https://a.com/X/Index.M3U8", normalized);
    }

    /// <summary>
    /// 归一化应保留非默认端口，否则会指向错误的服务。
    /// </summary>
    [Fact]
    public void Normalize_ShouldKeepNonDefaultPort()
    {
        var normalized = VideoUrlMatcher.Normalize("http://a.com:8080/live/index.m3u8");

        Assert.Equal("http://a.com:8080/live/index.m3u8", normalized);
    }

    /// <summary>
    /// 去重指纹必须忽略动态 token 与 fragment，
    /// 否则同一个资源在页面播放过程中会被反复上报，把列表刷屏。
    /// </summary>
    [Fact]
    public void Fingerprint_ShouldIgnoreQueryAndFragment_SoSameResourceDeduplicates()
    {
        var first = VideoUrlMatcher.Fingerprint("https://a.com/hls/index.m3u8?token=aaa");
        var second = VideoUrlMatcher.Fingerprint("https://A.com/hls/INDEX.m3u8?token=bbb#x");

        Assert.Equal(first, second);
    }

    /// <summary>
    /// 不同清晰度的播放列表必须产生不同指纹，否则会被错误去重导致只留下一个清晰度。
    /// </summary>
    [Fact]
    public void Fingerprint_ShouldDistinguishDifferentQualityPaths()
    {
        var hd = VideoUrlMatcher.Fingerprint("https://a.com/hls/1080p/index.m3u8");
        var sd = VideoUrlMatcher.Fingerprint("https://a.com/hls/720p/index.m3u8");

        Assert.NotEqual(hd, sd);
    }
}
