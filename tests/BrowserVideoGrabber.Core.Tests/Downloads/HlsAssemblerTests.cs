/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Downloads
*文件名： HlsAssemblerTests
*版本号： V1.0.0.0
*唯一标识：0448bbe6-1fc6-4945-86da-3470e307e901
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 01:16:00
*描述：HlsAssembler 的单元测试，覆盖多分片按序拼接、单分片与空列表边界。
*
*=================================================
*修改标记
*修改时间：2026/9/13 01:16:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BrowserVideoGrabber.Core.Downloads;
using BrowserVideoGrabber.Tests.Fakes;
using Xunit;

namespace BrowserVideoGrabber.Tests.Downloads;

/// <summary>
/// <see cref="HlsAssembler"/> 的行为验证。
/// </summary>
public sealed class HlsAssemblerTests
{
    /// <summary>
    /// 多个分片应按传入顺序逐字节拼接。
    /// </summary>
    [Fact]
    public async Task ConcatenateAsync_JoinsSegmentsInOrder()
    {
        var fs = new FakeFileSystem();
        fs.SeedFile("a.ts", new byte[] { 1, 2, 3 });
        fs.SeedFile("b.ts", new byte[] { 4, 5 });
        fs.SeedFile("c.ts", new byte[] { 6 });

        var assembler = new HlsAssembler(fs);
        var total = await assembler.ConcatenateAsync(new[] { "a.ts", "b.ts", "c.ts" }, "out.ts", CancellationToken.None);

        Assert.Equal(6, total);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, fs.ReadFile("out.ts"));
    }

    /// <summary>
    /// 单个分片时拼接结果应等于其本身。
    /// </summary>
    [Fact]
    public async Task ConcatenateAsync_SingleSegment_CopiesVerbatim()
    {
        var fs = new FakeFileSystem();
        fs.SeedFile("only.ts", new byte[] { 9, 8, 7 });

        var assembler = new HlsAssembler(fs);
        var total = await assembler.ConcatenateAsync(new List<string> { "only.ts" }, "out.ts", CancellationToken.None);

        Assert.Equal(3, total);
        Assert.Equal(new byte[] { 9, 8, 7 }, fs.ReadFile("out.ts"));
    }

    /// <summary>
    /// 空分片列表不应产生输出文件，且返回 0 字节。
    /// </summary>
    [Fact]
    public async Task ConcatenateAsync_EmptyList_WritesNothing()
    {
        var fs = new FakeFileSystem();

        var assembler = new HlsAssembler(fs);
        var total = await assembler.ConcatenateAsync(new List<string>(), "out.ts", CancellationToken.None);

        Assert.Equal(0, total);
        Assert.False(fs.FileExists("out.ts"));
    }
}
