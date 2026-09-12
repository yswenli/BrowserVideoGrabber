/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Infrastructure.Execution
*文件名： ProcessRunner
*版本号： V1.0.0.0
*唯一标识：94cd3c63-8685-41ad-b09e-a75e43d2c845
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 23:26:00
*描述：基于 System.Diagnostics.Process 的进程运行器实现，支持整棵进程树终止。
*
*=================================================
*修改标记
*修改时间：2026/9/12 23:26:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Diagnostics;
using System.Text;
using BrowserVideoGrabber.Core.Abstractions;

namespace BrowserVideoGrabber.Infrastructure.Execution;

/// <summary>
/// 基于 <see cref="Process"/> 的进程运行器。
/// </summary>
/// <remarks>
/// <para>
/// 三处细节决定了本实现的可靠性，缺一不可：
/// <list type="number">
///   <item><description>
///     <b>参数逐项传递</b>：通过 <c>ProcessStartInfo.ArgumentList</c> 而非拼接命令行字符串，
///     从而天然支持含空格的路径与含 CRLF 的请求头块，无需任何转义处理。
///   </description></item>
///   <item><description>
///     <b>标准输出必须被抽干</b>：即使我们只关心 stderr，也必须异步读取 stdout。
///     否则当子进程写满 stdout 管道缓冲区时会阻塞，进而导致整个下载卡死。
///   </description></item>
///   <item><description>
///     <b>终止整棵进程树</b>：ffmpeg 在处理 HLS 会派生子任务，
///     只杀主进程会留下孤儿进程占用带宽与磁盘句柄。
///   </description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class ProcessRunner : IProcessRunner
{
    /// <inheritdoc />
    public async Task<int> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        Action<string> onStandardErrorLine,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(onStandardErrorLine);

        var startInfo = new ProcessStartInfo(executablePath)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
            // ffmpeg 的日志与进度均为 UTF-8；不显式指定会按系统 ANSI 代码页解码，中文路径会乱码
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                onStandardErrorLine(eventArgs.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"无法启动进程：{executablePath}");
        }

        process.BeginErrorReadLine();

        // 即使不消费 stdout，也必须读取，否则管道写满会导致子进程阻塞
        var standardOutputDrain = process.StandardOutput.ReadToEndAsync(cancellationToken);

        await using var cancellationRegistration = cancellationToken.Register(static state =>
        {
            var target = (Process)state!;
            KillProcessTree(target);
        }, process);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 取消令牌注册回调可能尚未执行完，此处再确保一次，避免进程残留
            KillProcessTree(process);
            throw;
        }

        try
        {
            await standardOutputDrain.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 取消流程中 stdout 读取被中断属预期情况
        }

        return process.ExitCode;
    }

    /// <summary>
    /// 安全地终止整棵进程树。
    /// </summary>
    /// <param name="process">目标进程。</param>
    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // 进程已退出
        }
        catch (NotSupportedException)
        {
            // 极少数平台或权限场景不支持整树终止，退化为单进程终止
            try
            {
                process.Kill();
            }
            catch
            {
                // 已无可为，忽略
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 权限不足导致终止失败，交由上层等待退出
        }
    }
}
