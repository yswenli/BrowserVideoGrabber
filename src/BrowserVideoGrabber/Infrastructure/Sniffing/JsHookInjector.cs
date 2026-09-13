/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Infrastructure.Sniffing
*文件名： JsHookInjector
*版本号： V1.0.0.0
*唯一标识：1cce4774-ed35-4ce2-86df-d7fa3c9bf7a1
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 23:40:00
*描述：把嗅探脚本注入到 WebView2 的每个新文档。
*
*=================================================
*修改标记
*修改时间：2026/9/12 23:40:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using Microsoft.Web.WebView2.Core;

namespace BrowserVideoGrabber.Infrastructure.Sniffing;

/// <summary>
/// 嗅探脚本注入器。
/// </summary>
/// <remarks>
/// 使用 <see cref="CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync"/> 而非
/// <c>ExecuteScriptAsync</c>，是注入时机决定的：
/// 前者保证脚本在页面任何脚本执行<b>之前</b>就被注册到每个新文档，
/// 后者只能在文档加载后执行，那时页面早已发完首轮请求，关键地址会永久漏抓。
/// </remarks>
public static class JsHookInjector
{
    /// <summary>
    /// 注入嗅探脚本。
    /// </summary>
    /// <param name="coreWebView">WebView2 核心对象。</param>
    /// <returns>表示异步注入的任务。</returns>
    /// <remarks>
    /// 重复调用是安全的：脚本内部通过 <c>__bvgbHooked</c> 标志自我去重。
    /// </remarks>
    public static async Task InjectAsync(CoreWebView2 coreWebView)
    {
        ArgumentNullException.ThrowIfNull(coreWebView);

        await coreWebView
            .AddScriptToExecuteOnDocumentCreatedAsync(JsHookScript.Source)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// 主动扫描当前页面的媒体资源。
    /// </summary>
    /// <param name="coreWebView">WebView2 核心对象。</param>
    /// <returns>扫描完成（含 1.2 秒后的补扫）后兑现的任务。</returns>
    /// <remarks>
    /// 对<b>已经加载完</b>的页面，<see cref="InjectAsync"/> 注册的文档钩子不会再执行，
    /// 因此这里直接把完整 Hook 源码再执行一遍（脚本内部以 <c>__bvgbHooked</c> 幂等，
    /// 无副作用地让 XHR / fetch / 媒体元素拦截在当前文档立刻生效），
    /// 随后运行 <see cref="JsHookScript.PageScanSource"/> 立即上报现有媒体元素地址。
    /// 这保证了：此后直播流每次刷新清单的 XHR / fetch 都会被重新捕获。
    /// </remarks>
    public static async Task ScanPageAsync(CoreWebView2 coreWebView)
    {
        ArgumentNullException.ThrowIfNull(coreWebView);

        await coreWebView.ExecuteScriptAsync(JsHookScript.Source).ConfigureAwait(true);
        await coreWebView.ExecuteScriptAsync(JsHookScript.PageScanSource).ConfigureAwait(true);
    }
}
