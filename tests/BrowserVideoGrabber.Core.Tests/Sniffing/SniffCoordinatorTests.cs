/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Sniffing
*文件名： SniffCoordinatorTests
*版本号： V1.0.0.0
*唯一标识：af767206-38a5-40fd-a2e5-612a2440b97f
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 01:55:00
*描述：SniffCoordinator 的单元测试，覆盖多标签挂接、事件聚合与开关转发。
*
*=================================================
*修改标记
*修改时间：2026/9/13 01:55:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Models;
using BrowserVideoGrabber.Infrastructure.Sniffing;
using BrowserVideoGrabber.Tests.Fakes;
using Microsoft.Web.WebView2.WinForms;
using Xunit;

namespace BrowserVideoGrabber.Tests.Sniffing;

/// <summary>
/// 多标签嗅探协调器的测试。
/// </summary>
/// <remarks>
/// 用可注入的嗅探器工厂替换真实 <c>WebView2Sniffer</c>，因此不需要真实浏览器内核即可验证
/// 「多个标签的事件汇聚到同一条列表」这一核心行为。
/// </remarks>
public sealed class SniffCoordinatorTests
{
    [Fact]
    public void Attach_TwoTabs_CreatesTwoSniffersSharingOneIndex()
    {
        var created = new List<FakeVideoSniffer>();
        using var coordinator = CreateCoordinator(created);

        Assert.True(coordinator.Attach(new WebView2()));
        Assert.True(coordinator.Attach(new WebView2()));

        Assert.Equal(2, coordinator.TabCount);
        Assert.Equal(2, created.Count);

        // 共享索引是多标签能归并为「一行」的前提：两个嗅探器必须拿到同一个索引实例
        Assert.Same(created[0].InjectedIndex, created[1].InjectedIndex);
    }

    [Fact]
    public void Attach_SameViewTwice_IsIgnored()
    {
        var created = new List<FakeVideoSniffer>();
        using var coordinator = CreateCoordinator(created);

        var view = new WebView2();
        Assert.True(coordinator.Attach(view));
        Assert.False(coordinator.Attach(view));

        Assert.Single(created);
    }

    [Fact]
    public void Detach_StopsAndDisposesSniffer()
    {
        var created = new List<FakeVideoSniffer>();
        using var coordinator = CreateCoordinator(created);

        var view = new WebView2();
        coordinator.Attach(view);
        var sniffer = created[0];

        Assert.True(coordinator.Detach(view));

        Assert.Equal(1, sniffer.StopCount);
        Assert.True(sniffer.Disposed);
        Assert.Equal(0, coordinator.TabCount);
    }

    [Fact]
    public void Start_StartsEveryAttachedSniffer()
    {
        var created = new List<FakeVideoSniffer>();
        using var coordinator = CreateCoordinator(created);

        coordinator.Attach(new WebView2());
        coordinator.Attach(new WebView2());

        coordinator.Start();

        Assert.All(created, sniffer => Assert.True(sniffer.StartCount >= 1));
        Assert.True(coordinator.Enabled);
    }

    [Fact]
    public void Stop_PreventsNewlyAttachedTabFromStarting()
    {
        var created = new List<FakeVideoSniffer>();
        using var coordinator = CreateCoordinator(created);

        coordinator.Stop();
        coordinator.Attach(new WebView2());

        // 总开关关闭期间新建的标签不应被启动，否则用户会看到「关了嗅探却仍能抓」
        Assert.Equal(0, created[0].StartCount);
        Assert.False(coordinator.Enabled);
    }

    [Fact]
    public void EventsFromAllTabs_AreAggregatedToSingleSurface()
    {
        var created = new List<FakeVideoSniffer>();
        using var coordinator = CreateCoordinator(created);

        var detected = new List<SniffedVideo>();
        coordinator.VideoDetected += (_, video) => detected.Add(video);

        coordinator.Attach(new WebView2());
        coordinator.Attach(new WebView2());

        created[0].RaiseDetected(MakeVideo("https://a.test/1.m3u8"));
        created[1].RaiseDetected(MakeVideo("https://b.test/2.m3u8"));

        Assert.Equal(2, detected.Count);
    }

    [Fact]
    public void StoppedCoordinator_DoesNotForwardDetected()
    {
        var created = new List<FakeVideoSniffer>();
        using var coordinator = CreateCoordinator(created);

        var detected = new List<SniffedVideo>();
        coordinator.VideoDetected += (_, video) => detected.Add(video);

        coordinator.Attach(new WebView2());
        coordinator.Stop();

        created[0].RaiseDetected(MakeVideo("https://a.test/1.m3u8"));

        Assert.Empty(detected);
    }

    [Fact]
    public void Retracted_IsForwardedEvenWhenStopped()
    {
        var created = new List<FakeVideoSniffer>();
        using var coordinator = CreateCoordinator(created);

        var retracted = new List<SniffedVideo>();
        coordinator.VideoRetracted += (_, video) => retracted.Add(video);

        coordinator.Attach(new WebView2());
        coordinator.Stop();

        created[0].RaiseRetracted(MakeVideo("https://a.test/1.m3u8"));

        // 撤回是对已展示内容的修正，被开关拦下会让列表残留噪音行
        Assert.Single(retracted);
    }

    [Fact]
    public void Dispose_DetachesEverything()
    {
        var created = new List<FakeVideoSniffer>();
        var coordinator = CreateCoordinator(created);

        coordinator.Attach(new WebView2());
        coordinator.Attach(new WebView2());

        coordinator.Dispose();

        Assert.Equal(0, coordinator.TabCount);
        Assert.All(created, sniffer => Assert.True(sniffer.Disposed));
    }

    /// <summary>
    /// 创建使用假嗅探器的协调器。
    /// </summary>
    /// <param name="created">用于收集被创建出来的假嗅探器。</param>
    /// <returns>协调器实例。</returns>
    private static SniffCoordinator CreateCoordinator(List<FakeVideoSniffer> created)
    {
        return new SniffCoordinator((_, index) =>
        {
            var sniffer = new FakeVideoSniffer { InjectedIndex = index };
            created.Add(sniffer);
            return sniffer;
        });
    }

    /// <summary>构造一个最小可用的嗅探结果。</summary>
    /// <param name="url">资源地址。</param>
    /// <returns>嗅探结果。</returns>
    private static SniffedVideo MakeVideo(string url)
        => new()
        {
            Url = url,
            NormalizedUrl = url,
            Format = VideoFormat.M3u8
        };
}
