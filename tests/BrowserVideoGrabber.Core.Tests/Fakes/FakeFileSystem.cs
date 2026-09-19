/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Fakes
*文件名： FakeFileSystem
*版本号： V1.0.0.0
*唯一标识：9c78e09a-be1c-4700-8498-8aa35d48245d
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:31:00
*描述：文件系统的内存假实现，用于在不触碰真实磁盘的前提下测试下载逻辑。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:31:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Collections.Concurrent;
using BrowserVideoGrabber.Core.Abstractions;

namespace BrowserVideoGrabber.Tests.Fakes;

/// <summary>
/// <see cref="IFileSystem"/> 的内存假实现。
/// </summary>
/// <remarks>
/// 内部用字节数组字典模拟文件，用字符串集合模拟目录。
/// 这样可以精确构造「半成品文件残留」「磁盘空间不足」等真实磁盘上难以复现的边界场景。
/// </remarks>
public sealed class FakeFileSystem : IFileSystem
{
    private readonly ConcurrentDictionary<string, byte[]> _files =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, bool> _directories =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>模拟的可用剩余空间（字节）。默认 100 GB，设为 0 可模拟磁盘写满。</summary>
    public long AvailableFreeSpace { get; set; } = 100L * 1024 * 1024 * 1024;

    /// <summary>已写入文件的路径集合快照。</summary>
    public IReadOnlyCollection<string> FilePaths => _files.Keys.ToList();

    /// <summary>
    /// 直接写入/覆盖一个文件，供测试预置初始状态使用。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <param name="content">文件内容。</param>
    public void SeedFile(string path, byte[] content) => _files[path] = content;

    /// <summary>
    /// 直接写入一个指定长度的文件，用于模拟「已下载一半的半成品」。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <param name="length">文件字节数。</param>
    public void SeedFile(string path, long length) => _files[path] = new byte[length];

    /// <summary>
    /// 读取文件内容。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <returns>文件内容；文件不存在时返回空数组。</returns>
    public byte[] ReadFile(string path)
        => _files.TryGetValue(path, out var content) ? content : Array.Empty<byte>();

    /// <inheritdoc />
    public bool FileExists(string path) => _files.ContainsKey(path);

    /// <inheritdoc />
    public bool DirectoryExists(string path) => _directories.ContainsKey(path);

    /// <inheritdoc />
    public long GetFileLength(string path) => _files.TryGetValue(path, out var content) ? content.Length : 0;

    /// <inheritdoc />
    public void CreateDirectory(string path) => _directories[path] = true;

    /// <inheritdoc />
    public void DeleteFile(string path) => _files.TryRemove(path, out _);

    /// <inheritdoc />
    public bool DeleteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        // 先删目录下的文件（含调用方不知道的残留），再删目录本身，
        // 与真实实现的「递归删除」语义保持一致
        var prefix = path.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        var children = _files.Keys
            .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var child in children)
        {
            _files.TryRemove(child, out _);
        }

        return _directories.TryRemove(path, out _);
    }

    /// <inheritdoc />
    public Stream OpenWrite(string path, bool append)
    {
        // 追加模式以已有内容为基础构造可写流，写入完成后回写字典，模拟真实文件系统语义
        var existing = append && _files.TryGetValue(path, out var content) ? content : Array.Empty<byte>();
        return new MemoryWriteStream(existing, bytes => _files[path] = bytes);
    }

    /// <inheritdoc />
    public Stream OpenRead(string path)
        => new MemoryStream(_files.TryGetValue(path, out var content) ? content : Array.Empty<byte>(), writable: false);

    /// <inheritdoc />
    public long GetAvailableFreeSpace(string path) => AvailableFreeSpace;

    /// <inheritdoc />
    public void MoveFile(string sourcePath, string destinationPath, bool overwrite)
    {
        if (!_files.TryRemove(sourcePath, out var content))
        {
            throw new FileNotFoundException("源文件不存在。", sourcePath);
        }

        if (!overwrite && _files.ContainsKey(destinationPath))
        {
            throw new IOException($"目标文件已存在：{destinationPath}");
        }

        _files[destinationPath] = content;
    }

    /// <summary>
    /// 写入完成后把数据回写到内存字典的流实现。
    /// </summary>
    private sealed class MemoryWriteStream : MemoryStream
    {
        private readonly Action<byte[]> _onDispose;
        private readonly byte[] _initialContent;

        public MemoryWriteStream(byte[] initialContent, Action<byte[]> onDispose)
            : base(initialContent.Length + 1024)
        {
            _initialContent = initialContent;
            _onDispose = onDispose;
            Write(initialContent, 0, initialContent.Length);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _onDispose(ToArray());
            }

            base.Dispose(disposing);
        }

        /// <summary>初始内容（追加写之前的已有数据）。</summary>
        public byte[] InitialContent => _initialContent;
    }
}
