/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Downloads
*文件名： HlsDownloadHandlerTests
*版本号： V1.0.0.0
*唯一标识：7b3c1e90-4f2a-4d81-9b2c-2e0d6f5a1c44
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 04:20:00
*描述：HlsDownloadHandler 单测，用假 fetcher / 假 fs / 假进程运行器覆盖「取片→合并」整条 C# 链路。
*
*=================================================
*修改标记
*修改时间：2026/9/13 04:20:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BrowserVideoGrabber.Core.Abstractions;
using BrowserVideoGrabber.Core.Downloads;
using BrowserVideoGrabber.Core.Ffmpeg;
using BrowserVideoGrabber.Core.Models;
using BrowserVideoGrabber.Infrastructure.Downloads;
using BrowserVideoGrabber.Infrastructure.Ffmpeg;
using BrowserVideoGrabber.Tests.Fakes;
using Xunit;

namespace BrowserVideoGrabber.Tests.Downloads;

/// <summary>
/// <see cref="HlsDownloadHandler"/> 的单测。
/// </summary>
/// <remarks>
/// 通过内存假实现精确构造「全部有效」「部分被占位污染」「全部污染」「直播回退 ffmpeg」
/// 「主清单选清晰度」「AES-128 解密」六类场景，完全不触碰真实网络与真实 ffmpeg。
/// </remarks>
public class HlsDownloadHandlerTests
{
    private const string FfmpegPath = @"D:\tools\ffmpeg\ffmpeg.exe";
    private const string OutputPath = @"D:\out\movie.mp4";

    /// <summary>构造一个已定位到内存存在路径的 ffmpeg 定位器（测试不启动真实 ffmpeg）。</summary>
    private static FfmpegLocator CreateLocator(FakeFileSystem fileSystem)
    {
        fileSystem.SeedFile(FfmpegPath, new byte[] { 1 });
        return new FfmpegLocator(fileSystem, FfmpegPath, appDirectory: null, enableEnvironmentSearch: false);
    }

    /// <summary>
    /// 全部分片有效时，应走「取片→拼接→ffmpeg 合并」并返回完整成功，且产物为 mp4。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_AllValidSegments_ReturnsOk()
    {
        var fs = new FakeFileSystem();
        fs.SeedFile(OutputPath, 4096); // 模拟 ffmpeg 已写出 mp4
        var fetcher = new FakeMediaFetcher(fs);
        var url = "https://a.com/hls/media.m3u8";

        fetcher.SeedText(url, BuildMediaPlaylist(false, ("seg0.ts", 2.0), ("seg1.ts", 2.0), ("seg2.ts", 2.0)));
        fetcher.SeedFile("https://a.com/hls/seg0.ts", MakeTsSample(0));
        fetcher.SeedFile("https://a.com/hls/seg1.ts", MakeTsSample(1));
        fetcher.SeedFile("https://a.com/hls/seg2.ts", MakeTsSample(2));

        var handler = CreateHandler(fetcher, fs);
        var task = CreateTask(url);

        var result = await handler.DownloadAsync(task, new Progress<DownloadProgress>(), default);

        Assert.True(result.Success);
        Assert.False(result.IsPartial);
        Assert.Equal(OutputPath, result.OutputPath);
    }

    /// <summary>
    /// 部分分片被 CDN 占位污染（image/jpeg）时，应接受「部分成功」并如实标注缺失时段。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_PartialDecoy_ReturnsOkPartial()
    {
        var fs = new FakeFileSystem();
        fs.SeedFile(OutputPath, 4096);
        var fetcher = new FakeMediaFetcher(fs);
        var url = "https://a.com/hls/media.m3u8";

        fetcher.SeedText(url, BuildMediaPlaylist(false, ("seg0.ts", 2.0), ("seg1.ts", 2.0), ("seg2.ts", 2.0), ("seg3.ts", 2.0), ("seg4.ts", 2.0)));
        fetcher.SeedFile("https://a.com/hls/seg0.ts", MakeTsSample(0));
        fetcher.SeedFile("https://a.com/hls/seg1.ts", MakeTsSample(1));
        fetcher.SeedFile("https://a.com/hls/seg2.ts", MakeTsSample(2));
        // seg3 / seg4 为占位污染（图片类型），校验阶段会被丢弃
        fetcher.SeedFile("https://a.com/hls/seg3.ts", new byte[] { 0x47, 0x01 }, "image/jpeg");
        fetcher.SeedFile("https://a.com/hls/seg4.ts", new byte[] { 0x47, 0x02 }, "image/jpeg");

        var handler = CreateHandler(fetcher, fs);
        var task = CreateTask(url);

        var result = await handler.DownloadAsync(task, new Progress<DownloadProgress>(), default);

        Assert.True(result.Success);
        Assert.True(result.IsPartial);
        Assert.NotNull(result.PartialDetail);
        Assert.Contains("缺失时段", result.PartialDetail!);
    }

    /// <summary>
    /// 下载成功后，分片工作目录必须被整体删除，不能在输出目录留下空的 <c>*.hls_segments</c>。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_Success_RemovesWorkDirectory()
    {
        var fs = new FakeFileSystem();
        fs.SeedFile(OutputPath, 4096);
        var fetcher = new FakeMediaFetcher(fs);
        var url = "https://a.com/hls/media.m3u8";

        fetcher.SeedText(url, BuildMediaPlaylist(false, ("seg0.ts", 2.0), ("seg1.ts", 2.0)));
        fetcher.SeedFile("https://a.com/hls/seg0.ts", MakeTsSample(0));
        fetcher.SeedFile("https://a.com/hls/seg1.ts", MakeTsSample(1));

        var handler = CreateHandler(fetcher, fs);

        var result = await handler.DownloadAsync(CreateTask(url), new Progress<DownloadProgress>(), default);

        Assert.True(result.Success);
        Assert.False(fs.DirectoryExists(OutputPath + ".hls_segments"));
        Assert.DoesNotContain(fs.FilePaths, path => path.Contains(".hls_segments"));
    }

    /// <summary>
    /// 下载失败同样要清理工作目录：失败时残留的分片往往比成功时更多。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_Failure_RemovesWorkDirectory()
    {
        var fs = new FakeFileSystem();
        var fetcher = new FakeMediaFetcher(fs);
        var url = "https://a.com/hls/media.m3u8";

        fetcher.SeedText(url, BuildMediaPlaylist(false, ("seg0.ts", 2.0), ("seg1.ts", 2.0)));
        fetcher.SeedFile("https://a.com/hls/seg0.ts", new byte[] { 0x47 }, "image/jpeg");
        fetcher.SeedFile("https://a.com/hls/seg1.ts", new byte[] { 0x47 }, "image/jpeg");

        var handler = CreateHandler(fetcher, fs);

        var result = await handler.DownloadAsync(CreateTask(url), new Progress<DownloadProgress>(), default);

        Assert.False(result.Success);
        Assert.False(fs.DirectoryExists(OutputPath + ".hls_segments"));
        Assert.DoesNotContain(fs.FilePaths, path => path.Contains(".hls_segments"));
    }

    /// <summary>
    /// 全部分片都被占位污染时，没有任何有效分片，应直接判定失败（不可重试）。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_AllDecoy_ReturnsFail()
    {
        var fs = new FakeFileSystem();
        var fetcher = new FakeMediaFetcher(fs);
        var url = "https://a.com/hls/media.m3u8";

        fetcher.SeedText(url, BuildMediaPlaylist(false, ("seg0.ts", 2.0), ("seg1.ts", 2.0), ("seg2.ts", 2.0)));
        fetcher.SeedFile("https://a.com/hls/seg0.ts", new byte[] { 0x47 }, "image/jpeg");
        fetcher.SeedFile("https://a.com/hls/seg1.ts", new byte[] { 0x47 }, "image/jpeg");
        fetcher.SeedFile("https://a.com/hls/seg2.ts", new byte[] { 0x47 }, "image/jpeg");

        var handler = CreateHandler(fetcher, fs);
        var task = CreateTask(url);

        var result = await handler.DownloadAsync(task, new Progress<DownloadProgress>(), default);

        Assert.False(result.Success);
        Assert.False(result.IsRetryable);
        Assert.Contains("污染", result.Error!);
    }

    /// <summary>
    /// 清单引用的分片地址全部取不到（典型是 404）时，应判定为「播放列表本身已失效的占位内容」，
    /// 而不是笼统地报「下载失败」—— 这决定了用户该重试还是该重新嗅探。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_AllSegmentsMissing_ReportsStalePlaylist()
    {
        var fs = new FakeFileSystem();
        var fetcher = new FakeMediaFetcher(fs);
        var url = "https://a.com/hls/stale.m3u8";

        fetcher.SeedText(url, BuildMediaPlaylist(false, ("video20000.jpeg", 8.0)));
        fetcher.FailWithMessage("https://a.com/hls/video20000.jpeg", "资源地址已失效（HTTP 404），通常为动态签名过期。");

        var handler = CreateHandler(fetcher, fs);
        var task = CreateTask(url);

        var result = await handler.DownloadAsync(task, new Progress<DownloadProgress>(), default);

        Assert.False(result.Success);
        Assert.False(result.IsRetryable);
        Assert.Contains("HTTP 404", result.Error!);
        Assert.Contains("占位", result.Error!);
    }

    /// <summary>
    /// 空清单（没有任何分片条目）应在建计划阶段就被拒，而不是走到分片下载阶段抛异常。
    /// </summary>
    /// <remarks>
    /// 这里断言「无法解析」而非「没有可下载的分片」：真实流程里空清单会先被
    /// <c>HlsPlanBuilder</c> 挡下，<c>BuildAllFailedDetail</c> 里的空清单分支只是防御性兜底。
    /// </remarks>
    [Fact]
    public async Task DownloadAsync_EmptyPlaylist_FailsWithParseError()
    {
        var fs = new FakeFileSystem();
        var fetcher = new FakeMediaFetcher(fs);
        var url = "https://a.com/hls/empty.m3u8";

        fetcher.SeedText(url, BuildMediaPlaylist(false));

        var handler = CreateHandler(fetcher, fs);
        var task = CreateTask(url);

        var result = await handler.DownloadAsync(task, new Progress<DownloadProgress>(), default);

        Assert.False(result.Success);
        Assert.Contains("无法解析", result.Error!);
    }

    /// <summary>
    /// 直播流（缺 #EXT-X-ENDLIST）无法规划完整下载，应改走 ffmpeg 回退。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_LiveStream_FallsBackToFfmpeg()
    {
        var fs = new FakeFileSystem();
        var fetcher = new FakeMediaFetcher(fs);
        var url = "https://a.com/hls/live.m3u8";

        fetcher.SeedText(url, BuildMediaPlaylist(true, ("seg0.ts", 2.0), ("seg1.ts", 2.0)));

        var fallback = new FakeDownloadHandler();
        var handler = CreateHandler(fetcher, fs, fallback);
        var task = CreateTask(url);

        var result = await handler.DownloadAsync(task, new Progress<DownloadProgress>(), default);

        Assert.True(result.Success);
        // 回退处理器确实被调用了一次
        Assert.Equal(1, fallback.AttemptCount);
    }

    /// <summary>
    /// 主清单（多清晰度）应自动选最高档变体并取回其媒体清单完成下载。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_MasterPlaylist_SelectsVariant()
    {
        var fs = new FakeFileSystem();
        fs.SeedFile(OutputPath, 4096);
        var fetcher = new FakeMediaFetcher(fs);
        var masterUrl = "https://a.com/hls/master.m3u8";
        var variantUrl = "https://a.com/hls/842x480/variant.m3u8";

        fetcher.SeedText(masterUrl, BuildMasterPlaylist((variantUrl, 2500000, "842x480"), ("https://a.com/hls/1280x720/variant.m3u8", 800000, "1280x720")));
        fetcher.SeedText(variantUrl, BuildMediaPlaylist(false, ("vseg0.ts", 2.0), ("vseg1.ts", 2.0)));
        fetcher.SeedFile("https://a.com/hls/842x480/vseg0.ts", MakeTsSample(10));
        fetcher.SeedFile("https://a.com/hls/842x480/vseg1.ts", MakeTsSample(11));

        var handler = CreateHandler(fetcher, fs);
        var task = CreateTask(masterUrl);

        var result = await handler.DownloadAsync(task, new Progress<DownloadProgress>(), default);

        Assert.True(result.Success);
        Assert.False(result.IsPartial);
    }

    /// <summary>
    /// AES-128 加密流应在 C# 侧逐片解密，解密后内容合法则拼入成品。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_Aes128_DecryptsSegments()
    {
        var fs = new FakeFileSystem();
        fs.SeedFile(OutputPath, 4096);
        var fetcher = new FakeMediaFetcher(fs);
        var url = "https://a.com/hls/aes.m3u8";
        var key = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x01 };

        fetcher.SeedText(url, BuildAes128Playlist("https://a.com/key.bin", ("seg0.ts", 2.0), ("seg1.ts", 2.0)));
        fetcher.SeedBytes("https://a.com/key.bin", key);

        // 用与下载器一致的 IV 推导（媒体序号 0 + 分片下标）加密合法 TS 明文
        fetcher.SeedFile("https://a.com/hls/seg0.ts", EncryptSample(0, key));
        fetcher.SeedFile("https://a.com/hls/seg1.ts", EncryptSample(1, key));

        var handler = CreateHandler(fetcher, fs);
        var task = CreateTask(url);

        var result = await handler.DownloadAsync(task, new Progress<DownloadProgress>(), default);

        Assert.True(result.Success);
        Assert.False(result.IsPartial);
    }

    /// <summary>
    /// 构造被测处理器。
    /// </summary>
    private static HlsDownloadHandler CreateHandler(FakeMediaFetcher fetcher, FakeFileSystem fs, IDownloadHandler? fallback = null)
        => new(fetcher, fs, CreateLocator(fs), new FakeProcessRunner(), new FfmpegOptions(), fallback);

    /// <summary>
    /// 构造下载任务。
    /// </summary>
    private static DownloadTask CreateTask(string url)
        => new() { Url = url, Format = VideoFormat.M3u8, OutputPath = OutputPath, Title = "movie" };

    /// <summary>
    /// 构造媒体播放列表文本。
    /// </summary>
    private static string BuildMediaPlaylist(bool live, params (string Uri, double Duration)[] segments)
    {
        var lines = new List<string> { "#EXTM3U", "#EXT-X-VERSION:3", "#EXT-X-MEDIA-SEQUENCE:0" };
        foreach (var (uri, duration) in segments)
        {
            lines.Add($"#EXTINF:{duration},");
            lines.Add(uri);
        }

        if (!live)
        {
            lines.Add("#EXT-X-ENDLIST");
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// 构造主播放列表文本（多档清晰度变体）。
    /// </summary>
    private static string BuildMasterPlaylist(params (string Uri, int Bandwidth, string Resolution)[] variants)
    {
        var lines = new List<string> { "#EXTM3U" };
        foreach (var (uri, bandwidth, resolution) in variants)
        {
            lines.Add($"#EXT-X-STREAM-INF:BANDWIDTH={bandwidth},RESOLUTION={resolution}");
            lines.Add(uri);
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// 构造 AES-128 加密的媒体播放列表文本。
    /// </summary>
    private static string BuildAes128Playlist(string keyUri, params (string Uri, double Duration)[] segments)
    {
        var lines = new List<string>
        {
            "#EXTM3U",
            "#EXT-X-VERSION:3",
            "#EXT-X-MEDIA-SEQUENCE:0",
            $"#EXT-X-KEY:METHOD=AES-128,URI=\"{keyUri}\""
        };
        foreach (var (uri, duration) in segments)
        {
            lines.Add($"#EXTINF:{duration},");
            lines.Add(uri);
        }

        lines.Add("#EXT-X-ENDLIST");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// 生成一段合法的 MPEG-TS 分片字节：首字节为同步字 0x47，长度与内容随下标变化以避免指纹重复。
    /// </summary>
    private static byte[] MakeTsSample(int index)
    {
        var length = 256 + index * 64;
        var bytes = new byte[length];
        bytes[0] = 0x47;
        for (var i = 1; i < length; i++)
        {
            bytes[i] = (byte)((i * 7 + index * 13) & 0xFF);
        }

        return bytes;
    }

    /// <summary>
    /// 用与下载器一致的 IV（媒体序号 0 + 分片下标）把合法 TS 明文加密，供伪造「加密分片」响应。
    /// </summary>
    private static byte[] EncryptSample(int index, byte[] key)
    {
        var plain = new byte[32];
        plain[0] = 0x47;
        for (var i = 1; i < plain.Length; i++)
        {
            plain[i] = (byte)((i + index) & 0xFF);
        }

        var iv = Aes128Decryptor.BuildIv(index);
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(plain, 0, plain.Length);
    }
}
