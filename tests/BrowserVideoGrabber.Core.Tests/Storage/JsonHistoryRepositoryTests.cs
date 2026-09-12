/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Storage
*文件名： JsonHistoryRepositoryTests
*版本号： V1.0.0.0
*唯一标识：b3dcbfc9-dd57-48b6-a184-8421d0b16e2b
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 01:56:00
*描述：历史记录仓储的单元测试，覆盖倒序写入、连续同网址去重与容量环形淘汰。
*
*=================================================
*修改标记
*修改时间：2026/9/13 01:56:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Text;
using BrowserVideoGrabber.Infrastructure.Storage;
using BrowserVideoGrabber.Tests.Fakes;
using Xunit;

namespace BrowserVideoGrabber.Tests.Storage;

/// <summary>
/// 历史仓储测试。
/// </summary>
/// <remarks>
/// 历史上限与去重规则都收敛在 <see cref="JsonHistoryRepository.Record"/> 内部，
/// 这里通过小容量（maxEntries: 3）来快速验证淘汰逻辑，而不用真的写入 500 条。
/// </remarks>
public sealed class JsonHistoryRepositoryTests
{
    private const string FilePath = @"C:\data\history.json";

    [Fact]
    public void Record_StoresNewestFirst()
    {
        var repository = new JsonHistoryRepository(FilePath, maxEntries: 10, new FakeFileSystem());

        repository.Record("第一页", "https://a.test/1");
        repository.Record("第二页", "https://a.test/2");

        var items = repository.Load();
        Assert.Equal(2, items.Count);
        Assert.Equal("https://a.test/2", items[0].Url);
        Assert.Equal("https://a.test/1", items[1].Url);
    }

    [Fact]
    public void Record_SameUrlConsecutively_UpdatesInsteadOfAdding()
    {
        var repository = new JsonHistoryRepository(FilePath, maxEntries: 10, new FakeFileSystem());

        repository.Record("首页", "https://a.test/");
        repository.Record("首页（刷新后）", "https://a.test/");

        // 连续刷新同一页面不应刷出多条历史
        var items = repository.Load();
        Assert.Single(items);
        Assert.Equal("首页（刷新后）", items[0].Title);
    }

    [Fact]
    public void Record_DifferentUrlAfterSameUrl_AddsNewEntry()
    {
        var repository = new JsonHistoryRepository(FilePath, maxEntries: 10, new FakeFileSystem());

        repository.Record("A", "https://a.test/");
        repository.Record("B", "https://b.test/");
        repository.Record("A again", "https://a.test/");

        // 只有「与最新一条相同」才合并；隔开的重复访问应各自成条
        Assert.Equal(3, repository.Load().Count);
    }

    [Fact]
    public void Record_ExceedingCapacity_TrimsOldest()
    {
        var repository = new JsonHistoryRepository(FilePath, maxEntries: 3, new FakeFileSystem());

        for (var index = 1; index <= 5; index++)
        {
            repository.Record($"页{index}", $"https://a.test/{index}");
        }

        var items = repository.Load();
        Assert.Equal(3, items.Count);
        Assert.Equal("https://a.test/5", items[0].Url);
        Assert.Equal("https://a.test/4", items[1].Url);
        Assert.Equal("https://a.test/3", items[2].Url);
    }

    [Fact]
    public void Record_BlankUrl_IsIgnored()
    {
        var repository = new JsonHistoryRepository(FilePath, maxEntries: 10, new FakeFileSystem());

        repository.Record("空地址", "   ");

        Assert.Empty(repository.Load());
    }

    [Fact]
    public void Remove_DeletesById()
    {
        var repository = new JsonHistoryRepository(FilePath, maxEntries: 10, new FakeFileSystem());
        repository.Record("A", "https://a.test/1");
        repository.Record("B", "https://a.test/2");

        var targetId = repository.Load()[1].Id;

        Assert.True(repository.Remove(targetId));
        Assert.Single(repository.Load());
        Assert.Equal("https://a.test/2", repository.Load()[0].Url);
    }

    [Fact]
    public void Clear_EmptiesHistory()
    {
        var repository = new JsonHistoryRepository(FilePath, maxEntries: 10, new FakeFileSystem());
        repository.Record("A", "https://a.test/1");

        repository.Clear();

        Assert.Empty(repository.Load());
    }

    [Fact]
    public void Load_WhenFileCorrupt_ReturnsEmpty()
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.SeedFile(FilePath, Encoding.UTF8.GetBytes("not json at all"));

        var repository = new JsonHistoryRepository(FilePath, maxEntries: 10, fileSystem);

        Assert.Empty(repository.Load());
    }

    [Fact]
    public void Constructor_NonPositiveCapacity_FallsBackToDefault()
    {
        var repository = new JsonHistoryRepository(FilePath, maxEntries: 0, new FakeFileSystem());

        Assert.Equal(500, repository.MaxEntries);
    }
}
