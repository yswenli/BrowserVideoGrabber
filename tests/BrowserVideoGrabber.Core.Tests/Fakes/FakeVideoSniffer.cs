/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Fakes
*文件名： FakeVideoSniffer
*版本号： V1.0.0.0
*唯一标识：0f2b22d5-8939-4347-92e4-dda1bc046434
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 01:55:00
*描述：视频嗅探器的内存假实现，可主动抛出事件并记录 Start/Stop/Clear 调用次数。
*
*=================================================
*修改标记
*修改时间：2026/9/13 01:55:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Abstractions;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Tests.Fakes;

/// <summary>
/// <see cref="IVideoSniffer"/> 的假实现。
/// </summary>
/// <remarks>
/// 用于在不依赖真实 WebView2 的前提下测试 <c>SniffCoordinator</c> 的事件聚合与开关转发：
/// 测试可主动调用 <see cref="RaiseDetected"/> / <see cref="RaiseRetracted"/> 模拟某个标签抓到资源，
/// 再用 <see cref="StartCount"/> 等计数器断言协调器是否正确把开关下发到每个标签。
/// </remarks>
public sealed class FakeVideoSniffer : IVideoSniffer, IDisposable
{
    /// <summary><see cref="Start"/> 被调用的次数。</summary>
    public int StartCount { get; private set; }

    /// <summary><see cref="Stop"/> 被调用的次数。</summary>
    public int StopCount { get; private set; }

    /// <summary><see cref="Clear"/> 被调用的次数。</summary>
    public int ClearCount { get; private set; }

    /// <summary>是否已被释放（协调器在解除挂接时应释放它）。</summary>
    public bool Disposed { get; private set; }

    /// <summary>创建时注入的索引，用于断言「多个标签共用同一份索引」。</summary>
    public object? InjectedIndex { get; set; }

    /// <inheritdoc />
    public event EventHandler<SniffedVideo>? VideoDetected;

    /// <inheritdoc />
    public event EventHandler<SniffedVideo>? VideoRetracted;

    /// <inheritdoc />
    public void Start() => StartCount++;

    /// <inheritdoc />
    public void Stop() => StopCount++;

    /// <inheritdoc />
    public void Clear() => ClearCount++;

    /// <summary>模拟「该标签发现了一个视频」。</summary>
    /// <param name="video">视频条目。</param>
    public void RaiseDetected(SniffedVideo video) => VideoDetected?.Invoke(this, video);

    /// <summary>模拟「该标签撤回了一个视频」。</summary>
    /// <param name="video">视频条目。</param>
    public void RaiseRetracted(SniffedVideo video) => VideoRetracted?.Invoke(this, video);

    /// <inheritdoc />
    public void Dispose()
    {
        Disposed = true;
        VideoDetected = null;
        VideoRetracted = null;
    }
}
