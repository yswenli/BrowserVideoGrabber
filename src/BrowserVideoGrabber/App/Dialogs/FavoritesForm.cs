/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.App.Dialogs
*文件名： FavoritesForm
*版本号： V1.0.0.0
*唯一标识：92f4f9ce-7708-4acc-a675-7719b09abcc7
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 02:20:00
*描述：地址收藏管理对话框，支持搜索、打开、改名与删除。
*
*=================================================
*修改标记
*修改时间：2026/9/13 02:20:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.App;
using BrowserVideoGrabber.App.Dialogs;
using BrowserVideoGrabber.Core.Abstractions;
using BrowserVideoGrabber.Core.Models;

namespace BrowserVideoGrabber.App.Dialogs;

/// <summary>
/// 地址收藏管理对话框。
/// </summary>
/// <remarks>
/// <para>
/// 本窗体只做「展示与编辑」，<b>不负责导航</b>：用户选择打开某条收藏时以
/// <see cref="OpenRequested"/> 抛出网址，由主窗体决定在哪个标签打开。
/// 这样对话框无需知道标签页的存在，职责保持单一。
/// </para>
/// <para>
/// 数据直接读写 <see cref="IFavoritesRepository"/>，每次操作后立即落盘，
/// 因此程序被强制结束时最多丢失最后一次操作。
/// </para>
/// </remarks>
public sealed class FavoritesForm : Form
{
    private readonly IFavoritesRepository _repository;
    private readonly ListView _list = new() { Dock = DockStyle.Fill, FullRowSelect = true, MultiSelect = false };
    private readonly TextBox _searchBox = new() { Dock = DockStyle.Fill, PlaceholderText = "搜索名称或网址…" };
    private readonly ContextMenuStrip _contextMenu = new();

    private List<FavoriteEntry> _items = new();

    /// <summary>
    /// 初始化收藏对话框。
    /// </summary>
    /// <param name="repository">收藏仓储。</param>
    public FavoritesForm(IFavoritesRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

        Text = "地址收藏";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 460);
        MinimumSize = new Size(520, 320);
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Font;
        Icon = AppIcon.Load();

        _list.View = View.Details;
        _list.Columns.Add("名称", 200);
        _list.Columns.Add("网址", 380);
        _list.Columns.Add("添加时间", 140);
        _list.DoubleClick += (_, _) => OpenSelected();
        _list.MouseUp += OnListMouseUp;

        BuildContextMenu();

        _searchBox.TextChanged += (_, _) => ApplyFilter();

        var searchPanel = new Panel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(8) };
        searchPanel.Controls.Add(_searchBox);

        // 用 FlowLayoutPanel 而非普通 Panel：Panel 不给子控件排布位置，
        // 直接 Add 会让四个按钮全部叠在 (0,0)。RightToLeft 让「关闭」靠右，
        // 故按视觉反序添加，屏幕上从左到右才是 打开 / 改名 / 删除 / 关闭。
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            Padding = new Padding(8),
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };

        var openButton = CreateButton("打开", OpenSelected);
        var renameButton = CreateButton("改名", RenameSelected);
        var deleteButton = CreateButton("删除", DeleteSelected);
        var closeButton = CreateButton("关闭", Close);

        buttonPanel.Controls.Add(closeButton);
        buttonPanel.Controls.Add(deleteButton);
        buttonPanel.Controls.Add(renameButton);
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
    /// 与 <see cref="HistoryForm"/> 保持一致：右键删除 / 改名是最高频的操作，
    /// 放菜单里比先选中再点底部按钮更顺手。菜单由 <see cref="OnListMouseUp"/> 按需弹出，
    /// 未点中行时不显示，避免点了「删除」却什么都没发生。
    /// </remarks>
    private void BuildContextMenu()
    {
        var openItem = new ToolStripMenuItem("打开");
        openItem.Click += (_, _) => OpenSelected();

        var renameItem = new ToolStripMenuItem("改名");
        renameItem.Click += (_, _) => RenameSelected();

        var deleteItem = new ToolStripMenuItem("删除");
        deleteItem.Click += (_, _) => DeleteSelected();

        _contextMenu.Items.AddRange([openItem, renameItem, new ToolStripSeparator(), deleteItem]);
    }

    /// <summary>
    /// 右键点击列表：先选中光标下的行，再弹出菜单。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">鼠标事件参数。</param>
    /// <remarks>
    /// 必须先改选中项再弹菜单：沿用旧选中项会出现「右键第 5 行却删掉第 1 行」的误删，
    /// 这与 Windows 资源管理器「右键谁就操作谁」的习惯相悖。
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
    /// 过滤在内存中进行而不重新读盘：收藏条目数量很小，
    /// 每次按键都读一次 JSON 反而会让输入变卡。
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
            row.SubItems.Add(item.AddedAt.ToString("yyyy-MM-dd HH:mm"));
            row.Tag = item;

            _list.Items.Add(row);
        }

        _list.EndUpdate();
    }

    /// <summary>打开选中的收藏。</summary>
    private void OpenSelected()
    {
        if (GetSelected() is not { } entry)
        {
            return;
        }

        OpenRequested?.Invoke(this, entry.Url);
        Close();
    }

    /// <summary>为选中的收藏改名。</summary>
    private void RenameSelected()
    {
        if (GetSelected() is not { } entry)
        {
            return;
        }

        using var dialog = new TextInputDialog("收藏改名", "名称：", entry.Title);

        if (dialog.ShowDialog(this) != DialogResult.OK
            || string.IsNullOrWhiteSpace(dialog.Value))
        {
            return;
        }

        _repository.Rename(entry.Id, dialog.Value.Trim());
        Reload();
    }

    /// <summary>删除选中的收藏。</summary>
    private void DeleteSelected()
    {
        if (GetSelected() is not { } entry)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"确定要从收藏中移除吗？{Environment.NewLine}{entry.Title}",
            "删除收藏",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (answer != DialogResult.Yes)
        {
            return;
        }

        _repository.Remove(entry.Id);
        Reload();
    }

    /// <summary>
    /// 获取列表中选中的条目。
    /// </summary>
    /// <returns>选中的收藏；未选中返回 null。</returns>
    private FavoriteEntry? GetSelected()
        => _list.SelectedItems.Count == 0 ? null : _list.SelectedItems[0].Tag as FavoriteEntry;

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
