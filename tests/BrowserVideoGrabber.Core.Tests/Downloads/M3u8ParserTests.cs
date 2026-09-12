/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Downloads
*文件名： M3u8ParserTests
*版本号： V1.0.0.0
*唯一标识：319b1a54-573b-41f5-a01c-58547c08d064
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:36:00
*描述：M3u8Parser 的单元测试，覆盖主/子播放列表、加密判定、init 段与相对路径解析。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:36:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Downloads;

namespace BrowserVideoGrabber.Tests.Downloads;

/// <summary>
/// <see cref="M3u8Parser"/> 的行为验证。
/// </summary>
public sealed class M3u8ParserTests
{
    private static readonly Uri BaseUri = new("https://a.com/hls/index.m3u8");

    /// <summary>
    /// 媒体播放列表：应解析出全部分片并累加出总时长。
    /// </summary>
    [Fact]
    public void Parse_ShouldExtractSegmentsAndTotalDuration()
    {
        const string content = """
            #EXTM3U
            #EXT-X-VERSION:3
            #EXT-X-TARGETDURATION:10
            #EXTINF:10.0,
            seg_000.ts
            #EXTINF:10.0,
            seg_001.ts
            #EXTINF:5.0,
            seg_002.ts
            #EXT-X-ENDLIST
            """;

        var playlist = M3u8Parser.Parse(content, BaseUri);

        Assert.True(playlist.IsValid);
        Assert.False(playlist.IsMasterPlaylist);
        Assert.Equal(3, playlist.Segments.Count);
        Assert.Equal(TimeSpan.FromSeconds(25), playlist.TotalDuration);
        Assert.Equal(M3u8Encryption.None, playlist.Encryption);
    }

    /// <summary>
    /// 分片地址为相对路径时，必须基于播放列表地址解析成绝对地址，
    /// 否则 ffmpeg 与原生下载器都无法定位分片。
    /// </summary>
    [Fact]
    public void Parse_ShouldResolveRelativeSegmentUriAgainstBase()
    {
        const string content = """
            #EXTM3U
            #EXTINF:10.0,
            seg_000.ts
            """;

        var playlist = M3u8Parser.Parse(content, BaseUri);

        Assert.Equal("https://a.com/hls/seg_000.ts", playlist.Segments[0].Uri);
    }

    /// <summary>
    /// 主播放列表：应提取各档清晰度的码率与分辨率，供用户选择。
    /// </summary>
    [Fact]
    public void Parse_ShouldExtractVariantsFromMasterPlaylist()
    {
        const string content = """
            #EXTM3U
            #EXT-X-STREAM-INF:BANDWIDTH=5000000,RESOLUTION=1920x1080
            1080p/index.m3u8
            #EXT-X-STREAM-INF:BANDWIDTH=2500000,RESOLUTION=1280x720
            720p/index.m3u8
            """;

        var playlist = M3u8Parser.Parse(content, BaseUri);

        Assert.True(playlist.IsMasterPlaylist);
        Assert.Equal(2, playlist.Variants.Count);
        Assert.Equal(5000000, playlist.Variants[0].Bandwidth);
        Assert.Equal("1920x1080", playlist.Variants[0].Resolution);
        Assert.Equal("https://a.com/hls/1080p/index.m3u8", playlist.Variants[0].Uri);
        Assert.Equal("1280x720", playlist.Variants[1].Resolution);
    }

    /// <summary>
    /// AES-128 加密流：本工具可以支持（ffmpeg 自动取 key 解密），
    /// 必须被识别为可下载而不是受保护内容。
    /// </summary>
    [Fact]
    public void Parse_ShouldDetectAes128Encryption()
    {
        const string content = """
            #EXTM3U
            #EXT-X-KEY:METHOD=AES-128,URI="https://k.com/key.bin"
            #EXTINF:10.0,
            seg_000.ts
            """;

        var playlist = M3u8Parser.Parse(content, BaseUri);

        Assert.Equal(M3u8Encryption.Aes128, playlist.Encryption);
        Assert.Equal("https://k.com/key.bin", playlist.KeyUri);
        Assert.False(playlist.IsDrmProtected);
    }

    /// <summary>
    /// SAMPLE-AES 属于 DRM 范畴，本工具不支持，必须被明确标记，
    /// 以便界面给出「受保护内容」提示，而不是让用户误以为程序卡死。
    /// </summary>
    [Fact]
    public void Parse_ShouldFlagDrmForSampleAes()
    {
        const string content = """
            #EXTM3U
            #EXT-X-KEY:METHOD=SAMPLE-AES,URI="skd://drm-key"
            #EXTINF:10.0,
            seg_000.ts
            """;

        var playlist = M3u8Parser.Parse(content, BaseUri);

        Assert.Equal(M3u8Encryption.Drm, playlist.Encryption);
        Assert.True(playlist.IsDrmProtected);
    }

    /// <summary>
    /// SESSION-KEY 同样代表 DRM 保护。
    /// </summary>
    [Fact]
    public void Parse_ShouldFlagDrmForSessionKey()
    {
        const string content = """
            #EXTM3U
            #EXT-X-SESSION-KEY:METHOD=AES-128,URI="skd://session"
            """;

        var playlist = M3u8Parser.Parse(content, BaseUri);

        Assert.True(playlist.IsDrmProtected);
    }

    /// <summary>
    /// METHOD=NONE 表示显式关闭加密，不得被误判为加密流。
    /// </summary>
    [Fact]
    public void Parse_ShouldTreatMethodNoneAsUnencrypted()
    {
        const string content = """
            #EXTM3U
            #EXT-X-KEY:METHOD=NONE
            #EXTINF:10.0,
            seg_000.ts
            """;

        var playlist = M3u8Parser.Parse(content, BaseUri);

        Assert.Equal(M3u8Encryption.None, playlist.Encryption);
    }

    /// <summary>
    /// fMP4（DASH 风格）播放列表通过 EXT-X-MAP 指定 init 段，必须被解析出来。
    /// </summary>
    [Fact]
    public void Parse_ShouldExtractInitSegmentFromMap()
    {
        const string content = """
            #EXTM3U
            #EXT-X-MAP:URI="init.mp4"
            #EXTINF:4.0,
            seg_000.m4s
            """;

        var playlist = M3u8Parser.Parse(content, BaseUri);

        Assert.Equal("https://a.com/hls/init.mp4", playlist.InitSegmentUri);
        Assert.Equal("https://a.com/hls/seg_000.m4s", playlist.Segments[0].Uri);
    }

    /// <summary>
    /// 空内容与畸形内容不得抛异常，应返回无效结果，由调用方决定如何提示。
    /// </summary>
    /// <param name="content">待解析内容。</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("这不是一个 m3u8 文件")]
    [InlineData("#EXTM3U\n#EXTINF:abc,\n???")]
    public void Parse_ShouldNotThrow_OnMalformedContent(string content)
    {
        var playlist = M3u8Parser.Parse(content, BaseUri);

        Assert.False(playlist.IsValid);
        Assert.Empty(playlist.Segments);
    }

    /// <summary>
    /// 内容为空时传入 null 基础地址也不应抛异常。
    /// </summary>
    [Fact]
    public void Parse_ShouldHandleNullBaseUri()
    {
        const string content = """
            #EXTM3U
            #EXTINF:10.0,
            https://cdn.com/seg_000.ts
            """;

        var playlist = M3u8Parser.Parse(content, null);

        Assert.True(playlist.IsValid);
        Assert.Equal("https://cdn.com/seg_000.ts", playlist.Segments[0].Uri);
    }

    /// <summary>
    /// 行尾 CR 与多余空白不应影响解析结果（Windows 换行的播放列表很常见）。
    /// </summary>
    [Fact]
    public void Parse_ShouldTolerateCrLfAndWhitespace()
    {
        var content = "#EXTM3U\r\n#EXTINF:10.0,\r\n  seg_000.ts  \r\n";

        var playlist = M3u8Parser.Parse(content, BaseUri);

        Assert.Single(playlist.Segments);
        Assert.Equal("https://a.com/hls/seg_000.ts", playlist.Segments[0].Uri);
    }

    /// <summary>
    /// 媒体序号是推导解密向量（IV）的依据：清单未声明 IV 时，
    /// 规范要求用「媒体序号 + 分片下标」作为 CBC 的初始化向量。
    /// </summary>
    [Fact]
    public void Parse_ShouldExtractMediaSequence()
    {
        const string content = """
            #EXTM3U
            #EXT-X-MEDIA-SEQUENCE:42
            #EXTINF:10.0,
            seg_000.ts
            #EXT-X-ENDLIST
            """;

        var playlist = M3u8Parser.Parse(content, BaseUri);

        Assert.Equal(42L, playlist.MediaSequence);
    }

    /// <summary>
    /// 未声明 MEDIA-SEQUENCE 时默认为 0。
    /// </summary>
    [Fact]
    public void Parse_ShouldDefaultMediaSequenceToZero()
    {
        const string content = """
            #EXTM3U
            #EXTINF:10.0,
            seg_000.ts
            """;

        var playlist = M3u8Parser.Parse(content, BaseUri);

        Assert.Equal(0L, playlist.MediaSequence);
    }

    /// <summary>
    /// #EXT-X-KEY 中的 IV 属性必须被解析出来；显式声明时优先于媒体序号推导。
    /// </summary>
    [Fact]
    public void Parse_ShouldExtractIvFromKeyTag()
    {
        const string content = """
            #EXTM3U
            #EXT-X-KEY:METHOD=AES-128,URI="key.bin",IV=0x0123456789ABCDEF0123456789ABCDEF
            #EXTINF:10.0,
            seg_000.ts
            """;

        var playlist = M3u8Parser.Parse(content, BaseUri);

        Assert.Equal(M3u8Encryption.Aes128, playlist.Encryption);
        Assert.Equal("0x0123456789ABCDEF0123456789ABCDEF", playlist.KeyIv);
    }

    /// <summary>
    /// 含 #EXT-X-ENDLIST 的是点播（VOD），分片列表有确定的终点。
    /// </summary>
    [Fact]
    public void Parse_ShouldNotBeLive_WhenEndListPresent()
    {
        const string content = """
            #EXTM3U
            #EXTINF:10.0,
            seg_000.ts
            #EXT-X-ENDLIST
            """;

        var playlist = M3u8Parser.Parse(content, BaseUri);

        Assert.False(playlist.IsLive);
    }

    /// <summary>
    /// 缺少 #EXT-X-ENDLIST 且含分片的是直播流：分片列表会持续增长，
    /// C# 侧无法据此规划一次完整的下载。
    /// </summary>
    [Fact]
    public void Parse_ShouldBeLive_WhenEndListMissing()
    {
        const string content = """
            #EXTM3U
            #EXT-X-TARGETDURATION:10
            #EXTINF:10.0,
            seg_000.ts
            """;

        var playlist = M3u8Parser.Parse(content, BaseUri);

        Assert.True(playlist.IsLive);
    }

    /// <summary>
    /// 主清单不含分片，不适用「直播」判定，避免把「多清晰度点播」误判成直播而错走录制路径。
    /// </summary>
    [Fact]
    public void Parse_ShouldNotBeLive_ForMasterPlaylist()
    {
        const string content = """
            #EXTM3U
            #EXT-X-STREAM-INF:BANDWIDTH=1000000,RESOLUTION=640x360
            640x360/index.m3u8
            """;

        var playlist = M3u8Parser.Parse(content, BaseUri);

        Assert.False(playlist.IsLive);
    }
}
