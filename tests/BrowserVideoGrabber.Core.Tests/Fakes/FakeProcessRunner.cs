/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Fakes
*文件名： FakeProcessRunner
*版本号： V1.0.0.0
*唯一标识：12554f7a-b280-43f2-af5e-f84d0a8164cf
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:30:00
*描述：进程运行器的假实现，用于在不启动真实 ffmpeg 的前提下测试下载编排逻辑。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:30:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Abstractions;

namespace BrowserVideoGrabber.Tests.Fakes;

/// <summary>
/// <see cref="IProcessRunner"/> 的假实现：记录调用参数，按预设脚本回放 stderr 行并返回退出码。
/// </summary>
/// <remarks>
/// 用它可以在完全确定性的条件下验证「ffmpeg 进度解析」与「退出码到下载结果的映射」，
/// 既不需要安装 ffmpeg，也不受网络波动影响。
/// </remarks>
public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly List<ProcessInvocation> _invocations = new();

    /// <summary>所有已发生的进程调用记录，按发生顺序排列。</summary>
    public IReadOnlyList<ProcessInvocation> Invocations
    {
        get
        {
            lock (_invocations)
            {
                return _invocations.ToList();
            }
        }
    }

    /// <summary>启动进程时要回放到 stderr 回调的行序列。</summary>
    public IList<string> StandardErrorLines { get; } = new List<string>();

    /// <summary>要返回的进程退出码，默认 0（成功）。</summary>
    public int ExitCode { get; set; }

    /// <summary>是否在回放 stderr 后抛出取消异常，用于模拟用户中途取消。</summary>
    public bool ThrowOnCancel { get; set; } = true;

    /// <summary>每次 stderr 行回放之间的延时，用于模拟真实下载耗时。</summary>
    public TimeSpan LineDelay { get; set; } = TimeSpan.Zero;

    /// <summary>本次调用是否实际执行到了末尾（未被取消打断）。</summary>
    public bool LastRunCompleted { get; private set; }

    /// <inheritdoc />
    public async Task<int> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        Action<string> onStandardErrorLine,
        CancellationToken cancellationToken)
    {
        LastRunCompleted = false;

        lock (_invocations)
        {
            _invocations.Add(new ProcessInvocation(executablePath, arguments.ToList()));
        }

        foreach (var line in StandardErrorLines)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // 模拟真实进程被终止：取消时抛出 OperationCanceledException，
                // 而不是安静地返回退出码，这样上层才能区分「取消」与「失败」
                if (ThrowOnCancel)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return -1;
            }

            onStandardErrorLine(line);

            if (LineDelay > TimeSpan.Zero)
            {
                await Task.Delay(LineDelay, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // 让出线程，模拟异步执行，避免整个回放同步跑完
                await Task.Yield();
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        LastRunCompleted = true;
        return ExitCode;
    }

    /// <summary>
    /// 一次进程调用的记录。
    /// </summary>
    /// <param name="ExecutablePath">可执行文件路径。</param>
    /// <param name="Arguments">参数列表。</param>
    public sealed record ProcessInvocation(string ExecutablePath, IReadOnlyList<string> Arguments)
    {
        /// <summary>
        /// 判断参数列表中是否存在指定参数。
        /// </summary>
        /// <param name="argument">要查找的参数。</param>
        /// <returns>存在返回 true。</returns>
        public bool HasArgument(string argument)
            => Arguments.Any(a => string.Equals(a, argument, StringComparison.Ordinal));

        /// <summary>
        /// 获取指定参数紧随其后的取值。
        /// </summary>
        /// <param name="argument">参数名。</param>
        /// <returns>紧随其后的取值；未找到时返回 null。</returns>
        public string? GetValueAfter(string argument)
        {
            for (var i = 0; i < Arguments.Count - 1; i++)
            {
                if (string.Equals(Arguments[i], argument, StringComparison.Ordinal))
                {
                    return Arguments[i + 1];
                }
            }

            return null;
        }
    }
}
