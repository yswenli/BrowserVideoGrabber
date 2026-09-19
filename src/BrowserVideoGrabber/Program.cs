/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.App
*文件名： Program
*版本号： V1.0.0.0
*唯一标识：31d47392-5e2b-4a17-9f83-6c1d4b7a2e05
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 03:05:00
*描述：程序入口，负责装配应用宿主与主窗体并进入消息循环。
*
*=================================================
*修改标记
*修改时间：2026/9/13 03:05:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.App;
using BrowserVideoGrabber.App.Forms;

namespace BrowserVideoGrabber.App;

/// <summary>
/// 程序入口。
/// </summary>
/// <remarks>
/// 组合顺序是「先建设施、后建界面」：<see cref="AppHost"/> 完成全部依赖装配后才构造主窗体，
/// 因此窗体拿到的是一个完全可用的宿主，无需在构造函数里做任何延迟初始化。
/// 两者都用 <c>using</c> 声明，保证进程退出前一定释放（尤其 WebView2 与 ffmpeg 相关资源）。
/// </remarks>
internal static class Program
{
    /// <summary>
    /// 应用程序主入口。
    /// </summary>
    [STAThread]
    private static void Main()
    {
        // 启用视觉样式、按显示器 DPI 缩放并设置默认字体
        ApplicationConfiguration.Initialize();

        // 必须在任何业务代码之前挂接：否则启动阶段的异常将无从记录
        StartupDiagnostics.Attach();

        try
        {
            using var host = new AppHost();
            using var mainForm = new MainForm(host);

            Application.Run(mainForm);
        }
        catch (Exception exception)
        {
            StartupDiagnostics.Write("启动阶段发生未处理异常", exception, showDialog: true);
        }
    }
}
