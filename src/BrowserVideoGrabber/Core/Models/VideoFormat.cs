/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Models
*文件名： VideoFormat
*版本号： V1.0.0.0
*唯一标识：0139f10f-ec6f-452f-bd5c-d5d8a09a1201
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 22:12:00
*描述：视频资源格式枚举，用于嗅探结果分类与下载处理器策略分发。
*
*=================================================
*修改标记
*修改时间：2026/9/12 22:12:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

namespace BrowserVideoGrabber.Core.Models;

/// <summary>
/// 视频资源格式分类。
/// </summary>
/// <remarks>
/// 该枚举同时承担两个职责：
/// 1）标注嗅探到的资源类型，供界面展示与去重；
/// 2）作为下载处理器（<c>IDownloadHandler</c>）的策略选择依据 ——
///    <see cref="Mp4"/> 走原生多线程下载，其余均交给 ffmpeg 托管。
/// </remarks>
public enum VideoFormat
{
    /// <summary>未知格式，通常为误报，不会进入下载队列。</summary>
    Unknown = 0,

    /// <summary>HLS 播放列表（.m3u8）。内部指向 ts 或 fMP4 分片，可能带 AES-128 加密。</summary>
    M3u8 = 1,

    /// <summary>MPEG-TS 分片（.ts），HLS 最常见的分片载体。</summary>
    Ts = 2,

    /// <summary>fMP4 分片（.m4s），DASH 的分片载体，单独一个分片无意义，需配合 init 段。</summary>
    M4s = 3,

    /// <summary>MP4 整文件。支持 HTTP Range 请求，可实现多线程分片与断点续传。</summary>
    Mp4 = 4,

    /// <summary>DASH 清单（.mpd）。交给 ffmpeg 自行解析并拉取分片。</summary>
    Mpd = 5,

    /// <summary>Microsoft Smooth Streaming 清单（.ism / .ismc）。交给 ffmpeg 自行拉取。</summary>
    Ismc = 6
}
