/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.App.Panes
*文件名： BrowserTabNavigatedEventArgs
*版本号： V1.0.0.0
*唯一标识：ead23725-7ea6-4ee8-945d-11f2c61f92f2
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 02:06:00
*描述：标签页导航完成事件参数，携带来源标签、地址与成功标记。
*
*=================================================
*修改标记
*修改时间：2026/9/13 02:06:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

namespace BrowserVideoGrabber.App.Panes;

/// <summary>
/// 标签页导航完成事件参数。
/// </summary>
/// <remarks>
/// 携带 <see cref="Tab"/> 是因为多标签下同一个事件会被所有标签共用一条处理链，
/// 处理方必须能判断「这次导航来自哪个标签」，否则非活动标签的导航也会被写进历史。
/// </remarks>
public sealed class BrowserTabNavigatedEventArgs : EventArgs
{
    /// <summary>
    /// 初始化事件参数。
    /// </summary>
    /// <param name="tab">发起导航的标签。</param>
    /// <param name="url">导航后的地址。</param>
    /// <param name="isSuccess">导航是否成功。</param>
    public BrowserTabNavigatedEventArgs(BrowserTab tab, string url, bool isSuccess)
    {
        Tab = tab;
        Url = url;
        IsSuccess = isSuccess;
    }

    /// <summary>发起导航的标签。</summary>
    public BrowserTab Tab { get; }

    /// <summary>导航后的地址。</summary>
    public string Url { get; }

    /// <summary>导航是否成功。</summary>
    public bool IsSuccess { get; }
}
