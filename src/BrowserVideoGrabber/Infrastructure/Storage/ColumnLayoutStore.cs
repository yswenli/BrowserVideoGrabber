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
/// 因此三个列表<b>顺序</b>保存时不会互相覆盖。
/// </para>
/// <para>
/// 读取失败分两类处理：文件不存在或 JSON 损坏视为「没有旧数据」，允许下次保存重建；
/// 文件存在但 IO / 权限读取失败时，<see cref="Save"/> 会<b>放弃本次写入</b> ——
/// 否则用空字典覆盖会把其它列表的布局一起清空。列布局属于可再生的界面偏好，
/// 丢一次新保存远好于清空全部。
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

        if (!TryReadAll(out var all)
            || !all.TryGetValue(key, out var layout)
            || layout is null)
        {
            return null;
        }

        // 手改文件可能留下 "Columns": null；在存储层归一化，避免每个消费者各自防御
        layout.Columns ??= [];
        return layout;
    }

    /// <summary>
    /// 保存指定列表的列布局。
    /// </summary>
    /// <param name="key">列表标识（如 <c>sniff</c>）。</param>
    /// <param name="layout">该列表的列布局。</param>
    /// <remarks>
    /// <para>只替换目标键，其余键保持原样；写入失败（磁盘满、权限不足）时静默忽略，
    /// 避免列宽记忆这种非关键功能阻断界面操作。</para>
    /// <para>
    /// 若旧文件存在却读不出来（被占用、权限变化），本方法直接放弃写入：
    /// 此时把它当成空文件覆盖会静默清空其它列表的布局。
    /// </para>
    /// </remarks>
    public void Save(string key, ColumnLayout layout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(layout);

        if (!TryReadAll(out var all))
        {
            return;
        }

        all[key] = layout;

        // 临时文件名必须唯一：固定 ".tmp" 在并发保存时会让先完成的一方
        // 把另一方的临时文件移走，导致「Save 正常返回但内容丢失」
        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                _fileSystem.CreateDirectory(directory);
            }

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
        finally
        {
            // 改名成功后源文件已不存在，这里的删除是幂等的；
            // 改名失败时用它清理残留，避免用户目录堆积 .tmp
            try
            {
                _fileSystem.DeleteFile(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// 读取整份文件为「列表标识 → 列布局」字典。
    /// </summary>
    /// <param name="all">读取结果；失败时为空字典。</param>
    /// <returns>
    /// 文件不存在、内容损坏或读取成功时返回 true（前两者给出空字典，允许后续重建）；
    /// 文件存在但 IO / 权限读取失败时返回 false，调用方不得以空字典覆盖。
    /// </returns>
    private bool TryReadAll(out Dictionary<string, ColumnLayout> all)
    {
        all = CreateEmpty();

        if (!_fileSystem.FileExists(_filePath))
        {
            return true;
        }

        try
        {
            using var stream = _fileSystem.OpenRead(_filePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, ColumnLayout>>(stream, SerializerOptions);

            if (loaded is not null)
            {
                all = new Dictionary<string, ColumnLayout>(loaded, StringComparer.Ordinal);
            }

            return true;
        }
        catch (JsonException)
        {
            // 内容损坏属于「没有可用旧数据」，允许下次保存覆盖修复
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static Dictionary<string, ColumnLayout> CreateEmpty()
        => new(StringComparer.Ordinal);
}
