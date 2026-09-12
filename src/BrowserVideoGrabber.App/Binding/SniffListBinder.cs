/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.App.Binding
*文件名： SniffListBinder
*版本号： V1.0.0.0
*唯一标识：c147a0f8-6b2d-4f59-83ae-1d4c7b2f9e50
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 01:58:00
*描述：把嗅探器的后台事件桥接到嗅探面板的绑定器。
*
*=================================================
*修改标记
*修改时间：2026/9/13 01:58:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Abstractions;
using BrowserVideoGrabber.Core.Models;
using BrowserVideoGrabber.Infrastructure.Sniffing;

namespace BrowserVideoGrabber.App.Binding;

/// <summary>
/// 嗅探结果到嗅探面板的绑定器。
/// </summary>
/// <remarks>
/// <para>
/// 存在的唯一理由是<b>线程封送</b>：<see cref="IVideoSniffer.VideoDetected"/> 与
/// <see cref="IVideoSniffer.VideoRetracted"/> 都在 WebView2 的回调线程上触发，
/// 而列表控件只能在 UI 线程操作。
/// 把这件事收在这个小类里，面板本身就能保持「纯视图」的干净形态。
/// </para>
/// <para>
/// 撤回事件同样需要封送：它对应「某档清晰度先被上报、随后被主清单收编」这一情形，
/// 必须把那一行从列表移除，否则同一个视频仍会留下两行。
/// </para>
/// <para>
/// 使用 <c>BeginInvoke</c>（异步）而非 <c>Invoke</c>（同步）：嗅探回调位于浏览器内核的消息链路上，
/// 若在此同步等待 UI 线程，一旦 UI 繁忙就会阻塞浏览器渲染。
/// </para>
/// </remarks>
public sealed class SniffListBinder : IDisposable
{
    private readonly IVideoSniffer _sniffer;
    private readonly Panes.SniffPane _pane;
    private bool _disposed;

    /// <summary>
    /// 初始化绑定器并订阅嗅探事件。
    /// </summary>
    /// <param name="sniffer">嗅探器。</param>
    /// <param name="pane">目标面板。</param>
    public SniffListBinder(IVideoSniffer sniffer, Panes.SniffPane pane)
    {
        _sniffer = sniffer ?? throw new ArgumentNullException(nameof(sniffer));
        _pane = pane ?? throw new ArgumentNullException(nameof(pane));

        _sniffer.VideoDetected += OnVideoDetected;
        _sniffer.VideoRetracted += OnVideoRetracted;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sniffer.VideoDetected -= OnVideoDetected;
        _sniffer.VideoRetracted -= OnVideoRetracted;
    }

    /// <summary>
    /// 嗅探事件回调：封送到 UI 线程后写入列表。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="video">嗅探结果。</param>
    private void OnVideoDetected(object? sender, SniffedVideo video)
    {
        SniffDiagnostics.Write($"binder VideoDetected; id={video.Id:N}; url={video.Url}");
        Dispatch(() => _pane.AddOrUpdate(video));
    }

    /// <summary>
    /// 撤回事件回调：把已被更优条目取代的行从列表移除。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="video">被撤回的条目。</param>
    /// <remarks>
    /// 条目可能早已被用户手动移除，<c>RemoveItem</c> 会返回 false，属正常情况。
    /// </remarks>
    private void OnVideoRetracted(object? sender, SniffedVideo video)
        => Dispatch(() => _pane.RemoveItem(video.Id));

    /// <summary>
    /// 把动作封送到 UI 线程执行。
    /// </summary>
    /// <param name="action">待执行动作。</param>
    /// <remarks>
    /// 使用 <c>BeginInvoke</c>（异步）而非 <c>Invoke</c>（同步）：嗅探回调位于浏览器内核的消息链路上，
    /// 若在此同步等待 UI 线程，一旦 UI 繁忙就会阻塞浏览器渲染。
    /// </remarks>
    private void Dispatch(Action action)
    {
        if (_disposed || _pane.IsDisposed)
        {
            return;
        }

        try
        {
            if (_pane.InvokeRequired)
            {
                if (!_pane.IsHandleCreated)
                {
                    return;
                }

                _pane.BeginInvoke(() =>
                {
                    if (!_disposed && !_pane.IsDisposed)
                    {
                        action();
                    }
                });

                return;
            }

            action();
        }
        catch (ObjectDisposedException)
        {
            // 窗体正在关闭，忽略
        }
        catch (InvalidOperationException)
        {
            // 句柄尚未创建或已销毁，忽略
        }
    }
}
