/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Downloads
*文件名： DownloadProgressEventArgs
*版本号： V1.0.0.0
*唯一标识：42c52c94-ceb1-40db-a856-74f96736ad80
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 23:03:00
*描述：下载进度变更事件参数。
*
*=================================================
*修改标记
*修改时间：2026/9/12 23:03:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Core.Downloads;

/// <summary>
/// 下载进度变更事件参数。
/// </summary>
/// <remarks>
/// <para>
/// 进度事件的触发频率取决于底层实现（ffmpeg 通常每 0.5 秒一行，原生下载器可能更高频），
/// 因此界面层<b>必须做节流刷新</b>，否则高频重绘会导致界面明显卡顿。
/// </para>
/// <para>
/// 队列内部使用同步进度回调转发，因此同一任务的事件严格按发生顺序触发、进度单调不减，
/// 界面无需再做乱序保护。
/// </para>
/// </remarks>
public sealed class DownloadProgressEventArgs : EventArgs
{
    /// <summary>产生进度的任务。</summary>
    public required DownloadTask Task { get; init; }

    /// <summary>最新进度快照。</summary>
    public required DownloadProgress Progress { get; init; }
}
