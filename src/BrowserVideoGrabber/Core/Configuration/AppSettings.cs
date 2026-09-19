/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Configuration
*文件名： AppSettings
*版本号： V1.0.0.0
*唯一标识：2a887c9d-3f1c-4a18-bab0-27a36ccee844
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 23:12:00
*描述：应用设置模型，承载用户可配置项并在重启后持久化。
*
*=================================================
*修改标记
*修改时间：2026/9/12 23:12:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

namespace BrowserVideoGrabber.Core.Configuration;

/// <summary>
/// 应用设置。
/// </summary>
/// <remarks>
/// 该模型同时被界面层（读写）、基础设施层（持久化）与下载器（读取并发数）使用，
/// 因此放在 Core 中而非界面层，避免基础设施层反向依赖界面层。
/// </remarks>
public sealed class AppSettings
{
    /// <summary>
    /// ffmpeg 可执行文件路径。
    /// </summary>
    /// <remarks>
    /// 为空时由定位器按「应用目录 → PATH → 常见安装路径」的顺序自动探测。
    /// </remarks>
    public string? FfmpegPath { get; set; }

    /// <summary>下载输出目录。为空时使用系统「下载」目录。</summary>
    public string? OutputDirectory { get; set; }

    /// <summary>同时下载的任务数上限。默认 3。</summary>
    public int MaxConcurrency { get; set; } = 3;

    /// <summary>MP4 多线程下载的分片数。默认 4，设为 1 表示单线程。</summary>
    public int HttpSegmentCount { get; set; } = 4;

    /// <summary>下载时使用的 User-Agent。为空时使用内嵌浏览器当前 UA。</summary>
    public string? UserAgent { get; set; }

    /// <summary>上次访问的地址，用于启动时恢复。</summary>
    public string? LastUrl { get; set; }

    /// <summary>关闭时是否记住已打开的标签页，下次启动原样恢复。默认 true。</summary>
    public bool RestoreTabs { get; set; } = true;

    /// <summary>历史记录容量上限（超出时环形淘汰最旧）。默认 500。</summary>
    public int MaxHistoryEntries { get; set; } = 500;

    /// <summary>同时打开的浏览器标签页上限。默认 10。</summary>
    public int MaxTabs { get; set; } = 10;

    /// <summary>
    /// 创建当前设置的深拷贝。
    /// </summary>
    /// <returns>字段值一致的新实例。</returns>
    public AppSettings Clone()
        => new()
        {
            FfmpegPath = FfmpegPath,
            OutputDirectory = OutputDirectory,
            MaxConcurrency = MaxConcurrency,
            HttpSegmentCount = HttpSegmentCount,
            UserAgent = UserAgent,
            LastUrl = LastUrl,
            RestoreTabs = RestoreTabs,
            MaxHistoryEntries = MaxHistoryEntries,
            MaxTabs = MaxTabs
        };
}
