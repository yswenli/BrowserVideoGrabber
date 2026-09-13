/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Infrastructure.Storage
*文件名： JsonTaskRepository
*版本号： V1.0.0.0
*唯一标识：b8faeec3-99b5-4e80-8c2f-ecccc3c44d29
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 23:30:00
*描述：基于 JSON 文件的下载任务持久化实现，采用临时文件加改名的方式保证写入原子性。
*
*=================================================
*修改标记
*修改时间：2026/9/12 23:30:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Text.Json;using BrowserVideoGrabber.Core.Abstractions;
using BrowserVideoGrabber.Core.Json;using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Infrastructure.Storage;

/// <summary>
/// 基于 JSON 文件的下载任务仓储。
/// </summary>
/// <remarks>
/// <para>
/// <b>原子写入</b>：先把内容写入 <c>tasks.json.tmp</c>，再用改名操作替换正式文件。
/// 这样即使程序在写入过程中被强制结束，正式文件也始终是一份完整可用的旧快照，
/// 不会出现「任务列表变成半截 JSON」导致整个列表丢失的情况。
/// </para>
/// <para>
/// <b>容错读取</b>：文件不存在、内容损坏、权限不足等情况一律返回空列表而非抛异常。
/// 任务列表只是便利功能，不应因为一份坏掉的缓存文件而让程序无法启动。
/// </para>
/// </remarks>
public sealed class JsonTaskRepository : ITaskRepository
{
    /// <summary>序列化配置。</summary>
    /// <remarks>
    /// 采用非严格转义编码器，使中文标题与 URL 在文件中保持可读，
    /// 便于用户直接打开文件排查问题。
    /// </remarks>
    private static readonly JsonSerializerOptions SerializerOptions = JsonModelsContext.CreateOptions();

    private readonly IFileSystem _fileSystem;
    private readonly string _filePath;

    /// <summary>
    /// 初始化仓储。
    /// </summary>
    /// <param name="filePath">任务文件完整路径。</param>
    /// <param name="fileSystem">文件系统抽象。为空时使用真实磁盘实现。</param>
    public JsonTaskRepository(string filePath, IFileSystem? fileSystem = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _filePath = filePath;
        _fileSystem = fileSystem ?? PhysicalFileSystem.Instance;
    }

    /// <summary>任务文件路径。</summary>
    public string FilePath => _filePath;

    /// <summary>
    /// 获取默认任务文件路径：<c>%LOCALAPPDATA%\BrowserVideoGrabber\tasks.json</c>。
    /// </summary>
    /// <returns>默认文件路径。</returns>
    public static string GetDefaultFilePath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrowserVideoGrabber",
            "tasks.json");

    /// <inheritdoc />
    public async Task<IReadOnlyList<DownloadTask>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!_fileSystem.FileExists(_filePath))
        {
            return Array.Empty<DownloadTask>();
        }

        try
        {
            await using var stream = _fileSystem.OpenRead(_filePath);

            var tasks = await JsonSerializer
                .DeserializeAsync<List<DownloadTask>>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            return tasks ?? new List<DownloadTask>();
        }
        catch (JsonException)
        {
            // 文件损坏：返回空列表，用户下次保存时会自然覆盖掉坏文件
            return Array.Empty<DownloadTask>();
        }
        catch (IOException)
        {
            return Array.Empty<DownloadTask>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<DownloadTask>();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        IReadOnlyCollection<DownloadTask> tasks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _fileSystem.CreateDirectory(directory);
        }

        var temporaryPath = _filePath + ".tmp";

        await using (var stream = _fileSystem.OpenWrite(temporaryPath, append: false))
        {
            await JsonSerializer
                .SerializeAsync(stream, tasks.ToList(), SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        // 改名替换具备原子性（同卷内），保证正式文件永远是完整快照
        _fileSystem.MoveFile(temporaryPath, _filePath, overwrite: true);
    }
}

