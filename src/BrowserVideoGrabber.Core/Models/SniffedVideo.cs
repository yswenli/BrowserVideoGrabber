/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Models
*文件名： SniffedVideo
*版本号： V1.0.0.0
*唯一标识：741735af-7d87-465d-9418-e1de945f7a5a
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:13:00
*描述：嗅探结果项模型，表示页面上被发现的一个可下载视频资源。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:13:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

namespace BrowserVideoGrabber.Core.Models;

/// <summary>
/// 嗅探结果项：表示页面上被发现的一个可下载视频资源。
/// </summary>
/// <remarks>
/// 由三条嗅探链路（网络响应监听 / URL 特征匹配 / JS 注入 Hook）统一产出，
/// 经 <c>VideoUrlMatcher</c> 归一化去重后进入界面列表。
/// 本类型为不可变对象，界面列表通过 <see cref="Id"/> 定位条目。
/// </remarks>
public sealed class SniffedVideo
{
    /// <summary>嗅探项唯一标识，用于界面列表定位与增量刷新。</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>资源原始地址。下载时直接使用该地址，不做任何改写，避免破坏动态签名参数。</summary>
    public required string Url { get; init; }

    /// <summary>归一化后的地址，仅用于界面展示与去重比较，不参与实际请求。</summary>
    public string NormalizedUrl { get; init; } = string.Empty;

    /// <summary>资源格式，决定后续由哪个下载处理器处理。</summary>
    public required VideoFormat Format { get; init; }

    /// <summary>HTTP 响应头中的 Content-Type。仅网络响应链路嗅探到的资源才有值。</summary>
    public string? ContentType { get; init; }

    /// <summary>分辨率（形如 1920x1080）。从主播放列表的 #EXT-X-STREAM-INF 解析得到，可能为空。</summary>
    public string? Resolution { get; init; }

    /// <summary>码率（单位 bit/s）。从主播放列表的 #EXT-X-STREAM-INF 解析得到，可能为空。</summary>
    public long? Bandwidth { get; init; }

    /// <summary>嗅探来源标记，便于排查漏抓问题。取值：network（响应监听）/ url（特征匹配）/ jshook（脚本劫持）。</summary>
    public string Source { get; init; } = "url";

    /// <summary>发现时间。</summary>
    public DateTimeOffset DetectedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>
    /// 展示用短标题：取归一化地址的最后一段路径，路径为空时回退为完整地址。
    /// </summary>
    public string DisplayTitle
    {
        get
        {
            // 归一化地址可能为空（构造时未赋值），此时直接回退到原始地址
            var source = string.IsNullOrWhiteSpace(NormalizedUrl) ? Url : NormalizedUrl;
            if (string.IsNullOrWhiteSpace(source))
            {
                return string.Empty;
            }

            var slashIndex = source.LastIndexOf('/');
            var tail = slashIndex >= 0 && slashIndex < source.Length - 1
                ? source[(slashIndex + 1)..]
                : source;

            return string.IsNullOrWhiteSpace(tail) ? source : tail;
        }
    }
}
