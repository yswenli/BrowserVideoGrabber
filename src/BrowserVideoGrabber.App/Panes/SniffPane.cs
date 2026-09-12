/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.App.Panes
*文件名： SniffPane
*版本号： V1.0.0.0
*唯一标识：26b67124-7d4a-4e93-8c51-9b2f6a3d1e74
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 01:30:00
*描述：右上侧页面视频嗅探面板，列表展示当前页面发现的视频资源。
*
*=================================================
*修改标记
*修改时间：2026/9/13 01:30:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Runtime.InteropServices;
using BrowserVideoGrabber.App.Controls;
using BrowserVideoGrabber.App.Formatting;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.App.Panes;

/// <summary>
/// 页面视频嗅探面板。
/// </summary>
/// <remarks>
/// <para>
/// <b>线程约定</b>：本面板的所有公开方法都必须在 UI 线程调用，自身不做任何跨线程封送。
/// 跨线程封送由 <c>SniffListBinder</c> 负责，这样面板就只是一个纯粹的视图，
/// 可以在设计器中直接摆放与预览。
/// </para>
/// <para>
/// 列表按 <see cref="SniffedVideo.Id"/> 建立索引，重复上报同一资源时原地刷新而不新增行，
/// 从而避免列表因同一地址反复出现而迅速膨胀。
/// </para>
/// </remarks>
public sealed class SniffPane : UserControl
{
    private readonly BufferedListView _listView;
    private readonly ToolStripLabel _countLabel;
    private readonly Dictionary<Guid, ListViewItem> _itemsById = new();
    private readonly ContextMenuStrip _contextMenu;

    /// <summary>
    /// 初始化嗅探面板。
    /// </summary>
    public SniffPane()
    {
        BackColor = Color.FromArgb(250, 250, 250);

        _listView = new BufferedListView { Dock = DockStyle.Fill };
        _listView.AddColumns(
            ("格式", 80),
            ("分辨率", 90),
            ("名称", 220),
            ("来源", 70),
            ("发现时间", 110),
            ("地址", 420));
        _listView.DoubleClick += OnListDoubleClick;
        _listView.MouseUp += OnListMouseUp;

        _countLabel = new ToolStripLabel("共 0 条");

        var clearButton = new ToolStripButton("清空列表")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ToolTipText = "清空已嗅探到的资源；清空后同一资源可以再次被捕获"
        };
        clearButton.Click += (_, _) =>
        {
            ClearItems();

            // 光清空视图是不够的：嗅探器内部的去重索引仍记着这些资源，
            // 不清掉的话「清空后同一资源可以再次被捕获」这句提示就是假的
            ClearRequested?.Invoke(this, EventArgs.Empty);
        };

        var toolbar = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.System,
            Dock = DockStyle.Top
        };
        toolbar.Items.AddRange([new ToolStripLabel("页面视频"), new ToolStripSeparator(), clearButton]);

        _contextMenu = BuildContextMenu();

        Controls.Add(_listView);
        Controls.Add(toolbar);

        UpdateCount();
    }

    /// <summary>请求把某个资源加入下载队列。</summary>
    public event EventHandler<SniffedVideo>? DownloadRequested;

    /// <summary>
    /// 请求清空嗅探器内部的去重记录。
    /// </summary>
    /// <remarks>
    /// 面板只负责视图，无从访问嗅探器，因此把这件事作为意图抛给宿主。
    /// 若不处理该事件，界面上列表已空、而嗅探器仍认为这些资源「已上报过」，
    /// 用户重新播放同一视频时将什么也刷不出来。
    /// </remarks>
    public event EventHandler? ClearRequested;

    /// <summary>列表中当前的资源数量。</summary>
    public int ItemCount => _itemsById.Count;

    /// <summary>
    /// 新增或刷新一条嗅探结果。
    /// </summary>
    /// <param name="video">嗅探结果。</param>
    public void AddOrUpdate(SniffedVideo video)
    {
        ArgumentNullException.ThrowIfNull(video);

        // 同一条资源可能被网络监听与 JS 注入同时捕获，
        // 此时用更完整的一条覆盖（例如网络链路能给出分辨率而 JS 链路不能）
        if (_itemsById.TryGetValue(video.Id, out var existing))
        {
            ApplyFields(existing, video);
            return;
        }

        var item = new ListViewItem(DisplayText.Format(video.Format)) { Tag = video };
        item.SubItems.Add(video.Resolution ?? "-");
        item.SubItems.Add(ToDisplayName(video));
        item.SubItems.Add(ToSourceText(video.Source));
        item.SubItems.Add(video.DetectedAt.ToString("HH:mm:ss"));
        item.SubItems.Add(string.IsNullOrWhiteSpace(video.NormalizedUrl) ? video.Url : video.NormalizedUrl);
        item.ToolTipText = video.Url;

        _itemsById[video.Id] = item;
        _listView.Items.Add(item);
        _listView.EnsureVisible(_listView.Items.Count - 1);

        UpdateCount();
    }

    /// <summary>
    /// 清空视图列表。
    /// </summary>
    /// <remarks>只影响本面板；嗅探器的去重记录由 <see cref="ClearRequested"/> 的订阅方清理。</remarks>
    public void ClearItems()
    {
        _listView.Items.Clear();
        _itemsById.Clear();
        UpdateCount();
    }

    /// <summary>
    /// 从列表中移除一条记录。
    /// </summary>
    /// <param name="id">资源标识。</param>
    /// <returns>移除成功返回 true。</returns>
    public bool RemoveItem(Guid id)
    {
        if (!_itemsById.TryGetValue(id, out var item))
        {
            return false;
        }

        _itemsById.Remove(id);
        _listView.Items.Remove(item);
        UpdateCount();
        return true;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _contextMenu.Dispose();
            _listView.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// 构建右键菜单。
    /// </summary>
    /// <returns>右键菜单实例。</returns>
    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var downloadItem = new ToolStripMenuItem("加入下载(&D)");
        downloadItem.Click += (_, _) => RequestDownloadOfSelected();

        var copyItem = new ToolStripMenuItem("复制链接(&C)");
        copyItem.Click += (_, _) => CopySelectedUrl();

        var removeItem = new ToolStripMenuItem("从列表移除(&R)");
        removeItem.Click += (_, _) => RemoveSelected();

        menu.Items.AddRange([downloadItem, copyItem, new ToolStripSeparator(), removeItem]);
        return menu;
    }

    /// <summary>
    /// 刷新一行的全部列。
    /// </summary>
    /// <param name="item">目标行。</param>
    /// <param name="video">最新数据。</param>
    private static void ApplyFields(ListViewItem item, SniffedVideo video)
    {
        BufferedListView.SetSubItemText(item, 0, DisplayText.Format(video.Format));
        BufferedListView.SetSubItemText(item, 1, video.Resolution ?? "-");
        BufferedListView.SetSubItemText(item, 2, ToDisplayName(video));
        BufferedListView.SetSubItemText(item, 3, ToSourceText(video.Source));
        BufferedListView.SetSubItemText(item, 4, video.DetectedAt.ToString("HH:mm:ss"));
        BufferedListView.SetSubItemText(
            item,
            5,
            string.IsNullOrWhiteSpace(video.NormalizedUrl) ? video.Url : video.NormalizedUrl);

        item.Tag = video;
        item.ToolTipText = video.Url;
    }

    /// <summary>
    /// 生成展示名称。
    /// </summary>
    /// <param name="video">嗅探结果。</param>
    /// <returns>展示名称。</returns>
    private static string ToDisplayName(SniffedVideo video)
    {
        var title = video.DisplayTitle;
        return string.IsNullOrWhiteSpace(title) ? "（未命名）" : title;
    }

    /// <summary>
    /// 把嗅探来源标记转换为中文。
    /// </summary>
    /// <param name="source">来源标记。</param>
    /// <returns>中文来源名。</returns>
    private static string ToSourceText(string source) => source switch
    {
        "network" => "网络",
        "jshook" => "脚本",
        "url" => "特征",
        _ => source
    };

    /// <summary>刷新计数标签。</summary>
    private void UpdateCount() => _countLabel.Text = $"共 {_itemsById.Count} 条";

    /// <summary>列表双击：直接加入下载。</summary>
    private void OnListDoubleClick(object? sender, EventArgs e) => RequestDownloadOfSelected();

    /// <summary>列表右键：弹出菜单。</summary>
    private void OnListMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
        {
            return;
        }

        var item = _listView.GetItemAt(e.X, e.Y);
        if (item is null)
        {
            return;
        }

        item.Selected = true;
        _contextMenu.Show(_listView, e.Location);
    }

    /// <summary>对当前选中项发起下载请求。</summary>
    private void RequestDownloadOfSelected()
    {
        if (_listView.SelectedItems.Count == 0
            || _listView.SelectedItems[0].Tag is not SniffedVideo video)
        {
            return;
        }

        DownloadRequested?.Invoke(this, video);
    }

    /// <summary>复制当前选中项的地址。</summary>
    private void CopySelectedUrl()
    {
        if (_listView.SelectedItems.Count == 0
            || _listView.SelectedItems[0].Tag is not SniffedVideo video)
        {
            return;
        }

        try
        {
            Clipboard.SetText(video.Url);
        }
        catch (ExternalException)
        {
            // 剪贴板被其它进程占用时设置失败，属于可忽略的偶发情况
        }
    }

    /// <summary>从列表移除当前选中项。</summary>
    private void RemoveSelected()
    {
        if (_listView.SelectedItems.Count == 0
            || _listView.SelectedItems[0].Tag is not SniffedVideo video)
        {
            return;
        }

        RemoveItem(video.Id);
    }
}
