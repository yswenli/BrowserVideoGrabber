/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Models
*文件名： DownloadStatus
*版本号： V1.0.0.0
*唯一标识：fc528411-c8b0-4b26-918e-378e12f80151
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:15:00
*描述：下载任务状态枚举，定义任务的完整生命周期与状态流转规则。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:15:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

namespace BrowserVideoGrabber.Core.Models;

/// <summary>
/// 下载任务状态。界面右下角三个页签即按此状态分组。
/// </summary>
/// <remarks>
/// 允许的状态流转（由 <c>DownloadQueue</c> 统一驱动，其它位置不得直接改写状态）：
/// <code>
/// Pending ──▶ Running ──▶ Completed（终态）
///    ▲           │
///    │           ├──▶ Failed（终态，或重试时回到 Pending）
///    │           ├──▶ Paused ──▶ Pending（恢复）
///    └───────────┴──▶ Canceled（终态）
/// </code>
/// </remarks>
public enum DownloadStatus
{
    /// <summary>待下载：已入队但尚未占用下载槽位。</summary>
    Pending = 0,

    /// <summary>正在下载：已占用并发槽位，处理器正在执行。</summary>
    Running = 1,

    /// <summary>已暂停：用户主动暂停，或被恢复为待下载状态前的中间态。</summary>
    Paused = 2,

    /// <summary>下载完成（终态）：输出文件已落盘。</summary>
    Completed = 3,

    /// <summary>下载失败（终态）：已耗尽重试次数，或遇到不可重试错误（如 DRM 保护）。</summary>
    Failed = 4,

    /// <summary>已取消（终态）：用户主动取消。</summary>
    Canceled = 5
}
