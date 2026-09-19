/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.App.Panes
*文件名： DownloadAction
*版本号： V1.0.0.0
*唯一标识：4d7b1e93-6c2a-4f85-9e37-8b1c5d2a7f64
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 01:40:00
*描述：下载列表的操作意图定义，由面板上抛给主窗体执行。
*
*=================================================
*修改标记
*修改时间：2026/9/13 01:40:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.App.Panes;

/// <summary>
/// 用户在下载列表中可发起的操作。
/// </summary>
/// <remarks>
/// 之所以用「意图枚举 + 单一事件」而不是为每种操作各建一个事件：
/// 操作项会随版本增加，用枚举时新增一项只需在开关里加一个分支，
/// 而事件方式会导致订阅代码与事件声明同步膨胀。
/// </remarks>
public enum DownloadAction
{
    /// <summary>暂停正在进行的任务。</summary>
    Pause,

    /// <summary>恢复已暂停的任务。</summary>
    Resume,

    /// <summary>取消任务。</summary>
    Cancel,

    /// <summary>重新尝试已失败或已取消的任务。</summary>
    Retry,

    /// <summary>从列表中移除任务（仅限终态）。</summary>
    Remove,

    /// <summary>打开已下载的文件。</summary>
    OpenFile,

    /// <summary>在资源管理器中定位已下载的文件。</summary>
    OpenFolder,

    /// <summary>复制下载地址到剪贴板。</summary>
    CopyUrl,

    /// <summary>清空全部终态任务。</summary>
    ClearFinished
}

/// <summary>
/// 下载列表操作请求的事件参数。
/// </summary>
public sealed class DownloadActionEventArgs : EventArgs
{
    /// <summary>发起操作时选中的任务。<see cref="DownloadAction.ClearFinished"/> 时为 null。</summary>
    public DownloadTask? Task { get; init; }

    /// <summary>操作意图。</summary>
    public required DownloadAction Action { get; init; }
}
