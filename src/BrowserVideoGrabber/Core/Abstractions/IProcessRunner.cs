/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Abstractions
*文件名： IProcessRunner
*版本号： V1.0.0.0
*唯一标识：d54e151d-439a-4b94-8024-2cfb38c432fe
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:23:00
*描述：外部进程运行器抽象，使 ffmpeg 调用逻辑可脱离真实进程进行单元测试。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:23:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

namespace BrowserVideoGrabber.Core.Abstractions;

/// <summary>
/// 外部进程运行器抽象。
/// </summary>
/// <remarks>
/// <para>
/// 引入该抽象的核心目的是可测试性：下载处理器的大量逻辑集中在
/// 「如何构建 ffmpeg 参数」与「如何从 stderr 解析进度」两件事上，
/// 若直接依赖 <c>System.Diagnostics.Process</c>，这两块逻辑就只能靠真实下载来验证。
/// 通过注入本接口的假实现，可以精确喂入任意 stderr 文本，做完全确定性的测试。
/// </para>
/// <para>
/// 实现必须满足：取消令牌触发时，不仅终止主进程，还要终止整棵子进程树，
/// 否则 ffmpeg 派生的分片拉取进程会成为孤儿进程残留在系统中。
/// </para>
/// </remarks>
public interface IProcessRunner
{
    /// <summary>
    /// 运行外部进程并等待其退出。
    /// </summary>
    /// <param name="executablePath">可执行文件路径或位于 PATH 中的命令名（如 ffmpeg）。</param>
    /// <param name="arguments">参数列表。实现方需保证每个参数原样传递，不做拼接与转义改写。</param>
    /// <param name="onStandardErrorLine">
    /// 标准错误每一行的回调。ffmpeg 的进度与日志全部走 stderr，因此该回调是进度采集的唯一入口。
    /// 回调在后台线程触发。
    /// </param>
    /// <param name="cancellationToken">取消令牌，触发时应终止整棵进程树。</param>
    /// <returns>进程退出码。0 表示成功。</returns>
    Task<int> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        Action<string> onStandardErrorLine,
        CancellationToken cancellationToken);
}
