/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Downloads
*文件名： ExitGuardTests
*版本号： V1.0.0.0
*唯一标识：7a1c3d5e-2b8f-4e6a-9c0b-3d4e5f6a7b8c
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/19 22:33:00
*描述：ExitGuard 的单元测试，覆盖「是否存在活动下载」的各状态组合判定。
*
*=================================================
*修改标记
*修改时间：2026/9/19 22:33:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Downloads;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Tests.Downloads;

/// <summary>
/// <see cref="ExitGuard"/> 的行为验证。
/// </summary>
/// <remarks>
/// 退出确认弹窗依赖这个判定：一旦漏判「正在下载」就可能在用户不知情时中止任务。
/// 用单测把状态枚举语义钉死，避免将来有人把 Paused 也当成活动态、或漏掉 Pending。
/// </remarks>
public sealed class ExitGuardTests
{
    /// <summary>正在下载的任务应判定为活动。</summary>
    [Fact]
    public void HasActiveDownloads_WithRunning_ReturnsTrue()
        => Assert.True(ExitGuard.HasActiveDownloads([DownloadStatus.Running]));

    /// <summary>已入队但还没占用槽位的任务也应判定为活动（退出会中断它）。</summary>
    [Fact]
    public void HasActiveDownloads_WithPending_ReturnsTrue()
        => Assert.True(ExitGuard.HasActiveDownloads([DownloadStatus.Pending]));

    /// <summary>只有终态（完成/失败/取消）时不应弹确认。</summary>
    [Fact]
    public void HasActiveDownloads_OnlyTerminalStatuses_ReturnsFalse()
        => Assert.False(ExitGuard.HasActiveDownloads(
            [DownloadStatus.Completed, DownloadStatus.Failed, DownloadStatus.Canceled]));

    /// <summary>已暂停不是「活动」态，退出无需特别确认（暂停任务留作暂停即可）。</summary>
    [Fact]
    public void HasActiveDownloads_WithPausedOnly_ReturnsFalse()
        => Assert.False(ExitGuard.HasActiveDownloads([DownloadStatus.Paused]));

    /// <summary>空集合（没有任何任务）不应弹确认。</summary>
    [Fact]
    public void HasActiveDownloads_Empty_ReturnsFalse()
        => Assert.False(ExitGuard.HasActiveDownloads(Array.Empty<DownloadStatus>()));

    /// <summary>混合集合只要含任一活动态即判定为活动。</summary>
    [Fact]
    public void HasActiveDownloads_MixedWithRunning_ReturnsTrue()
        => Assert.True(ExitGuard.HasActiveDownloads(
            [DownloadStatus.Completed, DownloadStatus.Running, DownloadStatus.Failed]));
}
