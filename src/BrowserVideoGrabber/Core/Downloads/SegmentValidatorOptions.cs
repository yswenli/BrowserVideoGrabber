/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Downloads
*文件名： SegmentValidatorOptions
*版本号： V1.0.0.0
*唯一标识：471f0898-c392-451c-8e1d-8d57eae6f464
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 00:44:00
*描述：分片内容校验的可调参数集合，使不同站点的判定阈值可在不改动校验逻辑的前提下调整。
*
*=================================================
*修改标记
*修改时间：2026/9/13 00:44:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

namespace BrowserVideoGrabber.Core.Downloads;

/// <summary>
/// 分片内容校验器的可调参数。
/// </summary>
/// <remarks>
/// 这些参数从校验逻辑中抽离出来，是因为不同 CDN 的污染形态差异很大：
/// 有的站点是「全部占位」，有的只是「片头片尾少量占位」。把阈值做成可配置项，
/// 既能避免把可用的下载误判成失败，也能在污染严重时果断放弃，而不是硬拼成一个坏文件。
/// </remarks>
public sealed class SegmentValidatorOptions
{
    /// <summary>
    /// 判定「整体仍值得保留」所需的最低可用分片占比（值域 0~1）。默认 0.1。
    /// </summary>
    /// <remarks>
    /// 值越小越激进（少量好片也拼）：实测某站污染率约 27%，把阈值设到 0.1 仍能正常保留。
    /// 但 1331 片里仅剩 2 片时，拼出的 10 秒 mp4 冒充成功毫无意义，故需设下限。
    /// </remarks>
    public double MinimumUsableRatio { get; set; } = 0.1;
}
