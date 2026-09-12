/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Abstractions
*文件名： IDownloadHandler
*版本号： V1.0.0.0
*唯一标识：33d3ea6e-a60b-4626-a08a-d54587911ad2
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:22:00
*描述：下载处理器抽象接口，定义按格式分发的下载策略契约。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:22:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Core.Abstractions;

/// <summary>
/// 下载处理器抽象：一种处理器负责一类（或多类）资源格式的下载。
/// </summary>
/// <remarks>
/// <para>
/// 当前有两个实现：
/// <list type="bullet">
///   <item><description><c>HttpDownloadHandler</c>：处理 MP4 整文件，多线程分片 + 断点续传。</description></item>
///   <item><description><c>FfmpegDownloadHandler</c>：处理 m3u8 / ts / m4s / mpd，交由 ffmpeg 拉取与合并。</description></item>
/// </list>
/// 新增格式只需新增一个实现并注册到 <c>DownloadHandlerFactory</c>，核心调度代码零改动。
/// </para>
/// <para>
/// 实现方必须遵守两条约定：
/// 1）不得改写 <see cref="DownloadTask.Status"/>，状态一律由 <c>DownloadQueue</c> 驱动；
/// 2）不得吞掉 <see cref="OperationCanceledException"/>，取消必须向上传播。
/// </para>
/// </remarks>
public interface IDownloadHandler
{
    /// <summary>
    /// 判断本处理器是否能处理该任务。
    /// </summary>
    /// <param name="task">待处理任务。</param>
    /// <returns>能处理返回 true。</returns>
    /// <remarks>实现必须是纯函数，不得产生任何副作用或阻塞。</remarks>
    bool CanHandle(DownloadTask task);

    /// <summary>
    /// 执行下载。
    /// </summary>
    /// <param name="task">下载任务，包含地址、格式、请求上下文与输出路径。</param>
    /// <param name="progress">进度上报通道。实现方应尽量高频上报，界面侧会做节流。</param>
    /// <param name="cancellationToken">取消令牌。取消时应彻底终止底层工作（含子进程树）并清理半成品。</param>
    /// <returns>本次尝试的结果。</returns>
    Task<DownloadResult> DownloadAsync(
        DownloadTask task,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken);
}
