/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Downloads
*文件名： DownloadTaskEventArgs
*版本号： V1.0.0.0
*唯一标识：02040218-16a7-4af6-9424-d10df873a6a7
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 23:02:00
*描述：下载任务状态变更事件参数。
*
*=================================================
*修改标记
*修改时间：2026/9/12 23:02:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Core.Downloads;

/// <summary>
/// 下载任务状态变更事件参数。
/// </summary>
/// <remarks>
/// 界面层订阅该事件来刷新「待下载 / 正在下载 / 已下载」三个页签。
/// 事件可能在后台线程触发，订阅方必须自行切换到 UI 线程后再操作控件。
/// </remarks>
public sealed class DownloadTaskEventArgs : EventArgs
{
    /// <summary>发生状态变更的任务。其 <see cref="DownloadTask.Status"/> 已是新状态。</summary>
    public required DownloadTask Task { get; init; }

    /// <summary>变更前的状态，便于订阅方判断是否需要特殊处理（例如从运行中变为失败）。</summary>
    public DownloadStatus PreviousStatus { get; init; }
}
