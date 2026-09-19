/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Downloads
*文件名： HlsAssembler
*版本号： V1.0.0.0
*唯一标识：3eceb288-7370-4387-bee4-405b200a0f83
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 01:12:00
*描述：HLS 分片拼接器，按播放顺序把可用分片（含 init 段）合并为单个 TS 文件。
*
*=================================================
*修改标记
*修改时间：2026/9/13 01:12:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Abstractions;

namespace BrowserVideoGrabber.Core.Downloads;

/// <summary>
/// HLS 分片拼接器。
/// </summary>
/// <remarks>
/// <para>
/// 职责极其单一：把已通过校验、已解密的若干分片文件，按播放顺序<b>逐字节拼接</b>成一个
/// 统一的 <c>.ts</c>。TS 分片天然可裸拼接（每个分片自带 PAT/PMT），因此这里不做任何转封装，
/// 转封装交给随后调用的 ffmpeg <c>-c copy</c>。
/// </para>
/// <para>
/// 之所以把「拼接」从「转封装」里拆出来，是因为拼接是确定性的字节搬运，而应放在 C# 这一侧
/// 用内存文件系统精确测试；而 ffmpeg 的 <c>-c copy</c> 只是把拼接结果重命名为 mp4 容器，几乎不会出错。
/// </para>
/// </remarks>
public sealed class HlsAssembler
{
    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// 构造拼接器。
    /// </summary>
    /// <param name="fileSystem">文件系统抽象。</param>
    public HlsAssembler(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    /// <summary>
    /// 按序拼接分片为单个输出文件。
    /// </summary>
    /// <param name="segmentFiles">已解密分片的本地路径，按播放顺序。</param>
    /// <param name="outputPath">合并后的 TS 输出路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>写入的总字节数。</returns>
    public async Task<long> ConcatenateAsync(
        IReadOnlyList<string> segmentFiles,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (segmentFiles is null || segmentFiles.Count == 0)
        {
            return 0;
        }

        long total = 0;
        using var output = _fileSystem.OpenWrite(outputPath, false);
        foreach (var segment in segmentFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var input = _fileSystem.OpenRead(segment);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            total += input.Length;
        }

        return total;
    }
}
