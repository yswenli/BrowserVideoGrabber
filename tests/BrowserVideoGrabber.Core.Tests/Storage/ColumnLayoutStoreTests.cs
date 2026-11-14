/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Storage
*文件名： ColumnLayoutStoreTests
*版本号： V1.0.0.0
*唯一标识：4e7a1c2d-9b3f-4a58-8c61-2f0d7e5a9b13
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/11/15 10:00:00
*描述：列布局存储的单元测试，覆盖往返序列化、按键合并与缺失/损坏文件回退。
*
*=================================================
*修改标记
*修改时间：2026/11/15 10:00:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Text;
using BrowserVideoGrabber.Core.Models;
using BrowserVideoGrabber.Infrastructure.Storage;
using BrowserVideoGrabber.Tests.Fakes;

namespace BrowserVideoGrabber.Tests.Storage;

/// <summary>
/// 列布局存储测试：往返、按键合并、缺失/损坏文件回退。
/// </summary>
/// <remarks>
/// 全部使用内存文件系统，不触碰真实磁盘；重点验证「三个列表各自保存互不覆盖」
/// 与「文件损坏时不阻断启动」这两条行为契约。
/// </remarks>
public sealed class ColumnLayoutStoreTests
{
    private const string FilePath = @"C:\data\column-layouts.json";

    [Fact]
    public void Load_WhenFileMissing_ReturnsNull()
    {
        var store = new ColumnLayoutStore(FilePath, new FakeFileSystem());

        Assert.Null(store.Load("sniff"));
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsOrderAndWidth()
    {
        var fileSystem = new FakeFileSystem();
        var store = new ColumnLayoutStore(FilePath, fileSystem);

        store.Save("sniff", new ColumnLayout
        {
            Columns =
            [
                new ColumnLayoutEntry { Name = "名称", Width = 240 },
                new ColumnLayoutEntry { Name = "格式", Width = 80 }
            ]
        });

        var layout = store.Load("sniff");

        Assert.NotNull(layout);
        Assert.Collection(
            layout!.Columns,
            first =>
            {
                Assert.Equal("名称", first.Name);
                Assert.Equal(240, first.Width);
            },
            second =>
            {
                Assert.Equal("格式", second.Name);
                Assert.Equal(80, second.Width);
            });
    }

    [Fact]
    public void Save_DifferentKeys_AreMerged()
    {
        var fileSystem = new FakeFileSystem();
        var store = new ColumnLayoutStore(FilePath, fileSystem);

        store.Save("sniff", new ColumnLayout
        {
            Columns = [new ColumnLayoutEntry { Name = "名称", Width = 200 }]
        });
        store.Save("download-running", new ColumnLayout
        {
            Columns = [new ColumnLayoutEntry { Name = "标题", Width = 300 }]
        });

        Assert.NotNull(store.Load("sniff"));
        Assert.NotNull(store.Load("download-running"));
        Assert.Null(store.Load("download-finished"));
    }

    [Fact]
    public void Load_WhenFileCorrupt_ReturnsNull()
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.SeedFile(FilePath, Encoding.UTF8.GetBytes("{ 这不是合法 JSON"));

        var store = new ColumnLayoutStore(FilePath, fileSystem);

        Assert.Null(store.Load("sniff"));
    }

    [Fact]
    public void Save_WhenExistingFileCorrupt_ReplacesWithValidContent()
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.SeedFile(FilePath, Encoding.UTF8.GetBytes("not json"));

        var store = new ColumnLayoutStore(FilePath, fileSystem);
        store.Save("sniff", new ColumnLayout
        {
            Columns = [new ColumnLayoutEntry { Name = "名称", Width = 200 }]
        });

        Assert.NotNull(store.Load("sniff"));
    }
}