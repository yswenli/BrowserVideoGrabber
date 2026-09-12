/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Abstractions
*文件名： IVideoSniffer
*版本号： V1.0.0.0
*唯一标识：99f30e1c-690e-47e2-912a-02c38afd3cc5
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:21:00
*描述：视频嗅探器抽象接口，屏蔽底层浏览器内核差异。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:21:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Core.Abstractions;

/// <summary>
/// 视频嗅探器抽象。
/// </summary>
/// <remarks>
/// 抽象的意义在于：界面层只依赖「发现视频资源」这一事件，
/// 后续若把 WebView2 换成 CefSharp 或其它内核，界面代码一行都不用改。
/// 实现方（如 <c>WebView2Sniffer</c>）需保证 <see cref="VideoDetected"/> 在 UI 线程之外触发，
/// 由订阅方自行负责线程切换。
/// </remarks>
public interface IVideoSniffer
{
    /// <summary>
    /// 发现可下载视频资源时触发。
    /// </summary>
    /// <remarks>
    /// 同一资源可能被多条嗅探链路重复捕获，去重由订阅方（或嗅探器内部）负责。
    /// 事件可能在任意后台线程触发，订阅方不得直接操作界面控件。
    /// </remarks>
    event EventHandler<SniffedVideo>? VideoDetected;

    /// <summary>
    /// 启动嗅探：注册网络监听与脚本注入。
    /// </summary>
    /// <remarks>重复调用应当幂等，不得重复注册事件导致资源被多次上报。</remarks>
    void Start();

    /// <summary>
    /// 停止嗅探：注销一切监听，但保留已经上报的结果。
    /// </summary>
    void Stop();
}
