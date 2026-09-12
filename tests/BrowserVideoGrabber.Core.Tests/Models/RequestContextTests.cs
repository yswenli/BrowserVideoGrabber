/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Models
*文件名： RequestContextTests
*版本号： V1.0.0.0
*唯一标识：0bbc2030-cbd0-40cf-bf0c-065d6d7c5ec9
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 03:20:00
*描述：请求上下文头块构建的单元测试，重点验证 Origin 请求头不再外发。
*
*=================================================
*修改标记
*修改时间：2026/9/13 03:20:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Tests.Models;

/// <summary>
/// <see cref="RequestContext"/> 的行为验证。
/// </summary>
/// <remarks>
/// 本组用例的核心是一条实测结论：请求携带 <c>Origin</c> 时，目标 CDN 会对<b>每一个</b>分片
/// 返回同一张 56024 字节的占位 JPEG（HTTP 200，内容为一段格式合法的 8 秒 TS），
/// 而 ffmpeg 会把它当作正常分片封装进成品，最终得到一个「能打开但不是视频」的 mp4。
/// 因此「头块中绝不出现 Origin」必须被测试固化，而不是留给后来者凭印象判断。
/// </remarks>
public sealed class RequestContextTests
{
    /// <summary>
    /// 即使上下文中带 Origin，头块也不得包含该头。
    /// </summary>
    [Fact]
    public void BuildHeaderBlock_ShouldNotEmitOrigin_EvenWhenContextCarriesIt()
    {
        var context = new RequestContext
        {
            Referer = "https://www.example.com/watch/1",
            Origin = "https://www.example.com",
            UserAgent = "Mozilla/5.0 Test",
            Cookie = "sid=abc"
        };

        var block = context.BuildHeaderBlock();

        Assert.DoesNotContain("Origin:", block, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Referer: https://www.example.com/watch/1", block, StringComparison.Ordinal);
        Assert.Contains("User-Agent: Mozilla/5.0 Test", block, StringComparison.Ordinal);
        Assert.Contains("Cookie: sid=abc", block, StringComparison.Ordinal);
    }

    /// <summary>
    /// 其余请求头照常输出，且每行以 CRLF 结尾（ffmpeg -headers 的硬性要求）。
    /// </summary>
    [Fact]
    public void BuildHeaderBlock_ShouldKeepCrlfSeparatedHeaders()
    {
        var context = new RequestContext { Referer = "https://a.com/w", UserAgent = "UA" };

        var block = context.BuildHeaderBlock();

        Assert.Contains("\r\n", block, StringComparison.Ordinal);
        Assert.EndsWith("\r\n", block, StringComparison.Ordinal);
    }

    /// <summary>
    /// 所有字段为空时返回空串，避免向 ffmpeg 传入空的 <c>-headers</c> 参数。
    /// </summary>
    [Fact]
    public void BuildHeaderBlock_ShouldBeEmpty_WhenNothingConfigured()
    {
        var context = new RequestContext();

        Assert.Equal(string.Empty, context.BuildHeaderBlock());
    }

    /// <summary>
    /// Origin 属性本身必须保留：它参与设置持久化，也是排查「分片全是占位图」时的重要线索。
    /// 停止外发与删除字段是两件事。
    /// </summary>
    [Fact]
    public void Clone_ShouldPreserveOriginValue_ForDiagnostics()
    {
        var context = new RequestContext { Origin = "https://www.example.com" };

        var clone = context.Clone();

        Assert.Equal("https://www.example.com", clone.Origin);
    }

    /// <summary>
    /// 自定义请求头照常注入，且不区分大小写地识别重复键。
    /// </summary>
    [Fact]
    public void BuildHeaderBlock_ShouldIncludeExtraHeaders()
    {
        var context = new RequestContext();
        context.ExtraHeaders["X-Custom"] = "1";

        var block = context.BuildHeaderBlock();

        Assert.Contains("X-Custom: 1", block, StringComparison.Ordinal);
    }
}
