/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Infrastructure.Storage
*文件名： ColumnLayoutStore
*版本号： V1.0.0.0
*唯一标识：c3d9e2a7-5f18-4b62-9a4e-7d1b8c2f6e35
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/11/15 10:00:00
*描述：列布局 JSON 存储，按列表标识读写列顺序与列宽，采用临时文件加改名保证写入原子性。
*
*=================================================
*修改标记
*修改时间：2026/11/15 10:00:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Text.Json;
using BrowserVideoGrabber.Core.Abstractions;
using BrowserVideoGrabber.Core.Json;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Infrastructure.Storage;

/// <summary>
/// 列布局 JSON 存储。
/// </summary>
/// <remarks>
/// <para>
/// 文件形态为 <c>Dictionary&lt;string, ColumnLayout&gt;</c>，键是列表标识
/// （<c>sniff</c> / <c>download-running</c> / <c>download-finished</c>）。
/// </para>
/// <para>
/// 按「单列表」读写：<see cref="Save"/> 先读整份文件、只替换目标键、再原子写回，
/// 因此三个列表各自保存时不会互相覆盖。读取失败返回 null / 空字典，永不阻断启动。
/// </para>
/// </remarks>
public sealed class ColumnLayoutStore
{
    private static readonly JsonSerializerOptions SerializerOptions = JsonModelsContext.CreateOptions();

    private readonly IFileSystem _fileSystem;
    private readonly string _filePath;

    /// <summary>
    /// 初始化列布局存储。
    /// </summary>
    /// <param name="filePath">列布局文件完整路径。</param>
    /// <param name="fileSystem">文件系统抽象。为空时使用真实磁盘实现。</param>
    public ColumnLayoutStore(string filePath, IFileSystem? fileSystem = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _filePath = filePath;
        _fileSystem = fileSystem ?? PhysicalFileSystem.Instance;
    }

    /// <summary>列布局文件路径。</summary>
    public string FilePath => _filePath;

    /// <summary>
    /// 读取指定列表的列布局。
    /// </summary>
    /// <param name="key">列表标识（如 <c>sniff</c>）。</param>
    /// <returns>对应的列布局；文件不存在、损坏或该键未保存时返回 null。</returns>
    public ColumnLayout? Load(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return ReadAll().TryGetValue(key, out var layout) ? layout : null;
    }

    /// <summary>
    /// 保存指定列表的列布局。
    /// </summary>
    /// <param name="key">列表标识（如 <c>sniff</c>）。</param>
    /// <param name="layout">该列表的列布局。</param>
    /// <remarks>
    /// 只替换目标键，其余键保持原样；写入失败（磁盘满、权限不足）时静默忽略，
    /// 避免列宽记忆这种非关键功能阻断界面操作。
    /// </remarks>
    public void Save(string key, ColumnLayout layout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(layout);

        var all = ReadAll();
        all[key] = layout;

        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                _fileSystem.CreateDirectory(directory);
            }

            var temporaryPath = _filePath + ".tmp";

            using (var stream = _fileSystem.OpenWrite(temporaryPath, append: false))
            {
                JsonSerializer.Serialize(stream, all, SerializerOptions);
            }

            _fileSystem.MoveFile(temporaryPath, _filePath, overwrite: true);
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// 读取整份文件为「列表标识 → 列布局」字典。
    /// </summary>
    /// <returns>字典；文件不存在或损坏时返回空字典。</returns>
    private Dictionary<string, ColumnLayout> ReadAll()
    {
        if (!_fileSystem.FileExists(_filePath))
        {
            return new Dictionary<string, ColumnLayout>(StringComparer.Ordinal);
        }

        try
        {
            using var stream = _fileSystem.OpenRead(_filePath);
            var all = JsonSerializer.Deserialize<Dictionary<string, ColumnLayout>>(stream, SerializerOptions);

            return all is null
                ? new Dictionary<string, ColumnLayout>(StringComparer.Ordinal)
                : new Dictionary<string, ColumnLayout>(all, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, ColumnLayout>(StringComparer.Ordinal);
        }
        catch (IOException)
        {
            return new Dictionary<string, ColumnLayout>(StringComparer.Ordinal);
        }
        catch (UnauthorizedAccessException)
        {
            return new Dictionary<string, ColumnLayout>(StringComparer.Ordinal);
        }
    }
}