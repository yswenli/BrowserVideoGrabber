/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Infrastructure.Storage
*文件名： JsonAppSettingsStore
*版本号： V1.0.0.0
*唯一标识：798f4767-5249-4bf2-95ee-fe0803975028
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 23:32:00
*描述：基于 JSON 文件的应用设置持久化实现。
*
*=================================================
*修改标记
*修改时间：2026/9/12 23:32:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Text.Encodings.Web;
using System.Text.Json;
using BrowserVideoGrabber.Core.Abstractions;
using BrowserVideoGrabber.Core.Configuration;

namespace BrowserVideoGrabber.Infrastructure.Storage;

/// <summary>
/// 基于 JSON 文件的应用设置仓储。
/// </summary>
/// <remarks>
/// 与 <see cref="JsonTaskRepository"/> 采用相同的原子写入策略；
/// 读取失败时返回默认设置而非抛异常，保证程序在任何情况下都能启动。
/// </remarks>
public sealed class JsonAppSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IFileSystem _fileSystem;
    private readonly string _filePath;

    /// <summary>
    /// 初始化设置仓储。
    /// </summary>
    /// <param name="filePath">设置文件完整路径。</param>
    /// <param name="fileSystem">文件系统抽象。为空时使用真实磁盘实现。</param>
    public JsonAppSettingsStore(string filePath, IFileSystem? fileSystem = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _filePath = filePath;
        _fileSystem = fileSystem ?? PhysicalFileSystem.Instance;
    }

    /// <summary>设置文件路径。</summary>
    public string FilePath => _filePath;

    /// <summary>
    /// 获取默认设置文件路径：<c>%LOCALAPPDATA%\BrowserVideoGrabber\settings.json</c>。
    /// </summary>
    /// <returns>默认文件路径。</returns>
    public static string GetDefaultFilePath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BrowserVideoGrabber",
            "settings.json");

    /// <summary>
    /// 载入设置。
    /// </summary>
    /// <returns>设置对象。文件不存在或损坏时返回默认设置。</returns>
    public AppSettings Load()
    {
        if (!_fileSystem.FileExists(_filePath))
        {
            return new AppSettings();
        }

        try
        {
            using var stream = _fileSystem.OpenRead(_filePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(stream, SerializerOptions);

            if (settings is null)
            {
                return new AppSettings();
            }

            // 防御性修正：设置文件可能被手工改坏，非法并发数会导致队列行为异常
            if (settings.MaxConcurrency < 1)
            {
                settings.MaxConcurrency = 3;
            }

            if (settings.HttpSegmentCount < 1)
            {
                settings.HttpSegmentCount = 4;
            }

            return settings;
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// 保存设置（原子写入）。
    /// </summary>
    /// <param name="settings">待保存的设置。</param>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _fileSystem.CreateDirectory(directory);
        }

        var temporaryPath = _filePath + ".tmp";

        using (var stream = _fileSystem.OpenWrite(temporaryPath, append: false))
        {
            JsonSerializer.Serialize(stream, settings, SerializerOptions);
        }

        _fileSystem.MoveFile(temporaryPath, _filePath, overwrite: true);
    }
}
