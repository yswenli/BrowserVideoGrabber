/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Ffmpeg
*文件名： FfmpegArgumentBuilderTests
*版本号： V1.0.0.0
*唯一标识：73225dca-abae-402c-8828-e215264ec7bc
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:38:00
*描述：FfmpegArgumentBuilder 的单元测试，覆盖参数顺序、请求头注入与进度开关。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:38:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Ffmpeg;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Tests.Ffmpeg;

/// <summary>
/// <see cref="FfmpegArgumentBuilder"/> 的行为验证。
/// </summary>
public sealed class FfmpegArgumentBuilderTests
{
    /// <summary>
    /// 基础参数必须包含输入地址、流拷贝、覆盖开关，且输出路径位于参数列表末尾。
    /// </summary>
    [Fact]
    public void Build_ShouldContainInputCodecCopyAndOutputPath()
    {
        var task = CreateTask(VideoFormat.M3u8, "https://a.com/hls/index.m3u8", @"D:\out\movie.mp4");

        var args = FfmpegArgumentBuilder.Build(task);

        Assert.Contains("-i", args);
        Assert.Equal("https://a.com/hls/index.m3u8", GetValueAfter(args, "-i"));
        Assert.Equal("copy", GetValueAfter(args, "-c"));
        Assert.Contains("-y", args);

        // 输出路径必须是最后一个参数，否则 ffmpeg 会把后续开关当成输出文件名
        Assert.Equal(@"D:\out\movie.mp4", args[^1]);
    }

    /// <summary>
    /// 请求上下文必须被转换成 -headers 参数，且涵盖 Referer / User-Agent / Cookie 三项反爬关键头。
    /// </summary>
    [Fact]
    public void Build_ShouldInjectRequestHeaders()
    {
        var task = CreateTask(VideoFormat.M3u8, "https://a.com/hls/index.m3u8", @"D:\out\movie.mp4");
        task.Context.Referer = "https://a.com/watch";
        task.Context.UserAgent = "Mozilla/5.0 Test";
        task.Context.Cookie = "sid=abc";

        var args = FfmpegArgumentBuilder.Build(task);

        var headerBlock = GetValueAfter(args, "-headers");

        Assert.NotNull(headerBlock);
        Assert.Contains("Referer: https://a.com/watch", headerBlock);
        Assert.Contains("User-Agent: Mozilla/5.0 Test", headerBlock);
        Assert.Contains("Cookie: sid=abc", headerBlock);
    }

    /// <summary>
    /// 多个请求头必须以 CRLF 分隔并整体作为「一个」进程参数传入。
    /// 若被拆成多个参数，ffmpeg 会把第二个头当成输入文件，直接报错。
    /// </summary>
    [Fact]
    public void Build_ShouldPassHeaderBlockAsSingleArgument()
    {
        var task = CreateTask(VideoFormat.M3u8, "https://a.com/hls/index.m3u8", @"D:\out\movie.mp4");
        task.Context.Referer = "https://a.com/watch";
        task.Context.UserAgent = "Mozilla/5.0 Test";

        var args = FfmpegArgumentBuilder.Build(task);

        var headerBlock = GetValueAfter(args, "-headers");

        Assert.NotNull(headerBlock);
        Assert.Contains("\r\n", headerBlock, StringComparison.Ordinal);

        // 整个参数列表里只能出现一次 -headers，且只有一个参数包含 Referer
        Assert.Single(args, a => a == "-headers");
        Assert.Single(args, a => a.Contains("Referer:", StringComparison.Ordinal));
    }

    /// <summary>
    /// 无请求上下文时不应产生空的 -headers 参数，
    /// 否则 ffmpeg 会收到一个空的头块并可能报错。
    /// </summary>
    [Fact]
    public void Build_ShouldOmitHeaders_WhenContextIsEmpty()
    {
        var task = CreateTask(VideoFormat.Mp4, "https://a.com/v.mp4", @"D:\out\v.mp4");

        var args = FfmpegArgumentBuilder.Build(task);

        Assert.DoesNotContain("-headers", args);
    }

    /// <summary>
    /// 分片类格式必须附加协议白名单与扩展名放行参数，
    /// 否则 ffmpeg 会因安全策略拒绝加载分片清单。
    /// </summary>
    /// <param name="format">待验证的格式。</param>
    [Theory]
    [InlineData(VideoFormat.M3u8)]
    [InlineData(VideoFormat.Ts)]
    [InlineData(VideoFormat.M4s)]
    [InlineData(VideoFormat.Mpd)]
    public void Build_ShouldAddPlaylistCompatibilitySwitches(VideoFormat format)
    {
        var task = CreateTask(format, "https://a.com/x/index.m3u8", @"D:\out\movie.mp4");

        var args = FfmpegArgumentBuilder.Build(task);

        Assert.Contains("-protocol_whitelist", args);
        Assert.Contains("-allowed_extensions", args);
    }

    /// <summary>
    /// MP4 整文件是单请求直取，不需要 HLS 的白名单参数。
    /// </summary>
    [Fact]
    public void Build_ShouldNotAddPlaylistSwitches_ForMp4()
    {
        var task = CreateTask(VideoFormat.Mp4, "https://a.com/v.mp4", @"D:\out\v.mp4");

        var args = FfmpegArgumentBuilder.Build(task);

        Assert.DoesNotContain("-protocol_whitelist", args);
    }

    /// <summary>
    /// 含空格的输出路径必须原样作为单个参数，不得被引号包裹或拆开。
    /// </summary>
    [Fact]
    public void Build_ShouldPassOutputPathWithSpacesUnchanged()
    {
        const string outputPath = @"D:\My Videos\2026\电视剧 第一集.mp4";
        var task = CreateTask(VideoFormat.M3u8, "https://a.com/hls/index.m3u8", outputPath);

        var args = FfmpegArgumentBuilder.Build(task);

        Assert.Equal(outputPath, args[^1]);
        // 输出路径只应作为最后一个参数出现一次，避免被误当成输入
        Assert.Single(args, a => a == outputPath);
    }

    /// <summary>
    /// 必须开启机器可读的进度输出，进度解析器才能稳定按行读取。
    /// </summary>
    [Fact]
    public void Build_ShouldEnableMachineReadableProgress()
    {
        var task = CreateTask(VideoFormat.M3u8, "https://a.com/hls/index.m3u8", @"D:\out\movie.mp4");

        var args = FfmpegArgumentBuilder.Build(task);

        Assert.Equal("pipe:2", GetValueAfter(args, "-progress"));

        // -nostats 关闭默认的状态行，避免它与 -progress 的输出混在一起干扰按行解析
        Assert.Contains("-nostats", args);
    }

    /// <summary>
    /// 网络重连参数必须存在，用于应对 CDN 抖动导致的临时中断。
    /// </summary>
    [Fact]
    public void Build_ShouldEnableReconnectForNetworkStreams()
    {
        var task = CreateTask(VideoFormat.M3u8, "https://a.com/hls/index.m3u8", @"D:\out\movie.mp4");

        var args = FfmpegArgumentBuilder.Build(task);

        Assert.Equal("1", GetValueAfter(args, "-reconnect"));
        Assert.Equal("1", GetValueAfter(args, "-reconnect_streamed"));
        Assert.Contains("-reconnect_delay_max", args);
    }

    /// <summary>
    /// 自定义输出文件已存在时若关闭覆盖开关，则不应出现 -y。
    /// </summary>
    [Fact]
    public void Build_ShouldOmitOverwriteSwitch_WhenDisabled()
    {
        var task = CreateTask(VideoFormat.Mp4, "https://a.com/v.mp4", @"D:\out\v.mp4");

        var args = FfmpegArgumentBuilder.Build(task, new FfmpegOptions { Overwrite = false });

        Assert.DoesNotContain("-y", args);
    }

    /// <summary>
    /// 关闭流拷贝时应使用重编码开关，而不是 -c copy。
    /// </summary>
    [Fact]
    public void Build_ShouldUseReencode_WhenCopyCodecDisabled()
    {
        var task = CreateTask(VideoFormat.Mp4, "https://a.com/v.mp4", @"D:\out\v.mp4");

        var args = FfmpegArgumentBuilder.Build(task, new FfmpegOptions { CopyCodec = false });

        Assert.DoesNotContain("copy", args);
        Assert.Contains("-c:v", args);
        Assert.Contains("-c:a", args);
    }

    /// <summary>
    /// 分片类格式必须关闭 ffmpeg 的扩展名挑剔开关。
    /// </summary>
    /// <remarks>
    /// HLS 解复用器对分片扩展名有一份硬编码白名单，<b>且不受 <c>-allowed_extensions</c> 影响</b>。
    /// 真实站点把 MPEG-TS 分片命名成 <c>.jpeg</c> 属于常见做法，此时 ffmpeg 会直接以
    /// 「Invalid data found when processing input」拒绝加载。唯一可行的开关就是
    /// <c>-extension_picky 0</c>，且它是输入选项，必须出现在 <c>-i</c> 之前。
    /// </remarks>
    [Fact]
    public void Build_ShouldDisableExtensionPicky_ForStreamFormats()
    {
        var task = CreateTask(VideoFormat.M3u8, "https://a.com/hls/index.m3u8", @"D:\out\movie.mp4");

        var args = FfmpegArgumentBuilder.Build(task);

        Assert.Equal("0", GetValueAfter(args, "-extension_picky"));

        // 必须在 -i 之前，否则 ffmpeg 会把它当作输出选项而忽略
        Assert.True(IndexOf(args, "-extension_picky") < IndexOf(args, "-i"));
    }

    /// <summary>
    /// MP4 整文件不涉及分片扩展名白名单，无需该开关。
    /// </summary>
    [Fact]
    public void Build_ShouldOmitExtensionPicky_ForMp4()
    {
        var task = CreateTask(VideoFormat.Mp4, "https://a.com/v.mp4", @"D:\out\v.mp4");

        var args = FfmpegArgumentBuilder.Build(task);

        Assert.DoesNotContain("-extension_picky", args);
    }

    /// <summary>
    /// 显式关闭该选项时不应附加参数，供 ffmpeg 版本低于 7.1 的环境回退。
    /// </summary>
    [Fact]
    public void Build_ShouldOmitExtensionPicky_WhenDisabled()
    {
        var task = CreateTask(VideoFormat.M3u8, "https://a.com/hls/index.m3u8", @"D:\out\movie.mp4");

        var args = FfmpegArgumentBuilder.Build(task, new FfmpegOptions { DisableExtensionPicky = false });

        Assert.DoesNotContain("-extension_picky", args);
    }

    /// <summary>
    /// 本地重封装模式：只处理磁盘上的文件，不得出现任何网络相关开关。
    /// </summary>
    /// <remarks>
    /// 这是「C# 取分片 + ffmpeg 只做合并」架构的落点。若这里混入了
    /// <c>-headers</c> / <c>-protocol_whitelist</c> 等网络开关，说明职责边界被破坏，
    /// 本次改造的核心收益（可逐片校验、可解密、不受 CDN 反爬影响）会随之失效。
    /// </remarks>
    [Fact]
    public void BuildLocalRemux_ShouldProduceLocalOnlyArguments()
    {
        const string input = @"D:\tmp\assembled.ts";
        const string output = @"D:\My Videos\电影 第一集.mp4";

        var args = FfmpegArgumentBuilder.BuildLocalRemux(input, output);

        Assert.Equal(input, GetValueAfter(args, "-i"));
        Assert.Equal("copy", GetValueAfter(args, "-c"));
        Assert.Equal(output, args[^1]);

        Assert.Contains("-y", args);
        Assert.DoesNotContain("-headers", args);
        Assert.DoesNotContain("-protocol_whitelist", args);
        Assert.DoesNotContain("-allowed_extensions", args);
        Assert.DoesNotContain("-reconnect", args);
    }

    /// <summary>
    /// 合并产出的 mp4 应把索引前移，浏览器与流式播放器才能边下边播。
    /// </summary>
    [Fact]
    public void BuildLocalRemux_ShouldEnableFastStart()
    {
        var args = FfmpegArgumentBuilder.BuildLocalRemux(@"D:\tmp\assembled.ts", @"D:\out\movie.mp4");

        Assert.Equal("+faststart", GetValueAfter(args, "-movflags"));
    }

    /// <summary>
    /// 关闭 faststart 时不应出现该开关。
    /// </summary>
    [Fact]
    public void BuildLocalRemux_ShouldOmitFastStart_WhenDisabled()
    {
        var args = FfmpegArgumentBuilder.BuildLocalRemux(
            @"D:\tmp\assembled.ts",
            @"D:\out\movie.mp4",
            new FfmpegOptions { EnableFastStart = false });

        Assert.DoesNotContain("-movflags", args);
    }

    /// <summary>
    /// 构造一个测试用下载任务。
    /// </summary>
    /// <param name="format">资源格式。</param>
    /// <param name="url">资源地址。</param>
    /// <param name="outputPath">输出路径。</param>
    /// <returns>下载任务实例。</returns>
    private static DownloadTask CreateTask(VideoFormat format, string url, string outputPath)
        => new()
        {
            Url = url,
            Format = format,
            OutputPath = outputPath,
            Title = "movie"
        };

    /// <summary>
    /// 获取参数列表中指定开关紧随其后的取值。
    /// </summary>
    /// <param name="args">参数列表。</param>
    /// <param name="switchName">开关名。</param>
    /// <returns>紧随其后的取值；未找到时返回 null。</returns>
    private static string? GetValueAfter(IReadOnlyList<string> args, string switchName)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], switchName, StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    /// <summary>
    /// 取得开关在参数列表中的下标，用于校验选项的先后顺序。
    /// </summary>
    /// <param name="args">参数列表。</param>
    /// <param name="switchName">开关名。</param>
    /// <returns>下标；未找到时返回 -1。</returns>
    private static int IndexOf(IReadOnlyList<string> args, string switchName)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], switchName, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
