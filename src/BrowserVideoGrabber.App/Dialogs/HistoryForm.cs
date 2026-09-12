/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.App.Dialogs
*文件名： HistoryForm
*版本号： V1.0.0.0
*唯一标识：5aa5257b-f340-460c-bc57-dd4a1b667341
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 02:24:00
*描述：历史记录管理对话框，支持搜索、打开、删除单条与清空。
*
*=================================================
*修改标记
*修改时间：2026/9/13 02:24:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.Core.Abstractions;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.App.Dialogs;

/// <summary>
/// 历史记录管理对话框。
/// </summary>
/// <remarks>
/// 与 <see cref="FavoritesForm"/> 保持同样的分工：只负责展示与编辑，
/// 打开某条历史时以 <see cref="OpenRequested"/> 抛出网址，由主窗体决定在哪个标签打开。
/// </remarks>
public sealed class HistoryForm : Form
{
    private readonly IHistoryRepository _repository;
    private readonly ListView _list = new() { Dock = DockStyle.Fill, FullRowSelect = true, MultiSelect = false };
    private readonly TextBox _searchBox = new() { Dock = DockStyle.Fill, PlaceholderText = "搜索标题或网址…" };
    private readonly ContextMenuStrip _contextMenu = new();

    private List<HistoryEntry> _items = new();

    /// <summary>
    /// 初始化历史对话框。
    /// </summary>
    /// <param name="repository">历史仓储。</param>
    public HistoryForm(IHistoryRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

        Text = "历史记录";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 460);
        MinimumSize = new Size(520, 320);
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Font;
        Icon = AppIcon.Load();

        _list.View = View.Details;
        _list.Columns.Add("标题", 200);
        _list.Columns.Add("网址", 380);
        _list.Columns.Add("访问时间", 140);
        _list.DoubleClick += (_, _) => OpenSelected();
        _list.MouseUp += OnListMouseUp;

        BuildContextMenu();

        _searchBox.TextChanged += (_, _) => ApplyFilter();

        var searchPanel = new Panel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(8) };
        searchPanel.Controls.Add(_searchBox);

        var openButton = CreateButton("打开", OpenSelected);
        var deleteButton = CreateButton("删除", DeleteSelected);
        var clearButton = CreateButton("清空", ClearAll);
        var closeButton = CreateButton("关闭", Close);

        // 用 FlowLayoutPanel 而非普通 Panel：Panel 不会给子控件排布位置，
        // 直接 Add 会让四个按钮全部叠在 (0,0)。RightToLeft 让「关闭」靠右，
        // 于是要按视觉反序添加，屏幕上从左到右才是 打开 / 删除 / 清空 / 关闭。
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            Padding = new Padding(8),
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };

        buttonPanel.Controls.Add(closeButton);
        buttonPanel.Controls.Add(clearButton);
        buttonPanel.Controls.Add(deleteButton);
        buttonPanel.Controls.Add(openButton);

        Controls.Add(_list);
        Controls.Add(buttonPanel);
        Controls.Add(searchPanel);

        AcceptButton = openButton;
        CancelButton = closeButton;

        Load += (_, _) => Reload();
    }

    /// <summary>用户请求打开某个网址时触发。</summary>
    public event EventHandler<string>? OpenRequested;

    /// <summary>
    /// 重新载入并应用当前筛选。
    /// </summary>
    public void Reload()
    {
        _items = _repository.Load().ToList();
        ApplyFilter();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _list.Dispose();
            _searchBox.Dispose();
            _contextMenu.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// 构建列表右键菜单。
    /// </summary>
    /// <remarks>
    /// 右键删除是最高频的操作，放在菜单里比让人先选中再点底部按钮更顺手。
    /// 菜单在 <see cref="OnListMouseUp"/> 中按需弹出，未点中行时不显示，
    /// 避免出现「删除」点了却什么都没发生的困惑。
    /// </remarks>
    private void BuildContextMenu()
    {
        var openItem = new ToolStripMenuItem("打开");
        openItem.Click += (_, _) => OpenSelected();

        var deleteItem = new ToolStripMenuItem("删除");
        deleteItem.Click += (_, _) => DeleteSelected();

        _contextMenu.Items.AddRange([openItem, new ToolStripSeparator(), deleteItem]);
    }

    /// <summary>
    /// 右键点击列表：先选中光标下的行，再弹出菜单。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">鼠标事件参数。</param>
    /// <remarks>
    /// 必须先改选中项再弹菜单：Windows 资源管理器的习惯是「右键谁就操作谁」，
    /// 若沿用旧的选中项，用户右键了第 5 行却删掉第 1 行，属于极易误删的设计。
    /// </remarks>
    private void OnListMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
        {
            return;
        }

        var hit = _list.HitTest(e.Location).Item;

        if (hit is null)
        {
            return;
        }

        hit.Selected = true;
        _contextMenu.Show(_list, e.Location);
    }

    /// <summary>
    /// 按搜索词过滤列表。
    /// </summary>
    /// <remarks>
    /// 历史上限是 500 条，内存过滤足够快；每次按键读盘反而会让输入变卡。
    /// </remarks>
    private void ApplyFilter()
    {
        var keyword = _searchBox.Text.Trim();

        var visible = string.IsNullOrWhiteSpace(keyword)
            ? _items
            : _items.Where(x =>
                x.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || x.Url.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();

        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (var item in visible)
        {
            var row = new ListViewItem(item.Title);
            row.SubItems.Add(item.Url);
            row.SubItems.Add(item.VisitedAt.ToString("yyyy-MM-dd HH:mm"));
            row.Tag = item;

            _list.Items.Add(row);
        }

        _list.EndUpdate();
    }

    /// <summary>打开选中的历史。</summary>
    private void OpenSelected()
    {
        if (GetSelected() is not { } entry)
        {
            return;
        }

        OpenRequested?.Invoke(this, entry.Url);
        Close();
    }

    /// <summary>删除选中的历史。</summary>
    private void DeleteSelected()
    {
        if (GetSelected() is not { } entry)
        {
            return;
        }

        _repository.Remove(entry.Id);
        Reload();
    }

    /// <summary>清空全部历史。</summary>
    private void ClearAll()
    {
        if (_items.Count == 0)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"确定要清空全部 {_items.Count} 条历史记录吗？此操作不可撤销。",
            "清空历史",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (answer != DialogResult.Yes)
        {
            return;
        }

        _repository.Clear();
        Reload();
    }

    /// <summary>
    /// 获取列表中选中的条目。
    /// </summary>
    /// <returns>选中的历史；未选中返回 null。</returns>
    private HistoryEntry? GetSelected()
        => _list.SelectedItems.Count == 0 ? null : _list.SelectedItems[0].Tag as HistoryEntry;

    /// <summary>
    /// 创建底部按钮。
    /// </summary>
    /// <param name="text">按钮文本。</param>
    /// <param name="onClick">点击回调。</param>
    /// <returns>按钮实例。</returns>
    private static Button CreateButton(string text, Action onClick)
    {
        var button = new Button
        {
            Text = text,
            Width = 88,
            Height = 28,
            FlatStyle = FlatStyle.System
        };

        button.Click += (_, _) => onClick();
        return button;
    }
}
