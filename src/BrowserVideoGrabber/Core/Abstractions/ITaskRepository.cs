/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Abstractions
*文件名： ITaskRepository
*版本号： V1.0.0.0
*唯一标识：0c6999fc-2c29-456c-bd14-44f2494adf16
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:25:00
*描述：下载任务持久化抽象，支持程序重启后恢复任务列表。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:25:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Core.Abstractions;

/// <summary>
/// 下载任务持久化抽象。
/// </summary>
/// <remarks>
/// 持久化的目的是「重启不丢列表」：用户可以关掉程序、第二天再继续。
/// 实现方需保证写入的原子性（先写临时文件再替换），
/// 避免程序在写入过程中被杀死导致任务文件损坏、整个列表丢失。
/// </remarks>
public interface ITaskRepository
{
    /// <summary>
    /// 载入历史任务列表。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>任务列表。文件不存在或内容损坏时返回空列表，不抛异常。</returns>
    Task<IReadOnlyList<DownloadTask>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存任务列表（全量覆盖）。
    /// </summary>
    /// <param name="tasks">待保存的任务集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步写入的任务。</returns>
    /// <remarks>
    /// 处于正在下载状态的任务在保存前会被降级为待下载：
    /// 下次启动时进程已不存在，继续标记为「正在下载」会造成界面永久卡在运行态。
    /// </remarks>
    Task SaveAsync(IReadOnlyCollection<DownloadTask> tasks, CancellationToken cancellationToken = default);
}
