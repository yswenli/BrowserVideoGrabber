/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Infrastructure.Storage
*文件名： JsonFavoritesRepository
*版本号： V1.0.0.0
*唯一标识：9ecf61be-4f15-45f4-8305-9331f1048ded
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 01:45:00
*描述：基于 JSON 文件的地址收藏仓储，采用临时文件加改名保证写入原子性。
*
*=================================================
*修改标记
*修改时间：2026/9/13 01:45:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Text.Json;using BrowserVideoGrabber.Core.Abstractions;
using BrowserVideoGrabber.Core.Json;using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Infrastructure.Storage;

/// <summary>
/// 基于 JSON 文件的地址收藏仓储。
/// </summary>
/// <remarks>
/// 与 <see cref="JsonTaskRepository"/> / <see cref="JsonAppSettingsStore"/> 采用相同的原子写入策略：
/// 读取失败一律返回空列表，保证任何情况下程序可启动。
/// </remarks>
public sealed class JsonFavoritesRepository : IFavoritesRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = JsonModelsContext.CreateOptions();

    private readonly IFileSystem _fileSystem;
    private readonly string _filePath;

    /// <summary>
    /// 初始化收藏仓储。
    /// </summary>
    /// <param name="filePath">收藏文件完整路径。</param>
    /// <param name="fileSystem">文件系统抽象。为空时使用真实磁盘实现。</param>
    public JsonFavoritesRepository(string filePath, IFileSystem? fileSystem = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _filePath = filePath;
        _fileSystem = fileSystem ?? PhysicalFileSystem.Instance;
    }

    /// <summary>收藏文件路径。</summary>
    public string FilePath => _filePath;

    /// <summary>
    /// 获取默认收藏文件路径：<c>%LOCALAPPDATA%\BrowserVideoGrabber\favorites.json</c>。
    /// </summary>
    /// <returns>默认文件路径。</returns>
    public static string GetDefaultFilePath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrowserVideoGrabber",
            "favorites.json");

    /// <inheritdoc />
    public IReadOnlyList<FavoriteEntry> Load()
    {
        if (!_fileSystem.FileExists(_filePath))
        {
            return Array.Empty<FavoriteEntry>();
        }

        try
        {
            using var stream = _fileSystem.OpenRead(_filePath);
            var items = JsonSerializer.Deserialize<List<FavoriteEntry>>(stream, SerializerOptions);
            return items ?? new List<FavoriteEntry>();
        }
        catch (JsonException)
        {
            return Array.Empty<FavoriteEntry>();
        }
        catch (IOException)
        {
            return Array.Empty<FavoriteEntry>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<FavoriteEntry>();
        }
    }

    /// <inheritdoc />
    public void Save(IReadOnlyList<FavoriteEntry> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _fileSystem.CreateDirectory(directory);
        }

        var temporaryPath = _filePath + ".tmp";

        using (var stream = _fileSystem.OpenWrite(temporaryPath, append: false))
        {
            JsonSerializer.Serialize(stream, items.ToList(), SerializerOptions);
        }

        _fileSystem.MoveFile(temporaryPath, _filePath, overwrite: true);
    }

    /// <inheritdoc />
    public bool Add(FavoriteEntry item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var items = Load().ToList();

        if (items.Any(x => SameUrl(x.Url, item.Url)))
        {
            return false;
        }

        items.Add(item);
        Save(items);
        return true;
    }

    /// <inheritdoc />
    public bool Remove(Guid id)
    {
        var items = Load().ToList();
        var removed = items.RemoveAll(x => x.Id == id) > 0;

        if (removed)
        {
            Save(items);
        }

        return removed;
    }

    /// <inheritdoc />
    public bool Rename(Guid id, string title)
    {
        var items = Load().ToList();
        var target = items.Find(x => x.Id == id);

        if (target is null)
        {
            return false;
        }

        target.Title = title;
        Save(items);
        return true;
    }

    /// <inheritdoc />
    public bool ContainsUrl(string url)
        => Load().Any(x => SameUrl(x.Url, url));

    /// <summary>
    /// 判断两个网址等价：忽略大小写与末尾斜杠。
    /// </summary>
    /// <param name="left">网址一。</param>
    /// <param name="right">网址二。</param>
    /// <returns>等价返回 true。</returns>
    private static bool SameUrl(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        var a = left.TrimEnd('/');
        var b = right.TrimEnd('/');
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}

