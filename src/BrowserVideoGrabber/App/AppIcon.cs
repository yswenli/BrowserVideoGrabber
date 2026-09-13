/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.App
*文件名： AppIcon
*版本号： V1.0.0.0
*唯一标识：b4d7e3f1-9c2a-4f6b-8d5e-1a3c7b9f2d64
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 02:12:00
*描述：统一加载窗体图标（favicon.ico），供所有窗体复用，避免图标来源散落各处。
*
*=================================================
*修改标记
*修改时间：2026/9/13 02:12:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Drawing;

namespace BrowserVideoGrabber.App;

/// <summary>
/// 窗体图标统一入口。
/// </summary>
/// <remarks>
/// <para>
/// 所有窗体都应通过 <see cref="Load"/> 取得图标，而不是各自硬编码路径或内嵌资源，
/// 这样「换图标只改一处」：favicon.ico 已在 csproj 中以 Content 形式随程序发布到输出目录。
/// </para>
/// <para>
/// <b>为什么要每次返回新实例</b>：<see cref="Icon"/> 在所属窗体释放（Dispose）时可能被一并释放。
/// 若多个窗体共享同一个实例，其中一个关闭就会连带损坏其余窗体的图标，表现为任务栏图标突然变白。
/// 返回新实例让每个窗体各自持有一份，互不牵连。
/// </para>
/// </remarks>
public static class AppIcon
{
    private const string IconFileName = "favicon.ico";

    /// <summary>
    /// 取得窗体图标：优先读取程序目录下的 favicon.ico，缺失时回退到 exe 自身内嵌的图标。
    /// </summary>
    /// <returns>
    /// 可用的图标；两种来源都取不到时返回 <see langword="null"/>，
    /// 调用方保留 WinForms 默认图标即可，不应把缺图标当作错误。
    /// </returns>
    /// <remarks>
    /// <b>为什么要有兜底</b>：csproj 的 <c>ApplicationIcon</c> 会把 favicon.ico 编译进 exe，
    /// 而 <c>Content</c> 是否复制到输出目录另说。一旦复制配置失效（或单文件发布），
    /// 只认文件就会全部退回默认图标。从 exe 自身提取让图标与「程序图标」永远一致。
    /// </remarks>
    public static Icon? Load()
        => LoadFromFile() ?? LoadFromExecutable();

    /// <summary>
    /// 从程序基目录加载 favicon.ico。
    /// </summary>
    /// <returns>加载成功的图标；文件缺失或损坏时返回 <see langword="null"/>。</returns>
    private static Icon? LoadFromFile()
    {
        var path = Path.Combine(AppContext.BaseDirectory, IconFileName);

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            // 每次返回新实例：见类注释「共享同一实例的释放陷阱」
            return new Icon(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 图标文件损坏或读取被拒：不影响程序运行，退到 exe 内嵌图标
            return null;
        }
    }

    /// <summary>
    /// 从当前进程的可执行文件中提取内嵌图标。
    /// </summary>
    /// <returns>提取成功的图标；提取失败时返回 <see langword="null"/>。</returns>
    /// <remarks>
    /// 用 <see cref="Environment.ProcessPath"/> 而非 <c>Application.ExecutablePath</c>，
    /// 使本类不依赖 WinForms，便于单元测试直接调用。
    /// </remarks>
    private static Icon? LoadFromExecutable()
    {
        var executable = Environment.ProcessPath;

        if (string.IsNullOrEmpty(executable) || !File.Exists(executable))
        {
            return null;
        }

        try
        {
            return Icon.ExtractAssociatedIcon(executable);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
