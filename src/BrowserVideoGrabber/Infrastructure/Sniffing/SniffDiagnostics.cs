/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Infrastructure.Sniffing
*文件名： SniffDiagnostics
*版本号： V1.0.0.0
*唯一标识：0f27c8d1-6e94-4a77-9c62-b5a3e1d7f4c8
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 14:20:00
*描述：嗅探链路诊断日志，把「订阅 → 识别 → 去重 → 界面」各环节的关键事件落到固定路径的日志文件，
*      便于排查「开发者工具能看到地址但列表始终为空」这类静默断链问题。
*
*=================================================
*修改标记
*修改时间：2026/9/13 14:20:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Text;

namespace BrowserVideoGrabber.Infrastructure.Sniffing;

/// <summary>
/// 嗅探链路诊断日志。
/// </summary>
/// <remarks>
/// <para>
/// 日志写入是「尽力而为」：任何失败都被吞掉，诊断代码本身不允许成为新的故障源。
/// 单行采用「时间 + 环节 + 事实」的扁平格式，便于逐行比对请求流与列表行为。
/// 日志文件位于 <c>%LOCALAPPDATA%\BrowserVideoGrabber\logs\sniff.log</c>。
/// </para>
/// </remarks>
public static class SniffDiagnostics
{
    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BrowserVideoGrabber",
        "logs",
        "sniff.log");

    private static readonly object Gate = new();

    /// <summary>
    /// 清空日志，标记一次新的运行周期开始。
    /// </summary>
    public static void Reset()
    {
        try
        {
            lock (Gate)
            {
                var directory = Path.GetDirectoryName(LogFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(LogFilePath, string.Empty, Encoding.UTF8);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 清空失败（如文件被占用）不影响本次诊断
        }
    }

    /// <summary>
    /// 追加一行诊断记录。
    /// </summary>
    /// <param name="message">记录内容。</param>
    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                File.AppendAllText(
                    LogFilePath,
                    $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 诊断写入失败必须静默
        }
    }
}