/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Downloads
*文件名： HlsPlanBuilderTests
*版本号： V1.0.0.0
*唯一标识：4b20ac59-e8ab-4d3e-a8d8-04b8e6542245
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 03:40:00
*描述：HLS 下载计划构建器的单元测试，覆盖媒体清单、主清单、直播、DRM 与 AES-128 五类情形。
*
*=================================================
*修改标记
*修改时间：2026/9/13 03:40:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Downloads;

namespace BrowserVideoGrabber.Tests.Downloads;

/// <summary>
/// <see cref="HlsPlanBuilder"/> 的行为验证。
/// </summary>
/// <remarks>
/// 构建器的职责是「把清单文本翻译成可执行的下载计划，或在无法下载时给出准确原因」。
/// 因此本组用例的重点不是解析细节（那由 <c>M3u8ParserTests</c> 覆盖），
/// 而是<b>分类是否准确</b>：主清单、直播、DRM、无法识别的加密，四者必须给出不同的结论，
/// 否则调用方无从决定「该回退 ffmpeg」还是「该直接告诉用户不支持」。
/// </remarks>
public sealed class HlsPlanBuilderTests
{
    private static readonly Uri PlaylistUri = new("https://cdn.example.com/path/video.m3u8");

    /// <summary>
    /// 普通媒体清单应产出一份可用计划，且各分片的起始时间是可累加的前缀和。
    /// </summary>
    [Fact]
    public void Build_ShouldProducePlan_ForMediaPlaylist()
    {
        const string text = """
            #EXTM3U
            #EXT-X-VERSION:3
            #EXT-X-TARGETDURATION:10
            #EXTINF:10.0,
            video00000.jpeg
            #EXTINF:2.5,
            video00001.jpeg
            #EXT-X-ENDLIST
            """;

        var result = HlsPlanBuilder.Build(text, PlaylistUri);

        Assert.True(result.Success);
        Assert.False(result.IsLive);
        Assert.False(result.IsDrmProtected);

        var plan = result.Plan!;
        Assert.Equal(2, plan.SegmentCount);
        Assert.Equal(TimeSpan.FromSeconds(12.5), plan.TotalDuration);
        Assert.Equal(TimeSpan.Zero, plan.SegmentStartOffsets[0]);
        Assert.Equal(TimeSpan.FromSeconds(10), plan.SegmentStartOffsets[1]);
        Assert.Equal("https://cdn.example.com/path/video00000.jpeg", plan.Segments[0].Uri);
    }

    /// <summary>
    /// 主清单不能直接下载：它只描述清晰度，必须先取对应的媒体清单。
    /// 调用方据此走「选变体 → 二次拉取」的分支。
    /// </summary>
    [Fact]
    public void Build_ShouldReportMasterPlaylist_WhenGivenMaster()
    {
        const string text = """
            #EXTM3U
            #EXT-X-STREAM-INF:BANDWIDTH=1000000,RESOLUTION=640x360
            640x360/video.m3u8
            #EXT-X-STREAM-INF:BANDWIDTH=2000000,RESOLUTION=842x480
            842x480/video.m3u8
            """;

        var result = HlsPlanBuilder.Build(text, PlaylistUri);

        Assert.False(result.Success);
        Assert.True(result.IsMasterPlaylist);

        // 主清单不是错误，而是需要继续解析的信号，因此不标注可重试性
        Assert.False(result.IsDrmProtected);
    }

    /// <summary>
    /// 无 #EXT-X-ENDLIST 的清单是直播流：分片列表没有终点，C# 侧无法规划完整下载。
    /// </summary>
    [Fact]
    public void Build_ShouldReportLive_WhenEndListMissing()
    {
        const string text = """
            #EXTM3U
            #EXT-X-TARGETDURATION:10
            #EXTINF:10.0,
            s0.ts
            #EXTINF:10.0,
            s1.ts
            """;

        var result = HlsPlanBuilder.Build(text, PlaylistUri);

        Assert.False(result.Success);
        Assert.True(result.IsLive);
        Assert.NotNull(result.Error);
    }

    /// <summary>
    /// DRM 内容必须在开工前明确拒绝，而不是产出「能下载但打不开」的假成功。
    /// </summary>
    [Fact]
    public void Build_ShouldRejectDrm_WithoutRetrying()
    {
        const string text = """
            #EXTM3U
            #EXT-X-KEY:METHOD=SAMPLE-AES,URI="skd://drm"
            #EXTINF:10.0,
            s0.ts
            #EXT-X-ENDLIST
            """;

        var result = HlsPlanBuilder.Build(text, PlaylistUri);

        Assert.False(result.Success);
        Assert.True(result.IsDrmProtected);

        // 重试多少次都不会解密成功，必须标记为不可重试，避免白白耗尽重试额度
        Assert.False(result.IsRetryable);
    }

    /// <summary>
    /// AES-128 是本工具支持的能力：密钥地址必须解析为绝对地址，IV 原样保留。
    /// </summary>
    [Fact]
    public void Build_ShouldKeepAes128KeyAndIv()
    {
        const string text = """
            #EXTM3U
            #EXT-X-KEY:METHOD=AES-128,URI="crypt.key?auth=1",IV=0x0000000000000000000000000000000A
            #EXTINF:10.0,
            s0.ts
            #EXT-X-ENDLIST
            """;

        var result = HlsPlanBuilder.Build(text, PlaylistUri);

        Assert.True(result.Success);

        var plan = result.Plan!;
        Assert.Equal(M3u8Encryption.Aes128, plan.Encryption);
        Assert.Equal("https://cdn.example.com/path/crypt.key?auth=1", plan.KeyUri);
        Assert.Equal("0x0000000000000000000000000000000A", plan.KeyIv);
    }

    /// <summary>
    /// 声明为 AES-128 却没有密钥地址，等价于无法解密，必须直接失败。
    /// </summary>
    [Fact]
    public void Build_ShouldFail_WhenAes128WithoutKeyUri()
    {
        const string text = """
            #EXTM3U
            #EXT-X-KEY:METHOD=AES-128
            #EXTINF:10.0,
            s0.ts
            #EXT-X-ENDLIST
            """;

        var result = HlsPlanBuilder.Build(text, PlaylistUri);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    /// <summary>
    /// 媒体序号会被带入计划，用于在清单未声明 IV 时推导解密向量。
    /// </summary>
    [Fact]
    public void Build_ShouldCarryMediaSequence()
    {
        const string text = """
            #EXTM3U
            #EXT-X-MEDIA-SEQUENCE:7
            #EXTINF:10.0,
            s0.ts
            #EXT-X-ENDLIST
            """;

        var result = HlsPlanBuilder.Build(text, PlaylistUri);

        Assert.True(result.Success);
        Assert.Equal(7L, result.Plan!.MediaSequence);
    }

    /// <summary>
    /// 空内容或非播放列表内容（例如站点返回的 HTML 错误页）必须被识别为失败，而不是抛异常。
    /// </summary>
    /// <param name="text">待解析内容。</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html><body>403 Forbidden</body></html>")]
    public void Build_ShouldFailGracefully_ForInvalidContent(string? text)
    {
        var result = HlsPlanBuilder.Build(text, PlaylistUri);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    /// <summary>
    /// 清晰度信息由调用方（已从主清单中选出变体）回填，避免二次解析主清单。
    /// </summary>
    [Fact]
    public void Build_ShouldCarryVariantMetadata()
    {
        const string text = """
            #EXTM3U
            #EXTINF:10.0,
            s0.ts
            #EXT-X-ENDLIST
            """;

        var result = HlsPlanBuilder.Build(text, PlaylistUri, "842x480", 2000000);

        Assert.True(result.Success);
        Assert.Equal("842x480", result.Plan!.Resolution);
        Assert.Equal(2000000L, result.Plan.Bandwidth);
    }
}
