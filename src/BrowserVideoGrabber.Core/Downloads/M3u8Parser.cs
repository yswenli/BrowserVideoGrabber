/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Downloads
*文件名： M3u8Parser
*版本号： V1.0.0.0
*唯一标识：715f15ea-4615-413c-8faf-fcc36f41b66d
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:54:00
*描述：m3u8 播放列表解析器，识别加密方式、清晰度变体、分片序列与 init 段。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:54:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Globalization;
using System.Text;

namespace BrowserVideoGrabber.Core.Downloads;

/// <summary>
/// m3u8（HLS）播放列表解析器。
/// </summary>
/// <remarks>
/// <para>
/// 设计原则是「宽进严出」：对任何畸形输入都不抛异常，只返回
/// <see cref="M3u8Playlist.Invalid"/>，因为播放列表来自不可控的第三方站点，
/// 让一个畸形清单把整个下载流程炸掉是不可接受的。
/// </para>
/// <para>
/// 仅实现本工具实际需要的能力：加密判定、清晰度变体、分片序列、init 段与总时长。
/// 不实现 <c>#EXT-X-BYTERANGE</c> 等罕见标签 —— 这些场景统一交给 ffmpeg 处理。
/// </para>
/// </remarks>
public static class M3u8Parser
{
    /// <summary>播放列表首行标识。</summary>
    private const string HeaderTag = "#EXTM3U";

    /// <summary>
    /// 解析播放列表文本。
    /// </summary>
    /// <param name="content">播放列表原文。允许为 null 或空白。</param>
    /// <param name="baseUri">
    /// 播放列表自身的地址，用于把相对路径分片解析为绝对地址。
    /// 为 null 时相对路径将原样保留（调用方需自行处理）。
    /// </param>
    /// <returns>解析结果。内容无效时返回 <see cref="M3u8Playlist.Invalid"/>。</returns>
    public static M3u8Playlist Parse(string? content, Uri? baseUri)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return M3u8Playlist.Invalid;
        }

        // 缺失 #EXTM3U 头说明这不是一份播放列表（例如站点返回了一个 HTML 错误页）
        if (!content.Contains(HeaderTag, StringComparison.OrdinalIgnoreCase))
        {
            return M3u8Playlist.Invalid;
        }

        var segments = new List<M3u8Segment>();
        var variants = new List<M3u8Variant>();

        var encryption = M3u8Encryption.None;
        string? keyUri = null;
        string? initSegmentUri = null;
        var isDrmProtected = false;
        var isMasterPlaylist = false;
        var totalSeconds = 0d;

        // 待与下一个 URI 行配对的上下文
        double? pendingDuration = null;
        long pendingBandwidth = 0;
        string? pendingResolution = null;

        foreach (var rawLine in SplitLines(content))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith('#'))
            {
                if (line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.OrdinalIgnoreCase))
                {
                    // 主播放列表标志：后面紧跟的那一行 URI 是一档清晰度
                    isMasterPlaylist = true;
                    var attributes = ParseAttributes(line[(line.IndexOf(':') + 1)..]);
                    pendingBandwidth = attributes.TryGetValue("BANDWIDTH", out var bandwidthText)
                        && long.TryParse(bandwidthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bandwidth)
                            ? bandwidth
                            : 0;
                    pendingResolution = attributes.TryGetValue("RESOLUTION", out var resolution) ? resolution : null;
                }
                else if (line.StartsWith("#EXT-X-SESSION-KEY:", StringComparison.OrdinalIgnoreCase))
                {
                    // SESSION-KEY 只出现在受 DRM 保护的清单中，无条件视为受保护内容
                    isDrmProtected = true;
                    encryption = M3u8Encryption.Drm;
                    var attributes = ParseAttributes(line[(line.IndexOf(':') + 1)..]);
                    if (attributes.TryGetValue("URI", out var sessionKeyUri))
                    {
                        keyUri = ResolveUri(sessionKeyUri, baseUri);
                    }
                }
                else if (line.StartsWith("#EXT-X-KEY:", StringComparison.OrdinalIgnoreCase))
                {
                    var attributes = ParseAttributes(line[(line.IndexOf(':') + 1)..]);
                    var method = attributes.TryGetValue("METHOD", out var methodText) ? methodText : string.Empty;

                    encryption = method.ToUpperInvariant() switch
                    {
                        "NONE" => M3u8Encryption.None,
                        "AES-128" => M3u8Encryption.Aes128,
                        // SAMPLE-AES / SAMPLE-AES-CTR 均为商业 DRM 的一部分，密钥无法通过 URI 直接获取
                        "SAMPLE-AES" or "SAMPLE-AES-CTR" => M3u8Encryption.Drm,
                        _ => M3u8Encryption.Unknown
                    };

                    if (encryption == M3u8Encryption.Drm)
                    {
                        isDrmProtected = true;
                    }

                    if (attributes.TryGetValue("URI", out var declaredKeyUri))
                    {
                        keyUri = ResolveUri(declaredKeyUri, baseUri);
                    }
                }
                else if (line.StartsWith("#EXT-X-MAP:", StringComparison.OrdinalIgnoreCase))
                {
                    // fMP4（DASH 风格）播放列表必须的初始化分片
                    var attributes = ParseAttributes(line[(line.IndexOf(':') + 1)..]);
                    if (attributes.TryGetValue("URI", out var mapUri))
                    {
                        initSegmentUri = ResolveUri(mapUri, baseUri);
                    }
                }
                else if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
                {
                    pendingDuration = ParseDuration(line[(line.IndexOf(':') + 1)..]);
                }

                // 其余标签（#EXT-X-VERSION、#EXT-X-TARGETDURATION、#EXT-X-ENDLIST 等）无需处理
                continue;
            }

            // 非 # 开头的行 = 资源地址行，需要与前面的待配对上下文绑定
            var resolvedUri = ResolveUri(line, baseUri);

            if (pendingDuration.HasValue)
            {
                segments.Add(new M3u8Segment(resolvedUri, pendingDuration.Value));
                totalSeconds += pendingDuration.Value;
                pendingDuration = null;
            }
            else if (pendingBandwidth > 0 || pendingResolution is not null)
            {
                variants.Add(new M3u8Variant(resolvedUri, pendingBandwidth, pendingResolution));
                pendingBandwidth = 0;
                pendingResolution = null;
            }
        }

        return new M3u8Playlist
        {
            // 至少要解析出一个分片或一档清晰度，才认为是有效清单
            IsValid = segments.Count > 0 || variants.Count > 0,
            IsMasterPlaylist = isMasterPlaylist,
            Variants = variants,
            Segments = segments,
            Encryption = encryption,
            KeyUri = keyUri,
            InitSegmentUri = initSegmentUri,
            TotalDuration = TimeSpan.FromSeconds(totalSeconds),
            IsDrmProtected = isDrmProtected
        };
    }

    /// <summary>
    /// 按行切分文本，统一处理 LF / CRLF / CR 三种换行风格。
    /// </summary>
    /// <param name="content">原始文本。</param>
    /// <returns>行序列。</returns>
    private static IEnumerable<string> SplitLines(string content)
        => content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    /// <summary>
    /// 解析 #EXTINF 后的时长值（形如 <c>10.0,</c> 或 <c>10.0</c>）。
    /// </summary>
    /// <param name="text">冒号之后的文本。</param>
    /// <returns>解析出的秒数；无法解析时返回 null。</returns>
    private static double? ParseDuration(string text)
    {
        // #EXTINF 格式为 "时长,标题"，标题可以为空且可能含逗号，因此只取第一个逗号之前的部分
        var commaIndex = text.IndexOf(',');
        var valueText = commaIndex >= 0 ? text[..commaIndex] : text;

        return double.TryParse(valueText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? seconds
            : null;
    }

    /// <summary>
    /// 解析形如 <c>BANDWIDTH=5000000,RESOLUTION=1920x1080</c> 的属性串。
    /// </summary>
    /// <param name="text">属性串原文。</param>
    /// <returns>属性名到属性值的字典（键不区分大小写），值已去除外层引号。</returns>
    /// <remarks>
    /// 手写解析而非直接按逗号切分，原因是属性值可能被引号包裹且内部含逗号
    /// （例如 <c>CODECS="avc1.640028,mp4a.40.2"</c>），按逗号硬切会把一个属性拆成两半。
    /// </remarks>
    private static Dictionary<string, string> ParseAttributes(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var parts = new List<string>();
        var current = new StringBuilder();
        var insideQuotes = false;

        foreach (var character in text)
        {
            if (character == '"')
            {
                insideQuotes = !insideQuotes;
                continue;
            }

            if (character == ',' && !insideQuotes)
            {
                parts.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        foreach (var part in parts)
        {
            var separatorIndex = part.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = part[..separatorIndex].Trim();
            var value = part[(separatorIndex + 1)..].Trim();
            if (key.Length > 0)
            {
                result[key] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// 把播放列表中出现的地址解析为绝对地址。
    /// </summary>
    /// <param name="uriText">地址文本，可能是绝对地址也可能是相对路径。</param>
    /// <param name="baseUri">播放列表自身地址，用于解析相对路径。</param>
    /// <returns>绝对地址；无法解析时原样返回输入文本。</returns>
    private static string ResolveUri(string uriText, Uri? baseUri)
    {
        if (Uri.TryCreate(uriText, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.ToString();
        }

        if (baseUri is not null && Uri.TryCreate(baseUri, uriText, out var combinedUri))
        {
            return combinedUri.ToString();
        }

        return uriText;
    }
}
