/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Infrastructure.Storage
*文件名： JsonHistoryRepository
*版本号： V1.0.0.0
*唯一标识：374ae092-07ff-4d8e-b2da-a92addd5360c
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 01:45:00
*描述：基于 JSON 文件的历史记录仓储，连续同网址去重 + 容量上限环形淘汰 + 原子写入。
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
/// 基于 JSON 文件的历史记录仓储。
/// </summary>
/// <remarks>
/// <para>
/// 列表始终以「访问时间倒序」存储（索引 0 最新）。<see cref="Record"/> 的两条规则：
/// <list type="bullet">
///   <item><description>与最新一条相同网址 → 只更新其访问时间与标题，避免刷新刷爆历史。</description></item>
///   <item><description>超出 <see cref="MaxEntries"/> 时从尾部（最旧）淘汰。</description></item>
/// </list>
/// </para>
/// <para>读取失败一律返回空列表，保证程序可启动。</para>
/// </remarks>
public sealed class JsonHistoryRepository : IHistoryRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = JsonModelsContext.CreateOptions();

    private readonly IFileSystem _fileSystem;
    private readonly string _filePath;

    /// <summary>
    /// 初始化历史仓储。
    /// </summary>
    /// <param name="filePath">历史文件完整路径。</param>
    /// <param name="maxEntries">容量上限，默认 500；超出环形淘汰最旧。</param>
    /// <param name="fileSystem">文件系统抽象。为空时使用真实磁盘实现。</param>
    public JsonHistoryRepository(string filePath, int maxEntries = 500, IFileSystem? fileSystem = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _filePath = filePath;
        _fileSystem = fileSystem ?? PhysicalFileSystem.Instance;
        MaxEntries = maxEntries < 1 ? 500 : maxEntries;
    }

    /// <summary>历史容量上限。</summary>
    public int MaxEntries { get; }

    /// <summary>历史文件路径。</summary>
    public string FilePath => _filePath;

    /// <summary>
    /// 获取默认历史文件路径：<c>%LOCALAPPDATA%\BrowserVideoGrabber\history.json</c>。
    /// </summary>
    /// <returns>默认文件路径。</returns>
    public static string GetDefaultFilePath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrowserVideoGrabber",
            "history.json");

    /// <inheritdoc />
    public IReadOnlyList<HistoryEntry> Load()
    {
        if (!_fileSystem.FileExists(_filePath))
        {
            return Array.Empty<HistoryEntry>();
        }

        try
        {
            using var stream = _fileSystem.OpenRead(_filePath);
            var items = JsonSerializer.Deserialize<List<HistoryEntry>>(stream, SerializerOptions);
            return items ?? new List<HistoryEntry>();
        }
        catch (JsonException)
        {
            return Array.Empty<HistoryEntry>();
        }
        catch (IOException)
        {
            return Array.Empty<HistoryEntry>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<HistoryEntry>();
        }
    }

    /// <inheritdoc />
    public void Save(IReadOnlyList<HistoryEntry> items)
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
    public void Record(string title, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var items = Load().ToList();

        // 与最新一条相同网址：只刷新时间，避免同一页面反复刷新刷爆历史
        if (items.Count > 0 && SameUrl(items[0].Url, url))
        {
            items[0].VisitedAt = DateTimeOffset.Now;
            items[0].Title = title;
            Save(items);
            return;
        }

        items.Insert(0, new HistoryEntry
        {
            Title = title,
            Url = url,
            VisitedAt = DateTimeOffset.Now
        });

        // 环形淘汰：超过上限时从尾部（最旧）删除
        while (items.Count > MaxEntries)
        {
            items.RemoveAt(items.Count - 1);
        }

        Save(items);
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
    public void Clear()
    {
        if (!_fileSystem.FileExists(_filePath))
        {
            return;
        }

        Save(Array.Empty<HistoryEntry>());
    }

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

