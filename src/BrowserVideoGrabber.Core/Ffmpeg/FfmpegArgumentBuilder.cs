/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Ffmpeg
*文件名： FfmpegArgumentBuilder
*版本号： V1.0.0.0
*唯一标识：591eed08-3176-479f-904c-b5bcd031697c
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:58:00
*描述：ffmpeg 命令行参数构建器，把下载任务转换为安全的参数列表。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:58:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Globalization;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Core.Ffmpeg;

/// <summary>
/// ffmpeg 命令行参数构建器。
/// </summary>
/// <remarks>
/// <para>
/// 本类<b>只负责构建参数列表，绝不负责拼接命令行字符串</b>。
/// 原因是：含空格的输出路径、含 CRLF 的请求头块，一旦拼成字符串就需要处理引号与转义，
/// 而不同平台的转义规则并不一致（Windows 用双引号、POSIX 用反斜杠）。
/// 交给 <c>ProcessStartInfo.ArgumentList</c> 逐参数传递可以彻底绕开这些坑。
/// </para>
/// <para>
/// 参数顺序遵循 ffmpeg 的语义要求：<b>输入选项必须在 <c>-i</c> 之前，输出选项必须在 <c>-i</c> 之后</b>，
/// 而输出文件名必须是最后一个参数。
/// </para>
/// </remarks>
public static class FfmpegArgumentBuilder
{
    /// <summary>
    /// 需要追加流媒体兼容参数的格式集合。
    /// </summary>
    /// <remarks>
    /// MP4 整文件是单次 HTTP 请求直取，不需要协议白名单；
    /// 而 HLS / DASH 类格式必须放开协议与扩展名限制，否则 ffmpeg 会拒绝加载清单。
    /// </remarks>
    private static readonly HashSet<VideoFormat> StreamFormats = new()
    {
        VideoFormat.M3u8,
        VideoFormat.Ts,
        VideoFormat.M4s,
        VideoFormat.Mpd
    };

    /// <summary>
    /// 为指定下载任务构建 ffmpeg 参数列表。
    /// </summary>
    /// <param name="task">下载任务，提供输入地址、格式、输出路径与请求上下文。</param>
    /// <param name="options">调用配置。为空时使用默认配置。</param>
    /// <returns>可直接交给进程运行器的参数列表（不含可执行文件本身）。</returns>
    public static IReadOnlyList<string> Build(DownloadTask task, FfmpegOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(task);

        options ??= new FfmpegOptions();
        var arguments = new List<string>();

        // ---------- 全局选项 ----------
        if (options.Overwrite)
        {
            arguments.Add("-y");
        }

        if (options.EnableProgressPipe)
        {
            // -nostats 关掉默认状态行，避免它与 -progress 的键值输出交错，破坏按行解析
            arguments.Add("-nostats");
            arguments.Add("-progress");
            arguments.Add("pipe:2");
        }

        // ---------- 输入选项 ----------
        // 请求头块必须以 CRLF 分隔并整体作为一个参数；空头块绝不能传入，否则 ffmpeg 会报参数错误
        var headerBlock = task.Context.BuildHeaderBlock();
        if (!string.IsNullOrEmpty(headerBlock))
        {
            arguments.Add("-headers");
            arguments.Add(headerBlock);
        }

        if (StreamFormats.Contains(task.Format))
        {
            // -reconnect 与 -reconnect_streamed 是 0/1 开关：1 表示允许 ffmpeg 在断流后自行重连
            arguments.Add("-reconnect");
            arguments.Add(options.Reconnect ? "1" : "0");

            arguments.Add("-reconnect_streamed");
            arguments.Add(options.ReconnectStreamed ? "1" : "0");

            arguments.Add("-reconnect_delay_max");
            arguments.Add(options.ReconnectDelayMaxSeconds.ToString(CultureInfo.InvariantCulture));

            arguments.Add("-protocol_whitelist");
            arguments.Add(options.ProtocolWhitelist);

            arguments.Add("-allowed_extensions");
            arguments.Add(options.AllowedExtensions);
        }

        // ---------- 输入源 ----------
        arguments.Add("-i");
        arguments.Add(task.Url);

        // ---------- 输出选项 ----------
        if (options.CopyCodec)
        {
            arguments.Add("-c");
            arguments.Add("copy");
        }
        else
        {
            arguments.Add("-c:v");
            arguments.Add("libx264");
            arguments.Add("-c:a");
            arguments.Add("aac");
        }

        // ---------- 输出文件（必须是最后一个参数） ----------
        arguments.Add(string.IsNullOrWhiteSpace(task.OutputPath) ? "output.mp4" : task.OutputPath);

        return arguments;
    }
}
