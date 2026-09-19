/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Downloads
*文件名： FileNameBuilderTests
*版本号： V1.0.0.0
*唯一标识：8a90de77-63f7-4bfa-bc43-2c7e27b8e702
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 03:00:00
*描述：文件名派生工具的单元测试，锁住「页面标题优先、截断 15、回退与清洗」这组规则。
*
*=================================================
*修改标记
*修改时间：2026/9/13 03:00:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Downloads;

namespace BrowserVideoGrabber.Tests.Downloads;

/// <summary>
/// <see cref="FileNameBuilder"/> 的行为验证。
/// </summary>
/// <remarks>
/// 之所以要为「拼一个文件名」写测试：这项规则曾经静默失效过 ——
/// 页面标题在 <c>SniffedVideo.WithId</c> 里被漏拷，于是文件名一直回退成 URL 派生名，
/// 而该逻辑当时以私有方法形式藏在 App 层，没有任何测试能发现。
/// 把它下沉到 Core 之后，规则必须由测试固化。
/// </remarks>
public sealed class FileNameBuilderTests
{
    /// <summary>
    /// 页面标题优先，且截断到 15 个字符。
    /// </summary>
    [Fact]
    public void Build_ShouldUsePageTitle_TruncatedToMaxLength()
    {
        var name = FileNameBuilder.Build("这是一个非常非常长的视频页面标题需要被截断", "playlist");

        Assert.Equal(15, name.Length);
        Assert.Equal("这是一个非常非常长的视频页面标", name);
    }

    /// <summary>
    /// 页面标题缺失时回退到 URL 派生名，且不受 15 字符限制。
    /// </summary>
    [Fact]
    public void Build_ShouldFallbackToUrlTitle_WhenPageTitleMissing()
    {
        var name = FileNameBuilder.Build(null, "an-episode-title-that-is-quite-long-indeed");

        Assert.Equal("an-episode-title-that-is-quite-long-indeed", name);
    }

    /// <summary>
    /// 两者都缺失时返回固定兜底名，绝不返回空串。
    /// </summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "  ")]
    public void Build_ShouldReturnFallbackName_WhenNothingUsable(string? pageTitle, string? fallback)
    {
        Assert.Equal("video", FileNameBuilder.Build(pageTitle, fallback));
    }

    /// <summary>
    /// 文件系统不允许的字符被替换为下划线。
    /// </summary>
    [Fact]
    public void Build_ShouldReplaceInvalidCharacters()
    {
        var name = FileNameBuilder.Build("a/b:c*d?e", null);

        Assert.Equal("a_b_c_d_e", name);
    }

    /// <summary>
    /// 清洗发生在截断之前：否则前 15 个字符里若全是非法字符，替换后几乎不剩内容。
    /// </summary>
    [Fact]
    public void Build_ShouldSanitizeBeforeTruncating()
    {
        // 前 15 个字符里有 5 个非法字符，先清洗再截断应得到 15 个有效字符
        var name = FileNameBuilder.Build("ab/cd*ef:gh?ijklmnopqrst", null);

        Assert.DoesNotContain("/", name);
        Assert.DoesNotContain("*", name);
        Assert.DoesNotContain(":", name);
        Assert.DoesNotContain("?", name);
    }

    /// <summary>
    /// 截断不得切在代理项对中间。
    /// </summary>
    [Fact]
    public void Build_ShouldNotSplitSurrogatePair()
    {
        // 14 个 ASCII + 1 个emoji（2 个 char），总长 16，截断点正好落在高代理项上
        var name = FileNameBuilder.Build("abcdefghijklmn\U0001F389", null);

        Assert.Equal("abcdefghijklmn", name);
    }

    /// <summary>
    /// 尾部点号必须清掉：Windows 会自动去掉文件名末尾的点，导致写入与查找不一致。
    /// </summary>
    [Fact]
    public void Build_ShouldTrimTrailingDots()
    {
        var name = FileNameBuilder.Build("abcdefghijklmno.", null);

        Assert.Equal("abcdefghijklmno", name);
    }

    /// <summary>
    /// 全部字符都是非法字符时不应返回一串下划线，而应回退。
    /// </summary>
    [Fact]
    public void Build_ShouldFallback_WhenTitleIsOnlyInvalidCharacters()
    {
        var name = FileNameBuilder.Build("///", "real-name");

        Assert.Equal("real-name", name);
    }
}
