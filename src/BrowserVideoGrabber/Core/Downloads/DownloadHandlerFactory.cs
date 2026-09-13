/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.Core.Downloads
*文件名： DownloadHandlerFactory
*版本号： V1.0.0.0
*唯一标识：b28fba36-29fd-41aa-80e5-2992142c7132
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/12 23:05:00
*描述：下载处理器工厂，按任务格式选择对应的下载策略实现。
*
*=================================================
*修改标记
*修改时间：2026/9/12 23:05:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Abstractions;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.Core.Downloads;

/// <summary>
/// 下载处理器工厂：按任务格式挑选处理器。
/// </summary>
/// <remarks>
/// <para>
/// 采用「按注册顺序取第一个能处理的」策略，而非用字典按格式硬映射。
/// 原因是格式与处理器的对应关系并非一一对应（例如未来可能出现
/// 「优先用原生下载器、失败再回退 ffmpeg」的复合处理器），
/// 保持有序列表可以让这类组合策略通过调整注册顺序自然实现。
/// </para>
/// <para>
/// 注册顺序约定：<b>更专用的处理器在前</b>。
/// </para>
/// </remarks>
public sealed class DownloadHandlerFactory
{
    private readonly List<IDownloadHandler> _handlers;

    /// <summary>
    /// 初始化工厂。
    /// </summary>
    /// <param name="handlers">按优先级从高到低排列的处理器集合。</param>
    /// <exception cref="ArgumentNullException">处理器集合为 null 时抛出。</exception>
    /// <exception cref="ArgumentException">处理器集合为空时抛出。</exception>
    public DownloadHandlerFactory(IEnumerable<IDownloadHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        _handlers = handlers.ToList();

        if (_handlers.Count == 0)
        {
            throw new ArgumentException("至少需要注册一个下载处理器。", nameof(handlers));
        }
    }

    /// <summary>已注册的处理器数量。</summary>
    public int Count => _handlers.Count;

    /// <summary>
    /// 解析出可处理该任务的下载处理器。
    /// </summary>
    /// <param name="task">下载任务。</param>
    /// <returns>匹配到的处理器。</returns>
    /// <exception cref="NotSupportedException">没有任何处理器能处理该格式时抛出。</exception>
    public IDownloadHandler Resolve(DownloadTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (TryResolve(task, out var handler))
        {
            return handler!;
        }

        throw new NotSupportedException($"没有可处理 {task.Format} 格式的下载处理器。");
    }

    /// <summary>
    /// 尝试解析出可处理该任务的下载处理器。
    /// </summary>
    /// <param name="task">下载任务。</param>
    /// <param name="handler">输出参数：匹配到的处理器；未匹配时为 null。</param>
    /// <returns>匹配成功返回 true。</returns>
    public bool TryResolve(DownloadTask task, out IDownloadHandler? handler)
    {
        ArgumentNullException.ThrowIfNull(task);

        foreach (var candidate in _handlers)
        {
            // CanHandle 被约定为纯函数且不得阻塞，因此这里可以放心逐个探测
            if (candidate.CanHandle(task))
            {
                handler = candidate;
                return true;
            }
        }

        handler = null;
        return false;
    }
}
