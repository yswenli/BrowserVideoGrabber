/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Downloads
*文件名： HlsSegmentDownloaderTests
*版本号： V1.0.0.0
*唯一标识：4807c19d-1e61-41bd-8809-60d8a1dc12f7
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 01:15:00
*描述：HlsSegmentDownloader 的单元测试，覆盖全成功、占位跳过、重复指纹、抓取失败、AES-128 解密与缺失区间合并。
*
*=================================================
*修改标记
*修改时间：2026/9/13 01:15:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BrowserVideoGrabber.Core.Abstractions;
using BrowserVideoGrabber.Core.Downloads;
using BrowserVideoGrabber.Core.Models;
using BrowserVideoGrabber.Tests.Fakes;
using Xunit;

namespace BrowserVideoGrabber.Tests.Downloads;

/// <summary>
/// <see cref="HlsSegmentDownloader"/> 的行为验证。
/// </summary>
/// <remarks>
/// 全部场景用 <see cref="FakeMediaFetcher"/> + <see cref="FakeFileSystem"/> 构造，不联网。
/// 重点验证「坏分片不会进入成品」与「缺失区间可被换算成时间段」这两项核心能力。
/// </remarks>
public sealed class HlsSegmentDownloaderTests
{
    private const int SegmentDurationSeconds = 10;

    /// <summary>构造一个测试计划：count 个各 10 秒的分片。</summary>
    private static HlsDownloadPlan BuildPlan(
        int count,
        M3u8Encryption encryption = M3u8Encryption.None,
        string? keyUri = null,
        string? keyIv = null)
    {
        var segments = new List<M3u8Segment>();
        var offsets = new List<TimeSpan>();
        double acc = 0;
        for (var i = 0; i < count; i++)
        {
            segments.Add(new M3u8Segment($"https://hls.example/seg{i}.ts", SegmentDurationSeconds));
            offsets.Add(TimeSpan.FromSeconds(acc));
            acc += SegmentDurationSeconds;
        }

        return new HlsDownloadPlan
        {
            MediaPlaylistUrl = "https://hls.example/video.m3u8",
            Segments = segments,
            SegmentStartOffsets = offsets,
            Encryption = encryption,
            KeyUri = keyUri,
            KeyIv = keyIv,
            MediaSequence = 0,
            TotalDuration = TimeSpan.FromSeconds(acc)
        };
    }

    /// <summary>一段以 TS 同步字开头的合法分片内容；按 index 使各分片内容互不相同，避免误触发重复指纹。</summary>
    private static byte[] MakeTsSample(int index)
    {
        var length = 1024 + index * 137;
        var bytes = new byte[length];
        bytes[0] = 0x47;
        bytes[1] = (byte)(index & 0xFF);
        return bytes;
    }

    /// <summary>按 HLS 的 AES-128-CBC + PKCS7 加密，用于构造测试输入。</summary>
    private static byte[] Encrypt(byte[] plain, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(plain, 0, plain.Length);
    }

    /// <summary>
    /// 全部分片正常时，应全部可取用、无缺失、值得保留。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_AllSegmentsFetched_WorthKeepingTrue()
    {
        var fs = new FakeFileSystem();
        var fetcher = new FakeMediaFetcher(fs);
        for (var i = 0; i < 5; i++)
        {
            fetcher.SeedFile($"https://hls.example/seg{i}.ts", MakeTsSample(i));
        }

        var downloader = new HlsSegmentDownloader(fetcher, new SegmentContentValidator(), fs);
        var report = await downloader.DownloadAsync(BuildPlan(5), new RequestContext(), "C:\\work", CancellationToken.None);

        Assert.Equal(5, report.FetchedCount);
        Assert.Empty(report.MissingIntervals);
        Assert.True(report.WorthKeeping);
        Assert.All(report.Outcomes, o => Assert.Equal(SegmentStatus.Fetched, o.Status));
        Assert.All(report.Outcomes, o => Assert.NotNull(o.FilePath));
    }

    /// <summary>
    /// 所有分片都是图片类型的占位响应：全部被校验跳过，整体不值得保留，缺失区间覆盖整段。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_ImagePlaceholders_AllSkipped()
    {
        var fs = new FakeFileSystem();
        var fetcher = new FakeMediaFetcher(fs);
        for (var i = 0; i < 5; i++)
        {
            fetcher.SeedFile($"https://hls.example/seg{i}.ts", MakeTsSample(56024), contentType: "image/jpeg");
        }

        var downloader = new HlsSegmentDownloader(fetcher, new SegmentContentValidator(), fs);
        var report = await downloader.DownloadAsync(BuildPlan(5), new RequestContext(), "C:\\work", CancellationToken.None);

        Assert.Equal(0, report.FetchedCount);
        Assert.False(report.WorthKeeping);
        Assert.Single(report.MissingIntervals);
        Assert.Equal(TimeSpan.Zero, report.MissingIntervals[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(50), report.MissingIntervals[0].End);
        Assert.All(report.Outcomes, o => Assert.Equal(SegmentStatus.SkippedByValidation, o.Status));
    }

    /// <summary>
    /// 多个分片返回逐字节相同的内容（视频类型绕过内容类型判定）：应判定为重复占位，仅首个可取用。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_DuplicateContent_MarkedDuplicate()
    {
        var fs = new FakeFileSystem();
        var fetcher = new FakeMediaFetcher(fs);
        fetcher.SeedFile("https://hls.example/seg0.ts", MakeTsSample(0));
        var decoy = MakeTsSample(56024); // 与 seg0 长度/首尾不同，但 seg1~4 彼此完全相同
        for (var i = 1; i < 5; i++)
        {
            fetcher.SeedFile($"https://hls.example/seg{i}.ts", decoy);
        }

        // 串行处理以确定性地验证「首个占位被取回、其余判重复」；并发竞态由 TryClaim 的原子性保证
        var downloader = new HlsSegmentDownloader(fetcher, new SegmentContentValidator(), fs, maxConcurrency: 1);
        var report = await downloader.DownloadAsync(BuildPlan(5), new RequestContext(), "C:\\work", CancellationToken.None);

        // seg0（真实）与 seg1（首个占位）被取回，seg2~4 与 seg1 内容重复被判为占位
        Assert.Equal(2, report.FetchedCount);
        Assert.Equal(SegmentStatus.Fetched, report.Outcomes[0].Status);
        Assert.Equal(SegmentStatus.Fetched, report.Outcomes[1].Status);
        Assert.All(report.Outcomes.Skip(2).Take(3), o => Assert.Equal(SegmentStatus.Duplicate, o.Status));
        Assert.Single(report.MissingIntervals);
        Assert.Equal(TimeSpan.FromSeconds(20), report.MissingIntervals[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(50), report.MissingIntervals[0].End);
    }

    /// <summary>
    /// 个别分片抓取失败时，应如实计入缺失且不污染其余分片。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_SingleFetchFailure_ReportedAsMissing()
    {
        var fs = new FakeFileSystem();
        var fetcher = new FakeMediaFetcher(fs);
        for (var i = 0; i < 5; i++)
        {
            if (i == 2)
            {
                fetcher.Fail($"https://hls.example/seg{i}.ts");
            }
            else
            {
                fetcher.SeedFile($"https://hls.example/seg{i}.ts", MakeTsSample(i));
            }
        }

        var downloader = new HlsSegmentDownloader(fetcher, new SegmentContentValidator(), fs);
        var report = await downloader.DownloadAsync(BuildPlan(5), new RequestContext(), "C:\\work", CancellationToken.None);

        Assert.Equal(4, report.FetchedCount);
        Assert.Equal(SegmentStatus.FetchFailed, report.Outcomes[2].Status);
        Assert.Single(report.MissingIntervals);
        Assert.Equal(TimeSpan.FromSeconds(20), report.MissingIntervals[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(30), report.MissingIntervals[0].End);
    }

    /// <summary>
    /// 间隔缺失应被合并为多个独立区间，而不是一长段。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_GappedFailures_MultipleIntervals()
    {
        var fs = new FakeFileSystem();
        var fetcher = new FakeMediaFetcher(fs);
        var failed = new HashSet<int> { 1, 3 };
        for (var i = 0; i < 5; i++)
        {
            if (failed.Contains(i))
            {
                fetcher.Fail($"https://hls.example/seg{i}.ts");
            }
            else
            {
                fetcher.SeedFile($"https://hls.example/seg{i}.ts", MakeTsSample(i));
            }
        }

        var downloader = new HlsSegmentDownloader(fetcher, new SegmentContentValidator(), fs);
        var report = await downloader.DownloadAsync(BuildPlan(5), new RequestContext(), "C:\\work", CancellationToken.None);

        Assert.Equal(2, report.MissingIntervals.Count);
        Assert.Equal(TimeSpan.FromSeconds(10), report.MissingIntervals[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(20), report.MissingIntervals[0].End);
        Assert.Equal(TimeSpan.FromSeconds(30), report.MissingIntervals[1].Start);
        Assert.Equal(TimeSpan.FromSeconds(40), report.MissingIntervals[1].End);
    }

    /// <summary>
    /// AES-128 流：密钥可取、逐片用各自 IV 解密，成品为合法 TS。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_Aes128_DecryptsToPlaintext()
    {
        var key = new byte[16];
        for (var i = 0; i < 16; i++)
        {
            key[i] = (byte)(i + 1);
        }

        var fs = new FakeFileSystem();
        var fetcher = new FakeMediaFetcher(fs);
        fetcher.SeedBytes("https://hls.example/key", key, "application/octet-stream");

        var plan = BuildPlan(3, M3u8Encryption.Aes128, keyUri: "https://hls.example/key");
        for (var i = 0; i < 3; i++)
        {
            var iv = Aes128Decryptor.BuildIv(i);
            var plain = MakeTsSample(2048);
            var cipher = Encrypt(plain, key, iv);
            fetcher.SeedFile($"https://hls.example/seg{i}.ts", cipher);
        }

        var downloader = new HlsSegmentDownloader(fetcher, new SegmentContentValidator(), fs);
        var report = await downloader.DownloadAsync(plan, new RequestContext(), "C:\\work", CancellationToken.None);

        Assert.Equal(3, report.FetchedCount);
        Assert.All(report.Outcomes, o =>
        {
            Assert.Equal(SegmentStatus.Fetched, o.Status);
            Assert.Equal(0x47, fs.ReadFile(o.FilePath!)[0]);
        });
    }

    /// <summary>
    /// 密钥与加密所用密钥不一致时，解密会失败（填充或同步字复检不过），分片被判废而非产出噪声。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_Aes128_WrongKey_MarksDecryptFailed()
    {
        var goodKey = new byte[16];
        goodKey[0] = 1;
        var badKey = new byte[16];
        badKey[0] = 2;

        var fs = new FakeFileSystem();
        var fetcher = new FakeMediaFetcher(fs);
        fetcher.SeedBytes("https://hls.example/key", badKey);

        var plan = BuildPlan(2, M3u8Encryption.Aes128, keyUri: "https://hls.example/key");
        for (var i = 0; i < 2; i++)
        {
            var iv = Aes128Decryptor.BuildIv(i);
            var plain = MakeTsSample(2048);
            var cipher = Encrypt(plain, goodKey, iv);
            fetcher.SeedFile($"https://hls.example/seg{i}.ts", cipher);
        }

        var downloader = new HlsSegmentDownloader(fetcher, new SegmentContentValidator(), fs);
        var report = await downloader.DownloadAsync(plan, new RequestContext(), "C:\\work", CancellationToken.None);

        Assert.Equal(0, report.FetchedCount);
        Assert.All(report.Outcomes, o => Assert.Equal(SegmentStatus.DecryptFailed, o.Status));
    }

    /// <summary>
    /// 密钥无法获取属于致命错误，整批应回退而非硬拼。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_Aes128_KeyFetchFails_FatalError()
    {
        var fs = new FakeFileSystem();
        var fetcher = new FakeMediaFetcher(fs);
        fetcher.Fail("https://hls.example/key");

        var plan = BuildPlan(2, M3u8Encryption.Aes128, keyUri: "https://hls.example/key");
        var downloader = new HlsSegmentDownloader(fetcher, new SegmentContentValidator(), fs);
        var report = await downloader.DownloadAsync(plan, new RequestContext(), "C:\\work", CancellationToken.None);

        Assert.NotNull(report.FatalError);
        Assert.Equal(0, report.FetchedCount);
        Assert.False(report.WorthKeeping);
    }
}
