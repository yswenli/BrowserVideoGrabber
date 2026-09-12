/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Downloads
*文件名： SegmentContentValidatorTests
*版本号： V1.0.0.0
*唯一标识：f2a0c40b-d635-47e6-81b3-69d715c21998
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 03:52:00
*描述：分片内容校验器的单元测试，覆盖占位内容识别、重复指纹与整体可用性判定。
*
*=================================================
*修改标记
*修改时间：2026/9/13 03:52:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Text;
using BrowserVideoGrabber.Core.Downloads;

namespace BrowserVideoGrabber.Tests.Downloads;

/// <summary>
/// <see cref="SegmentContentValidator"/> 的行为验证。
/// </summary>
/// <remarks>
/// 本组用例直接对应一组实测事实：被 CDN 污染的响应是
/// <b>HTTP 200 + <c>Content-Type: image/jpeg</c> + 56024 字节 + 首字节为合法 TS 同步字 <c>0x47</c></b>
/// 的一段 8 秒合法 TS。也就是说「状态码」「同步字」单独看都会放行，
/// 唯一稳定可靠的特征是<b>多个不同地址返回了逐字节相同的内容</b>。
/// 因此这里的断言必须把「重复指纹」当成主判据来验证。
/// </remarks>
public sealed class SegmentContentValidatorTests
{
    /// <summary>合法的 MPEG-TS 分片首字节固定为 0x47。</summary>
    private static readonly byte[] TsHead = [0x47, 0x40, 0x11, 0x10];

    /// <summary>
    /// 正常 TS 分片应被接受。
    /// </summary>
    [Fact]
    public void Inspect_ShouldAccept_ForNormalTsSegment()
    {
        var validator = new SegmentContentValidator();

        var verdict = validator.Inspect("video/mp2t", 586560, TsHead);

        Assert.True(verdict.Usable);
        Assert.Null(verdict.Reason);
    }

    /// <summary>
    /// 图片内容类型是高置信度的污染特征：视频分片不可能声明成 image/*。
    /// </summary>
    [Fact]
    public void Inspect_ShouldReject_ForImageContentType()
    {
        var validator = new SegmentContentValidator();

        var verdict = validator.Inspect("image/jpeg", 56024, TsHead);

        Assert.False(verdict.Usable);
        Assert.NotNull(verdict.Reason);
    }

    /// <summary>
    /// 文本 / JSON 内容类型通常意味着站点返回了错误页或限流提示。
    /// </summary>
    /// <param name="contentType">待验证的内容类型。</param>
    [Theory]
    [InlineData("text/html")]
    [InlineData("application/json")]
    [InlineData("text/plain; charset=utf-8")]
    public void Inspect_ShouldReject_ForTextualContentType(string contentType)
    {
        var validator = new SegmentContentValidator();

        var verdict = validator.Inspect(contentType, 128, TsHead);

        Assert.False(verdict.Usable);
    }

    /// <summary>
    /// 首字节不是 TS 同步字时说明这不是一段 MPEG-TS。
    /// </summary>
    [Fact]
    public void Inspect_ShouldReject_WhenTsSyncByteMissing()
    {
        var validator = new SegmentContentValidator();
        byte[] head = [0x89, 0x50, 0x4E, 0x47];

        var verdict = validator.Inspect("video/mp2t", 4096, head);

        Assert.False(verdict.Usable);
    }

    /// <summary>
    /// fMP4 分片不是 TS，期望同步字的开关必须可关，否则会把正常内容误判为污染。
    /// </summary>
    [Fact]
    public void Inspect_ShouldSkipSyncByteCheck_WhenNotExpected()
    {
        var validator = new SegmentContentValidator();
        byte[] head = [0x00, 0x00, 0x00, 0x18];

        var verdict = validator.Inspect("video/mp4", 4096, head, expectTsSyncByte: false);

        Assert.True(verdict.Usable);
    }

    /// <summary>
    /// 空内容必然无效。
    /// </summary>
    [Fact]
    public void Inspect_ShouldReject_ForEmptyContent()
    {
        var validator = new SegmentContentValidator();

        var verdict = validator.Inspect("video/mp2t", 0, ReadOnlySpan<byte>.Empty);

        Assert.False(verdict.Usable);
    }

    /// <summary>
    /// 同一份内容出现在不同分片上，是占位污染最可靠的特征。
    /// </summary>
    [Fact]
    public void RegisterFingerprint_ShouldDetectDuplicate()
    {
        var validator = new SegmentContentValidator();
        var fingerprint = SegmentContentValidator.ComputeFingerprint(TsHead, TsHead, 56024);

        Assert.False(validator.IsDuplicate(fingerprint));

        validator.Remember(fingerprint);

        Assert.True(validator.IsDuplicate(fingerprint));
    }

    /// <summary>
    /// 内容不同的分片不得被判为重复，否则会把正常视频大面积误删。
    /// </summary>
    [Fact]
    public void RegisterFingerprint_ShouldNotFlagDistinctContent()
    {
        var validator = new SegmentContentValidator();
        byte[] otherHead = [0x47, 0x41, 0x22, 0x20];

        validator.Remember(SegmentContentValidator.ComputeFingerprint(TsHead, TsHead, 56024));

        Assert.False(validator.IsDuplicate(SegmentContentValidator.ComputeFingerprint(otherHead, otherHead, 586560)));
    }

    /// <summary>
    /// 指纹必须同时受「长度」与「首尾字节」影响，否则体积相近的分片会被误判为同一份内容。
    /// </summary>
    [Fact]
    public void ComputeFingerprint_ShouldDependOnLengthAndBoundaryBytes()
    {
        var baseline = SegmentContentValidator.ComputeFingerprint(TsHead, TsHead, 1000);

        Assert.NotEqual(baseline, SegmentContentValidator.ComputeFingerprint(TsHead, TsHead, 1001));
        Assert.NotEqual(baseline, SegmentContentValidator.ComputeFingerprint([0x47, 0x41], TsHead, 1000));
    }

    /// <summary>
    /// 指纹必须是定长摘要（SHA-256 的十六进制形式），而不是原始字节的回显：
    /// 后者会让每个分片的头部内容常驻内存，且失去「同摘要即同内容」的判定能力。
    /// </summary>
    [Fact]
    public void ComputeFingerprint_ShouldBeStableHashSummary()
    {
        var payload = Encoding.ASCII.GetBytes("AAAA");

        var fingerprint = SegmentContentValidator.ComputeFingerprint(payload, payload, payload.Length);

        Assert.Equal(64, fingerprint.Length);
        Assert.All(fingerprint, c => Assert.True(Uri.IsHexDigit(c)));

        // 同一输入必须稳定得到同一摘要，否则重复检测会完全失效
        Assert.Equal(fingerprint, SegmentContentValidator.ComputeFingerprint(payload, payload, payload.Length));
    }

    /// <summary>
    /// 可用分片占比过低（几乎全是占位）时应判定为「不值得保留」，
    /// 避免把 1331 片里仅剩的 2 片拼成一个 10 秒的 mp4 冒充成功。
    /// </summary>
    /// <param name="total">分片总数。</param>
    /// <param name="usable">可用分片数。</param>
    /// <param name="expected">期望结论。</param>
    [Theory]
    [InlineData(100, 50, true)]
    [InlineData(100, 10, true)]
    [InlineData(100, 9, false)]
    [InlineData(100, 0, false)]
    [InlineData(10, 0, false)]
    [InlineData(0, 0, false)]
    public void IsWorthKeeping_ShouldApplyUsableRatioThreshold(int total, int usable, bool expected)
    {
        var validator = new SegmentContentValidator();

        Assert.Equal(expected, validator.IsWorthKeeping(total, usable));
    }

    /// <summary>
    /// 阈值必须可调：站点差异很大时，硬编码比例会把可用的下载判成失败。
    /// </summary>
    [Fact]
    public void IsWorthKeeping_ShouldHonorConfiguredRatio()
    {
        var validator = new SegmentContentValidator(new SegmentValidatorOptions { MinimumUsableRatio = 0.5 });

        Assert.False(validator.IsWorthKeeping(100, 40));
        Assert.True(validator.IsWorthKeeping(100, 50));
    }
}
