/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Downloads
*文件名： HttpDownloadOptions
*版本号： V1.0.0.0
*唯一标识：0e8fdfdf-6f3a-4c1e-9c8b-2d5a1f7e4b30
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 00:05:00
*描述：原生 HTTP 下载器的行为配置，集中承载分片数、缓冲与重试等可调参数。
*
*=================================================
*修改标记
*修改时间：2026/9/13 00:05:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

namespace BrowserVideoGrabber.Core.Downloads;

/// <summary>
/// 原生 HTTP 下载器配置。
/// </summary>
/// <remarks>
/// 与 <c>AppSettings</c> 分离而不直接复用，是为了让下载器只依赖「自己需要的那几个参数」，
/// 而不是整个应用设置对象。这样单测里构造配置无需关心 UI 相关字段，
/// 后续把并发数改成按任务动态计算时也不会波及设置界面。
/// </remarks>
public sealed class HttpDownloadOptions
{
    /// <summary>分片并发数。设为 1 时退化为单线程顺序下载。</summary>
    public int SegmentCount { get; set; } = 4;

    /// <summary>
    /// 触发分片的最小文件字节数。
    /// </summary>
    /// <remarks>
    /// 小文件（如几十 KB 的预览片段）分片反而会因多次建连而更慢，
    /// 因此低于该阈值一律单连接下载。
    /// </remarks>
    public long MinimumSegmentBytes { get; set; } = 4L * 1024 * 1024;

    /// <summary>读写缓冲区字节数。默认 80 KB，与 .NET 流默认值保持一致。</summary>
    public int BufferSize { get; set; } = 81920;

    /// <summary>单个分片遭遇瞬时错误时的就地重试次数。默认 2 次。</summary>
    /// <remarks>
    /// 这一层重试与队列层的「任务级重试」互补：分片级重试避免一个抖动的分片
    /// 把整个任务打回待下载队列，从而保住已下载的其它分片。
    /// </remarks>
    public int MaxRetryPerSegment { get; set; } = 2;

    /// <summary>进度上报的最小间隔（毫秒）。默认 100 毫秒。</summary>
    public int ProgressIntervalMilliseconds { get; set; } = 100;

    /// <summary>
    /// 探测请求的超时时间。默认 30 秒。
    /// </summary>
    /// <remarks>
    /// 探测只取一个字节，理论上应在毫秒级完成；给到 30 秒是为了容忍高延迟链路，
    /// 超过即认为该地址不可用并转由 ffmpeg 尝试。
    /// </remarks>
    public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 分片传输的<b>空闲</b>超时时间。默认 60 秒。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这里刻意不用 <c>HttpClient.Timeout</c>：它约束的是「整个请求从发出到读完正文」的总时长，
    /// 而大文件下载动辄数十分钟，用总时长做超时会把正常的慢速下载误杀。
    /// </para>
    /// <para>
    /// 改为「空闲超时」——只要还在持续收到数据就永不超时，一旦超过该时长没有任何字节到达
    /// 就判定连接已僵死并中断。这正是下载器真正需要的语义。
    /// </para>
    /// </remarks>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(60);
}
