/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Ffmpeg
*文件名： FfmpegOptions
*版本号： V1.0.0.0
*唯一标识：35b5d7de-5566-42f9-9e96-5dc9774425a1
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:56:00
*描述：ffmpeg 调用参数配置，集中管理可调项以便在设置界面暴露给用户。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:56:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

namespace BrowserVideoGrabber.Core.Ffmpeg;

/// <summary>
/// ffmpeg 调用配置。
/// </summary>
/// <remarks>
/// 默认值的选取依据是「网络流媒体下载」这一特定场景，而非通用转码场景：
/// 默认不做任何重编码（<see cref="CopyCodec"/> 为 true），以最短耗时、最小体积完成下载。
/// </remarks>
public sealed class FfmpegOptions
{
    /// <summary>ffmpeg 可执行文件路径。仅用于展示与诊断，实际调用路径由定位器决定。</summary>
    public string ExecutablePath { get; set; } = "ffmpeg";

    /// <summary>是否附加 <c>-y</c> 直接覆盖已存在的输出文件。默认 true。</summary>
    public bool Overwrite { get; set; } = true;

    /// <summary>
    /// 是否以流拷贝方式下载（<c>-c copy</c>）。默认 true。
    /// 关闭后走重编码分支，仅在需要修复异常流时才应使用，会显著增加耗时。
    /// </summary>
    public bool CopyCodec { get; set; } = true;

    /// <summary>
    /// 是否启用机器可读的进度输出（<c>-nostats -progress pipe:2</c>）。默认 true。
    /// 关闭后进度解析器将收不到稳定的逐行进度，进度条会失效。
    /// </summary>
    public bool EnableProgressPipe { get; set; } = true;

    /// <summary>
    /// 是否允许 HTTP 断线重连（<c>-reconnect</c>）。默认 true。
    /// </summary>
    /// <remarks>
    /// 该参数在 ffmpeg 中是 <b>0/1 开关</b>而非重试次数，
    /// 真正的重试次数由 ffmpeg 内部策略决定，外部只能通过
    /// <see cref="ReconnectDelayMaxSeconds"/> 限制单次等待上限。
    /// </remarks>
    public bool Reconnect { get; set; } = true;

    /// <summary>是否允许流式输入（HLS/DASH）断线重连（<c>-reconnect_streamed</c>）。默认 true。</summary>
    public bool ReconnectStreamed { get; set; } = true;

    /// <summary>重连最大等待秒数（<c>-reconnect_delay_max</c>）。默认 5 秒。</summary>
    public int ReconnectDelayMaxSeconds { get; set; } = 5;

    /// <summary>
    /// 协议白名单（<c>-protocol_whitelist</c>）。
    /// 不放开这一限制时，ffmpeg 会拒绝加载 HLS 清单中的分片。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 列表中的 <c>httpproxy</c> 不可省略：当系统存在 <c>http_proxy</c> / <c>https_proxy</c>
    /// 环境变量（常见的代理/加速工具都会设置）时，ffmpeg 会改走 <c>httpproxy</c> 协议取流。
    /// 若白名单中没有它，ffmpeg 会在打开输入阶段直接报
    /// <c>Protocol 'httpproxy' not on whitelist</c> 并以 <c>-22 (EINVAL)</c> 退出 ——
    /// 表面上像是「地址失效」或「CDN 拒绝」，实际与远端毫无关系。
    /// </para>
    /// <para>
    /// <c>file</c> 与 <c>crypto</c> 同样是 HLS 的硬性需求：
    /// 前者用于清单中出现的本地路径，后者用于 AES-128 解密。
    /// </para>
    /// </remarks>
    public string ProtocolWhitelist { get; set; } = "file,http,https,tcp,tls,crypto,httpproxy";

    /// <summary>
    /// 允许的扩展名（<c>-allowed_extensions</c>）。设为 ALL 以兼容 .ts / .m4s / .aac 等各种分片。
    /// </summary>
    public string AllowedExtensions { get; set; } = "ALL";

    /// <summary>
    /// 是否关闭 ffmpeg 的扩展名挑剔开关（<c>-extension_picky 0</c>）。默认 true。
    /// </summary>
    /// <remarks>
    /// <para>
    /// HLS 解复用器对分片扩展名有一份<b>硬编码白名单</b>，且它<b>不受 <c>-allowed_extensions</c> 约束</b>。
    /// 真实站点把 MPEG-TS 分片命名为 <c>.jpeg</c> / <c>.jpg</c> 是常见做法（用于规避简易抓取工具），
    /// 此时 ffmpeg 会直接报
    /// <c>URL ... is not in allowed_segment_extensions, consider updating hls.c</c>
    /// 并以「Invalid data found when processing input」失败 —— 表面上完全看不出是扩展名问题。
    /// </para>
    /// <para>
    /// <b>版本要求</b>：该选项由 ffmpeg 7.1 引入。若用户环境为更早版本，ffmpeg 会报
    /// 「Unrecognized option 'extension_picky'」而直接退出，此时应把本属性置为 false。
    /// 本工具的 VOD 下载已改由 C# 自行取片，该开关只影响<b>直播与兜底</b>这两条 ffmpeg 直连路径。
    /// </para>
    /// </remarks>
    public bool DisableExtensionPicky { get; set; } = true;

    /// <summary>
    /// 合并产出 mp4 时是否把索引前移（<c>-movflags +faststart</c>）。默认 true。
    /// </summary>
    /// <remarks>
    /// 仅对「本地输入重封装」有意义：把 moov 原子放到文件头部，成品才能被浏览器与流式播放器
    /// 边下载边播放。对纯本地播放或二次处理无影响。
    /// </remarks>
    public bool EnableFastStart { get; set; } = true;
}
