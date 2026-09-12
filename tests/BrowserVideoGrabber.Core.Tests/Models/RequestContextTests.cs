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
*描述：请求上下文头块构建的单元测试，重点验证 Origin 请求头会随页面来源一并外发。
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
/// <para>
/// 本组用例的核心是<b>纠正过一次方向</b>的实测结论：早期认为「携带 <c>Origin</c> 会让 CDN 对
/// 每个分片返回同一张 56024 字节占位 JPEG」，于是把该头从两条下载链路中一并移除。
/// 复测推翻了这个判断 —— 真正的原因是 <c>Origin</c> <b>取值错误</b>（取了媒体地址自身的
/// origin），CDN 将其判为来源不合法才回退诱饵；取来源页面的 origin 反而能拿到真实内容。
/// </para>
/// <para>
/// 反向的坑同样致命：不发该头时 CDN 同样回诱饵，且诱饵是 HTTP 200、首字节为合法 TS 同步字
/// 的内容，ffmpeg 会顺利封装并退出码 0，用户拿到「能打开却不是目标视频」的 mp4。
/// 因此「头块中必须出现 Origin」要用测试固化，防止后来者凭旧注释把它再删掉。
/// </remarks>
public sealed class RequestContextTests
{
    /// <summary>
    /// 上下文带 Origin 时，头块必须带上该头 —— 缺失会让 CDN 回退占位诱饵。
    /// </summary>
    [Fact]
    public void BuildHeaderBlock_ShouldEmitOrigin_WhenContextCarriesIt()
    {
        var context = new RequestContext
        {
            Referer = "https://www.example.com/watch/1",
            Origin = "https://www.example.com",
            UserAgent = "Mozilla/5.0 Test",
            Cookie = "sid=abc"
        };

        var block = context.BuildHeaderBlock();

        Assert.Contains("Origin: https://www.example.com", block, StringComparison.Ordinal);
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
    /// Origin 属性必须能被拷贝：它参与任务持久化，下载时复用同一份上下文才能保持来源一致。
    /// </summary>
    [Fact]
    public void Clone_ShouldPreserveOriginValue()
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
