/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Abstractions
*文件名： IFileSystem
*版本号： V1.0.0.0
*唯一标识：20e2c5f5-1232-416f-a4ee-feae52ad92d1
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:24:00
*描述：文件系统抽象接口，隔离真实磁盘读写以便单元测试。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:24:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

namespace BrowserVideoGrabber.Core.Abstractions;

/// <summary>
/// 文件系统抽象。
/// </summary>
/// <remarks>
/// 下载逻辑中大量存在「文件是否已存在」「已有多少字节」「剩余空间是否够用」这类判断，
/// 抽象之后既可以在测试中用内存实现覆盖各种边界（磁盘满、半成品残留），
/// 也便于后续替换为其它存储介质。
/// </remarks>
public interface IFileSystem
{
    /// <summary>判断文件是否存在。</summary>
    /// <param name="path">文件路径。</param>
    /// <returns>存在返回 true。</returns>
    bool FileExists(string path);

    /// <summary>判断目录是否存在。</summary>
    /// <param name="path">目录路径。</param>
    /// <returns>存在返回 true。</returns>
    bool DirectoryExists(string path);

    /// <summary>获取文件字节数。文件不存在时返回 0。</summary>
    /// <param name="path">文件路径。</param>
    /// <returns>文件字节数。</returns>
    long GetFileLength(string path);

    /// <summary>创建目录（含多级父目录）。目录已存在时不抛异常。</summary>
    /// <param name="path">目录路径。</param>
    void CreateDirectory(string path);

    /// <summary>删除文件。文件不存在时不抛异常。</summary>
    /// <param name="path">文件路径。</param>
    void DeleteFile(string path);

    /// <summary>以写入方式打开文件。</summary>
    /// <param name="path">文件路径。</param>
    /// <param name="append">true 表示追加写（用于断点续传），false 表示覆盖写。</param>
    /// <returns>可写的文件流，由调用方负责释放。</returns>
    Stream OpenWrite(string path, bool append);

    /// <summary>以只读方式打开文件。</summary>
    /// <param name="path">文件路径。</param>
    /// <returns>可读的文件流，由调用方负责释放。</returns>
    Stream OpenRead(string path);

    /// <summary>获取指定路径所在驱动器的可用剩余空间。</summary>
    /// <param name="path">用于定位驱动器的任意路径。</param>
    /// <returns>可用字节数。无法确定时返回 <see cref="long.MaxValue"/>，表示「不阻止下载」。</returns>
    long GetAvailableFreeSpace(string path);

    /// <summary>移动（或重命名）文件。</summary>
    /// <param name="sourcePath">源文件路径。</param>
    /// <param name="destinationPath">目标文件路径。</param>
    /// <param name="overwrite">目标已存在时是否覆盖。</param>
    void MoveFile(string sourcePath, string destinationPath, bool overwrite);
}
