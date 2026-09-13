/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.App
*文件名： StartupDiagnostics
*版本号： V1.0.0.0
*唯一标识：6a8d2f41-5c9b-4e73-8a26-3f1d7b4c9e58
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 03:22:00
*描述：启动期诊断，把未处理异常与初始化失败写入日志文件，避免「程序打不开」时无从排查。
*
*=================================================
*修改标记
*修改时间：2026/9/13 03:22:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Text;

namespace BrowserVideoGrabber.App;

/// <summary>
/// 启动期诊断工具。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么必须有它</b>：WinForms 程序以 <c>WinExe</c> 方式发布，没有控制台，
/// 一旦在启动阶段抛出异常，进程会静默退出或弹出一个信息量极低的对话框，
/// 用户能提供的线索只有「双击没反应」。把完整堆栈落到固定路径的日志文件，
/// 是把这类问题变成可诊断问题的唯一低成本手段。
/// </para>
/// <para>
/// 日志写入本身是「尽力而为」：任何写入失败都必须被吞掉，
/// 否则诊断代码自己就会成为新的崩溃源。
/// </para>
/// </remarks>
internal static class StartupDiagnostics
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BrowserVideoGrabber",
        "logs");

    /// <summary>日志文件完整路径。</summary>
    public static string LogFilePath => Path.Combine(LogDirectory, "app.log");

    /// <summary>
    /// 挂接全局异常处理。
    /// </summary>
    /// <remarks>
    /// 三处都要挂：UI 线程异常走 <see cref="Application.ThreadException"/>，
    /// 后台线程异常走 <see cref="AppDomain.UnhandledException"/>，
    /// 而未 await 的 Task 异常走 <see cref="TaskScheduler.UnobservedTaskException"/>，
    /// 任何一处遗漏都会让对应类型的故障无法被记录。
    /// </remarks>
    public static void Attach()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        Application.ThreadException += (_, e) => Write("UI 线程未处理异常", e.Exception, showDialog: true);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("非 UI 线程未处理异常", e.ExceptionObject as Exception, showDialog: false);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("未观察的任务异常", e.Exception, showDialog: false);

            // 标记为已观察，避免进程因一次可恢复的后台异常被直接终止
            e.SetObserved();
        };
    }

    /// <summary>
    /// 记录一条异常信息。
    /// </summary>
    /// <param name="context">异常发生的上下文描述。</param>
    /// <param name="exception">异常对象，可为 null。</param>
    /// <param name="showDialog">是否同时弹窗提示用户。</param>
    public static void Write(string context, Exception? exception, bool showDialog = false)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);

            var builder = new StringBuilder();
            builder.Append('[').Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ").AppendLine(context);
            builder.AppendLine(exception?.ToString() ?? "（无异常对象）");
            builder.AppendLine(new string('-', 72));

            File.AppendAllText(LogFilePath, builder.ToString(), Encoding.UTF8);
        }
        catch (Exception writeFailure) when (writeFailure is IOException or UnauthorizedAccessException)
        {
            // 诊断写入失败绝不能掩盖原始异常
        }

        if (!showDialog)
        {
            return;
        }

        try
        {
            MessageBox.Show(
                $"{context}：{exception?.Message}{Environment.NewLine}{Environment.NewLine}详细信息已记录到：{Environment.NewLine}{LogFilePath}",
                "程序错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch (InvalidOperationException)
        {
            // 极端情况下弹窗本身失败（如已在关闭流程中），忽略即可
        }
    }
}
