/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Tests.Fakes
*文件名： FakeMediaFetcher
*版本号： V1.0.0.0
*唯一标识：e6fe35fd-5f45-40eb-9f7b-898d1ff95958
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 01:14:00
*描述：IMediaFetcher 的内存假实现，按 URL 预置文本/字节/文件响应与失败，用于不联网测试取片编排。
*
*=================================================
*修改标记
*修改时间：2026/9/13 01:14:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Collections.Generic;
using System.Text;
using BrowserVideoGrabber.Core.Abstractions;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Tests.Fakes;

/// <summary>
/// <see cref="IMediaFetcher"/> 的内存假实现。
/// </summary>
/// <remarks>
/// 通过预置「URL → 响应」映射，可精确构造「第 N 个分片返回占位图」「密钥返回 16 字节」等场景，
/// 完全不触碰真实网络。文件类响应会借注入的 <see cref="IFileSystem"/> 真正落盘，
/// 以贴合 <see cref="IMediaFetcher.GetToFileAsync"/> 的契约。
/// </remarks>
public sealed class FakeMediaFetcher : IMediaFetcher
{
    private readonly IFileSystem _fileSystem;
    private readonly Dictionary<string, MediaFetchResult> _textResponses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (byte[] Content, string ContentType)> _byteAndFile = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 构造假抓取器。
    /// </summary>
    /// <param name="fileSystem">用于落盘文件类响应的文件系统。</param>
    public FakeMediaFetcher(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    /// <summary>预置一个文本响应（用于 m3u8 清单）。</summary>
    public void SeedText(string url, string text)
        => _textResponses[url] = MediaFetchResult.Ok(200, "application/vnd.apple.mpegurl", Encoding.UTF8.GetByteCount(text), text: text);

    /// <summary>预置一个字节响应（用于密钥）。</summary>
    public void SeedBytes(string url, byte[] content, string contentType = "application/octet-stream")
        => _byteAndFile[url] = (content, contentType);

    /// <summary>预置一个会真正落盘的文件响应（用于分片）。</summary>
    public void SeedFile(string url, byte[] content, string contentType = "video/mp2t")
        => _byteAndFile[url] = (content, contentType);

    /// <summary>标记某 URL 抓取失败。</summary>
    public void Fail(string url) => _failures.Add(url);

    /// <inheritdoc />
    public Task<MediaFetchResult> GetStringAsync(string url, RequestContext context, CancellationToken cancellationToken)
        => Task.FromResult(_textResponses.TryGetValue(url, out var result)
            ? result
            : MediaFetchResult.Fail($"未预置文本响应：{url}"));

    /// <inheritdoc />
    public Task<MediaFetchResult> GetBytesAsync(string url, RequestContext context, CancellationToken cancellationToken)
    {
        if (_failures.Contains(url))
        {
            return Task.FromResult(MediaFetchResult.Fail($"字节获取失败：{url}"));
        }

        return Task.FromResult(_byteAndFile.TryGetValue(url, out var pair)
            ? MediaFetchResult.Ok(200, pair.ContentType, pair.Content.Length, bytes: pair.Content)
            : MediaFetchResult.Fail($"未预置字节响应：{url}"));
    }

    /// <inheritdoc />
    public Task<MediaFetchResult> GetToFileAsync(string url, RequestContext context, string destinationPath, CancellationToken cancellationToken)
    {
        if (_failures.Contains(url))
        {
            return Task.FromResult(MediaFetchResult.Fail($"分片获取失败：{url}"));
        }

        if (_byteAndFile.TryGetValue(url, out var pair))
        {
            using var writeStream = _fileSystem.OpenWrite(destinationPath, false);
            writeStream.Write(pair.Content, 0, pair.Content.Length);
            return Task.FromResult(MediaFetchResult.Ok(200, pair.ContentType, pair.Content.Length));
        }

        return Task.FromResult(MediaFetchResult.Fail($"未预置文件响应：{url}"));
    }
}
