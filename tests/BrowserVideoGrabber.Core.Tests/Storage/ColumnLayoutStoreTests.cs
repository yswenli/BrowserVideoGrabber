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
using System.Text.Json;
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

        // 只断言 NotNull 会让「键都在但内容被写串」蒙混过关，这里核对真实内容
        Assert.Equal(200, Assert.Single(store.Load("sniff")!.Columns).Width);
        Assert.Equal(300, Assert.Single(store.Load("download-running")!.Columns).Width);

        // 磁盘上必须是合法 JSON，且两个键同时存在
        using var document = JsonDocument.Parse(fileSystem.ReadFile(FilePath));
        Assert.True(document.RootElement.TryGetProperty("sniff", out _));
        Assert.True(document.RootElement.TryGetProperty("download-running", out _));

        // 原子写不应留下临时文件
        Assert.DoesNotContain(fileSystem.FilePaths, path => path.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void Save_WhenReadFails_DoesNotClearOtherKeys()
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

        // 模拟文件被独占：读不出来时不能把空字典写回去，否则另外两个列表的布局会被清空
        fileSystem.OpenReadException = new IOException("文件被另一进程占用");

        store.Save("download-finished", new ColumnLayout
        {
            Columns = [new ColumnLayoutEntry { Name = "标题", Width = 400 }]
        });

        fileSystem.OpenReadException = null;

        Assert.NotNull(store.Load("sniff"));
        Assert.NotNull(store.Load("download-running"));
        Assert.Null(store.Load("download-finished"));
    }

    [Fact]
    public void Save_WhenMoveFails_LeavesDestinationUntouchedAndRemovesTemporaryFile()
    {
        var fileSystem = new FakeFileSystem();
        var store = new ColumnLayoutStore(FilePath, fileSystem);

        store.Save("sniff", new ColumnLayout
        {
            Columns = [new ColumnLayoutEntry { Name = "名称", Width = 200 }]
        });

        fileSystem.MoveFileException = new IOException("改名失败");

        store.Save("download-running", new ColumnLayout
        {
            Columns = [new ColumnLayoutEntry { Name = "标题", Width = 300 }]
        });

        fileSystem.MoveFileException = null;

        // 旧内容保持不变，新键未写入
        Assert.Equal(200, Assert.Single(store.Load("sniff")!.Columns).Width);
        Assert.Null(store.Load("download-running"));

        // 失败路径也必须清理临时文件
        Assert.DoesNotContain(fileSystem.FilePaths, path => path.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void Save_WhenDirectoryCreationFails_DoesNotThrow()
    {
        var fileSystem = new FakeFileSystem
        {
            CreateDirectoryException = new UnauthorizedAccessException("无权限")
        };
        var store = new ColumnLayoutStore(FilePath, fileSystem);

        store.Save("sniff", new ColumnLayout
        {
            Columns = [new ColumnLayoutEntry { Name = "名称", Width = 200 }]
        });

        Assert.Null(store.Load("sniff"));
    }

    [Fact]
    public void Load_IsCaseSensitiveForKey()
    {
        var fileSystem = new FakeFileSystem();
        var store = new ColumnLayoutStore(FilePath, fileSystem);

        store.Save("sniff", new ColumnLayout
        {
            Columns = [new ColumnLayoutEntry { Name = "名称", Width = 200 }]
        });

        Assert.NotNull(store.Load("sniff"));
        Assert.Null(store.Load("SNIFF"));
    }

    [Fact]
    public void Load_WhenValueIsNull_ReturnsNull()
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.SeedFile(FilePath, Encoding.UTF8.GetBytes("""{"sniff":null}"""));

        var store = new ColumnLayoutStore(FilePath, fileSystem);

        Assert.Null(store.Load("sniff"));
    }

    [Fact]
    public void Load_NormalizesNullColumns()
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.SeedFile(FilePath, Encoding.UTF8.GetBytes("""{"sniff":{"Columns":null}}"""));

        var store = new ColumnLayoutStore(FilePath, fileSystem);

        var layout = store.Load("sniff");

        Assert.NotNull(layout);
        Assert.Empty(layout!.Columns);
    }

    [Fact]
    public void Save_WhenFileIsEmpty_ReplacesWithValidContent()
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.SeedFile(FilePath, Array.Empty<byte>());

        var store = new ColumnLayoutStore(FilePath, fileSystem);
        store.Save("sniff", new ColumnLayout
        {
            Columns = [new ColumnLayoutEntry { Name = "名称", Width = 200 }]
        });

        Assert.Equal(200, Assert.Single(store.Load("sniff")!.Columns).Width);
        using var document = JsonDocument.Parse(fileSystem.ReadFile(FilePath));
        Assert.True(document.RootElement.TryGetProperty("sniff", out _));
    }

    [Fact]
    public void Save_WithRelativePathWithoutDirectory_Succeeds()
    {
        var fileSystem = new FakeFileSystem();
        var store = new ColumnLayoutStore("column-layouts.json", fileSystem);

        store.Save("sniff", new ColumnLayout
        {
            Columns = [new ColumnLayoutEntry { Name = "名称", Width = 200 }]
        });

        Assert.Equal(200, Assert.Single(store.Load("sniff")!.Columns).Width);
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

        Assert.Equal(200, Assert.Single(store.Load("sniff")!.Columns).Width);
        using var document = JsonDocument.Parse(fileSystem.ReadFile(FilePath));
        Assert.True(document.RootElement.TryGetProperty("sniff", out _));
    }
}