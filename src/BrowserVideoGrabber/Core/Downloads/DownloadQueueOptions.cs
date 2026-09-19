/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Downloads
*文件名： DownloadQueueOptions
*版本号： V1.0.0.0
*唯一标识：6f2f7c96-9002-4e4b-99c0-25a353fc613d
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 23:04:00
*描述：下载队列运行配置。
*
*=================================================
*修改标记
*修改时间：2026/9/12 23:04:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Common;

namespace BrowserVideoGrabber.Core.Downloads;

/// <summary>
/// 下载队列运行配置。
/// </summary>
public sealed class DownloadQueueOptions
{
    /// <summary>
    /// 并发下载数上限。默认 3。
    /// </summary>
    /// <remarks>
    /// 不宜设置过大：多数流媒体站点会按 IP 或会话限流，
    /// 并发过高反而会触发 403/429，导致整体成功率下降。
    /// </remarks>
    public int MaxConcurrency { get; set; } = 3;

    /// <summary>
    /// 调度轮询间隔。默认 20 毫秒。
    /// </summary>
    /// <remarks>
    /// 采用轮询而非信号量唤醒，是为了让「新任务入队、任务重试回到待下载、暂停恢复」
    /// 三种来源共用同一条调度路径，逻辑更简单且不会出现漏唤醒。
    /// 代价是空转时每 20 毫秒一次轻量检查，开销可忽略。
    /// </remarks>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(20);

    /// <summary>失败重试策略，用于计算重试前的退避等待时长。</summary>
    public RetryPolicy RetryPolicy { get; set; } = new();
}
