/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Downloads
*文件名： HttpRequestHeadersTests
*版本号： V1.0.0.0
*唯一标识：0e450709-06f2-418f-ac97-144832ec1c27
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 03:22:00
*描述：HTTP 请求头写入工具的单元测试，验证不再注入 Origin 且鉴权头完整。
*
*=================================================
*修改标记
*修改时间：2026/9/13 03:22:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Models;
using BrowserVideoGrabber.Infrastructure.Downloads;

namespace BrowserVideoGrabber.Tests.Downloads;

/// <summary>
/// <see cref="HttpRequestHeaders"/> 的行为验证。
/// </summary>
/// <remarks>
/// 与 <c>RequestContextTests</c> 同源：ffmpeg 链路与 HttpClient 链路必须同样「不发 Origin」，
/// 只修一条链路会让另一条继续产出占位内容，而故障现象完全相同，极难定位。
/// </remarks>
public sealed class HttpRequestHeadersTests
{
    /// <summary>
    /// 上下文带 Origin 时，请求也不得携带该头。
    /// </summary>
    [Fact]
    public void Apply_ShouldNotSendOrigin_EvenWhenContextCarriesIt()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://cdn.example.com/seg0.jpeg");
        var context = new RequestContext
        {
            Referer = "https://www.example.com/watch/1",
            Origin = "https://www.example.com",
            UserAgent = "Mozilla/5.0 Test",
            Cookie = "sid=abc"
        };

        HttpRequestHeaders.Apply(request, context);

        Assert.False(request.Headers.Contains("Origin"));
        Assert.Equal("https://www.example.com/watch/1", request.Headers.Referrer?.ToString());

        // 必须用 NonValidated 视图读原始值：直接对 User-Agent 调 TryGetValues，
        // .NET 会按产品记号重新解析，"Mozilla/5.0 Test" 会被拆成两段
        Assert.Equal("Mozilla/5.0 Test", request.Headers.NonValidated["User-Agent"].ToString());
        Assert.Equal("sid=abc", request.Headers.NonValidated["Cookie"].ToString());
    }

    /// <summary>
    /// 自定义请求头照常写入。
    /// </summary>
    [Fact]
    public void Apply_ShouldIncludeExtraHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://cdn.example.com/seg0.jpeg");
        var context = new RequestContext();
        context.ExtraHeaders["X-Token"] = "t1";

        HttpRequestHeaders.Apply(request, context);

        Assert.True(request.Headers.TryGetValues("X-Token", out var token));
        Assert.Equal("t1", token!.Single());
    }

    /// <summary>
    /// 非法 Referer（非绝对地址）应被忽略而不是抛异常。
    /// </summary>
    [Fact]
    public void Apply_ShouldIgnoreInvalidReferer()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://cdn.example.com/seg0.jpeg");
        var context = new RequestContext { Referer = "not-a-uri" };

        HttpRequestHeaders.Apply(request, context);

        Assert.Null(request.Headers.Referrer);
    }
}
