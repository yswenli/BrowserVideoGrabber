/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Downloads
*文件名： PlaylistProxyResolverTests
*版本号： V1.0.0.0
*唯一标识：d2f3a4b5-6c7d-8e9f-0a1b-2c3d4e5f6a7b
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/11/21 01:10:00
*描述：播放地址代理解析测试：把 `?url=<编码后的 m3u8>` 包装地址还原为真实清单地址。
*
*=================================================
*修改标记
*修改时间：2026/11/21 01:10:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Downloads;
using Xunit;

namespace BrowserVideoGrabber.Tests.Downloads;

/// <summary>
/// <see cref="PlaylistProxyResolver"/> 的单元测试。
/// </summary>
public sealed class PlaylistProxyResolverTests
{
    private const string Inner =
        "https://cdn.example.com/videos/abc.m3u8?auth_key=1795183800-065&v=3";

    [Fact]
    public void Resolve_EncodedWrapperQuery_ReturnsInnerUrl()
    {
        var url = "https://site.example.com/?url=" + Uri.EscapeDataString(Inner);

        Assert.Equal(Inner, PlaylistProxyResolver.Resolve(url));
    }

    [Fact]
    public void Resolve_UrlParameterAfterOtherParameters_ReturnsInnerUrl()
    {
        var url = "https://site.example.com/play?from=home&url=" + Uri.EscapeDataString(Inner) + "&t=1";

        Assert.Equal(Inner, PlaylistProxyResolver.Resolve(url));
    }

    [Fact]
    public void Resolve_ParameterNameIsCaseInsensitive_ReturnsInnerUrl()
    {
        var url = "https://site.example.com/?URL=" + Uri.EscapeDataString(Inner);

        Assert.Equal(Inner, PlaylistProxyResolver.Resolve(url));
    }

    [Fact]
    public void Resolve_NoUrlParameter_ReturnsOriginal()
    {
        var url = "https://cdn.example.com/videos/abc.m3u8?auth_key=1";

        Assert.Equal(url, PlaylistProxyResolver.Resolve(url));
    }

    [Fact]
    public void Resolve_DirectPlaylistUrl_ReturnsOriginal()
    {
        Assert.Equal(Inner, PlaylistProxyResolver.Resolve(Inner));
    }

    [Fact]
    public void Resolve_OuterIsItselfPlaylist_ReturnsOriginal()
    {
        var url = "https://cdn.example.com/videos/abc.m3u8?url=" + Uri.EscapeDataString(Inner);

        Assert.Equal(url, PlaylistProxyResolver.Resolve(url));
    }

    [Fact]
    public void Resolve_EmptyUrlValue_ReturnsOriginal()
    {
        var url = "https://site.example.com/?url=";

        Assert.Equal(url, PlaylistProxyResolver.Resolve(url));
    }

    [Fact]
    public void Resolve_InnerNotHttp_ReturnsOriginal()
    {
        var url = "https://site.example.com/?url=" + Uri.EscapeDataString("file:///C:/secret.m3u8");

        Assert.Equal(url, PlaylistProxyResolver.Resolve(url));
    }

    [Fact]
    public void Resolve_InnerIsNotPlaylist_ReturnsOriginal()
    {
        // 只解析指向 m3u8 的包装地址：`?url=` 在其他站点可能只是普通查询参数
        var url = "https://site.example.com/?url=" + Uri.EscapeDataString("https://cdn.example.com/v.mp4");

        Assert.Equal(url, PlaylistProxyResolver.Resolve(url));
    }

    [Fact]
    public void Resolve_NonHttpOuter_ReturnsOriginal()
    {
        var url = "ftp://site.example.com/?url=" + Uri.EscapeDataString(Inner);

        Assert.Equal(url, PlaylistProxyResolver.Resolve(url));
    }

    [Fact]
    public void Resolve_RelativeOuter_ReturnsOriginal()
    {
        var url = "/play?url=" + Uri.EscapeDataString(Inner);

        Assert.Equal(url, PlaylistProxyResolver.Resolve(url));
    }

    [Fact]
    public void Resolve_MalformedEscaping_ReturnsOriginalWithoutThrowing()
    {
        var url = "https://site.example.com/?url=%E0%A4%A";

        Assert.Equal(url, PlaylistProxyResolver.Resolve(url));
    }
}