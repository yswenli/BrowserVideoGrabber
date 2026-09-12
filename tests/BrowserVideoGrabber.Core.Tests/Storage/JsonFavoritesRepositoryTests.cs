/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Storage
*文件名： JsonFavoritesRepositoryTests
*版本号： V1.0.0.0
*唯一标识：dccee54a-6bdb-4058-961e-40bd8cd4b7ac
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 01:56:00
*描述：地址收藏仓储的单元测试，覆盖增删改查、网址去重与损坏文件回退。
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
using BrowserVideoGrabber.Core.Models;
using BrowserVideoGrabber.Infrastructure.Storage;
using BrowserVideoGrabber.Tests.Fakes;
using Xunit;

namespace BrowserVideoGrabber.Tests.Storage;

/// <summary>
/// 收藏仓储测试。
/// </summary>
/// <remarks>
/// 全部使用内存文件系统，不触碰真实磁盘；重点验证「同一网址不重复收藏」与
/// 「文件损坏时回退空列表而不是抛异常」这两条行为契约。
/// </remarks>
public sealed class JsonFavoritesRepositoryTests
{
    private const string FilePath = @"C:\data\favorites.json";

    [Fact]
    public void Add_ThenLoad_ReturnsPersistedItem()
    {
        var fileSystem = new FakeFileSystem();
        var repository = new JsonFavoritesRepository(FilePath, fileSystem);

        repository.Add(new FavoriteEntry { Title = "测试站", Url = "https://a.test/" });

        var items = repository.Load();
        Assert.Single(items);
        Assert.Equal("测试站", items[0].Title);
        Assert.Equal("https://a.test/", items[0].Url);
    }

    [Fact]
    public void Add_SameUrlTwice_IsRejected()
    {
        var fileSystem = new FakeFileSystem();
        var repository = new JsonFavoritesRepository(FilePath, fileSystem);

        Assert.True(repository.Add(new FavoriteEntry { Title = "A", Url = "https://a.test/" }));

        // 末尾斜杠与大小写差异都应视为同一网址
        Assert.False(repository.Add(new FavoriteEntry { Title = "B", Url = "https://A.test" }));
        Assert.Single(repository.Load());
    }

    [Fact]
    public void Remove_DeletesById()
    {
        var fileSystem = new FakeFileSystem();
        var repository = new JsonFavoritesRepository(FilePath, fileSystem);

        var entry = new FavoriteEntry { Title = "A", Url = "https://a.test/" };
        repository.Add(entry);
        repository.Add(new FavoriteEntry { Title = "B", Url = "https://b.test/" });

        Assert.True(repository.Remove(entry.Id));
        Assert.Single(repository.Load());
        Assert.Equal("B", repository.Load()[0].Title);
    }

    [Fact]
    public void Rename_UpdatesTitleOnly()
    {
        var fileSystem = new FakeFileSystem();
        var repository = new JsonFavoritesRepository(FilePath, fileSystem);

        var entry = new FavoriteEntry { Title = "旧名", Url = "https://a.test/" };
        repository.Add(entry);

        Assert.True(repository.Rename(entry.Id, "新名"));

        var reloaded = repository.Load()[0];
        Assert.Equal("新名", reloaded.Title);
        Assert.Equal("https://a.test/", reloaded.Url);
    }

    [Fact]
    public void Rename_MissingId_ReturnsFalse()
    {
        var fileSystem = new FakeFileSystem();
        var repository = new JsonFavoritesRepository(FilePath, fileSystem);

        Assert.False(repository.Rename(Guid.NewGuid(), "不存在"));
    }

    [Fact]
    public void Load_WhenFileMissing_ReturnsEmpty()
    {
        var repository = new JsonFavoritesRepository(FilePath, new FakeFileSystem());

        Assert.Empty(repository.Load());
    }

    [Fact]
    public void Load_WhenFileCorrupt_ReturnsEmpty()
    {
        var fileSystem = new FakeFileSystem();
        fileSystem.SeedFile(FilePath, Encoding.UTF8.GetBytes("{ 这不是合法 JSON"));

        var repository = new JsonFavoritesRepository(FilePath, fileSystem);

        Assert.Empty(repository.Load());
    }

    [Fact]
    public void ContainsUrl_MatchesIgnoringTrailingSlash()
    {
        var fileSystem = new FakeFileSystem();
        var repository = new JsonFavoritesRepository(FilePath, fileSystem);
        repository.Add(new FavoriteEntry { Title = "A", Url = "https://a.test/" });

        Assert.True(repository.ContainsUrl("https://a.test"));
        Assert.False(repository.ContainsUrl("https://b.test"));
    }
}
