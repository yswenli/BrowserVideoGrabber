/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Downloads
*文件名： ExitGuard
*版本号： V1.0.0.0
*唯一标识：6f9c2b1e-4a7d-4c3e-9b8a-1d2e3f4a5b6c
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/19 22:33:00
*描述：退出时的活动下载判定，把「是否有任务仍在跑」收口成一个可单测的纯函数。
*
*=================================================
*修改标记
*修改时间：2026/9/19 22:33:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Linq;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Core.Downloads;

/// <summary>
/// 退出守卫：判定是否存在仍在进行的下载任务。
/// </summary>
/// <remarks>
/// 把这段判定从主窗体里抽出来，原因有二：
/// <list type="bullet">
/// <item>主窗体属于 App 层（依赖 WinForms），项目约定不对其做单元测试；而「是否该弹退出确认」是纯逻辑，值得被单测固化。</item>
/// <item>收口后，托盘「退出」与未来任何退出入口都走同一判定，不会出现两处各写一遍状态枚举导致语义漂移。</item>
/// </list>
/// </remarks>
public static class ExitGuard
{
    /// <summary>
    /// 是否存在仍在进行的下载任务。
    /// </summary>
    /// <param name="statuses">全部任务的状态集合。</param>
    /// <returns>
    /// 含 <see cref="DownloadStatus.Running"/>（正在下载）或 <see cref="DownloadStatus.Pending"/>（已入队等待槽位）时返回 <see langword="true"/>；
    /// 其余状态（已完成 / 失败 / 已取消 / 已暂停）以及空集合均返回 <see langword="false"/>。
    /// </returns>
    /// <remarks>
    /// <b>为什么只认 Running / Pending</b>：这两种状态代表「进程退出后任务会真正中断、需要用户知情」。
    /// 暂停（Paused）是用户主动的中间态，退出后留作暂停不动即可；终态任务不会因退出而受到额外影响。
    /// </remarks>
    public static bool HasActiveDownloads(IEnumerable<DownloadStatus> statuses)
        => statuses.Any(s => s is DownloadStatus.Running or DownloadStatus.Pending);
}
