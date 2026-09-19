/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Infrastructure.Storage
*文件名： PhysicalFileSystem
*版本号： V1.0.0.0
*唯一标识：2c150bc7-1be7-4234-abe3-08613f9d2032
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 23:14:00
*描述：基于真实磁盘的文件系统实现。
*
*=================================================
*修改标记
*修改时间：2026/9/12 23:14:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Abstractions;

namespace BrowserVideoGrabber.Infrastructure.Storage;

/// <summary>
/// 基于真实磁盘的 <see cref="IFileSystem"/> 实现。
/// </summary>
/// <remarks>
/// 实现上刻意把「文件不存在」这类可预期情况做成幂等行为（删除不存在的文件、读取不存在的文件返回 0），
/// 因为下载流程中会大量执行清理动作，若每次都需先判断存在性，代码会变得非常啰嗦且容易漏判。
/// </remarks>
public sealed class PhysicalFileSystem : IFileSystem
{
    /// <summary>共享实例。本类无可变状态，可安全复用。</summary>
    public static PhysicalFileSystem Instance { get; } = new();

    /// <inheritdoc />
    public bool FileExists(string path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    /// <inheritdoc />
    public bool DirectoryExists(string path)
        => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);

    /// <inheritdoc />
    public long GetFileLength(string path)
    {
        if (!FileExists(path))
        {
            return 0L;
        }

        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            // 文件可能正被 ffmpeg 占用，此时拿不到长度，返回 0 优于抛异常
            return 0L;
        }
    }

    /// <inheritdoc />
    public void CreateDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        Directory.CreateDirectory(path);
    }

    /// <inheritdoc />
    public void DeleteFile(string path)
    {
        if (!FileExists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // 文件被占用时删除失败不应中断流程，残留文件会在后续清理中处理
        }
        catch (UnauthorizedAccessException)
        {
            // 权限不足同理，交由上层以「清理失败」提示用户
        }
    }

    /// <inheritdoc />
    public bool DeleteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !DirectoryExists(path))
        {
            return false;
        }

        try
        {
            Directory.Delete(path, recursive: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 目录被占用（例如 ffmpeg 尚未完全退出）或权限不足：
            // 清理失败不能让下载结果失效，交由上层决定是否提示用户
            return false;
        }
    }

    /// <inheritdoc />
    public Stream OpenWrite(string path, bool append)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            CreateDirectory(directory);
        }

        return new FileStream(
            path,
            append ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
    }

    /// <inheritdoc />
    public Stream OpenRead(string path)
        => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);

    /// <inheritdoc />
    public long GetAvailableFreeSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrWhiteSpace(root))
            {
                return long.MaxValue;
            }

            var driveInfo = new DriveInfo(root);
            return driveInfo.IsReady ? driveInfo.AvailableFreeSpace : long.MaxValue;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // 无法判定剩余空间时返回「充足」，避免因探测失败而错误地阻止下载
            return long.MaxValue;
        }
    }

    /// <inheritdoc />
    public void MoveFile(string sourcePath, string destinationPath, bool overwrite)
        => File.Move(sourcePath, destinationPath, overwrite);
}
