/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.App.Panes
*文件名： BrowserTabStrip
*版本号： V1.0.0.0
*唯一标识：0e8fc58c-08ec-4305-8a5b-f5a7cfd5c0a1
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 02:07:00
*描述：浏览器标签条，自绘「标题 + 关闭按钮 + 新建按钮」，负责切换、关闭与新建的命中判定。
*
*=================================================
*修改标记
*修改时间：2026/9/13 02:07:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.ComponentModel;

namespace BrowserVideoGrabber.App.Panes;

/// <summary>
/// 浏览器标签条。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不用 <see cref="TabControl"/></b>：<c>TabControl</c> 会把每个子控件钉死在各自的
/// <c>TabPage</c> 里。多标签浏览器需要「同一个 <c>WebView2</c> 在活动时才挂进可视化树」，
/// 且非活动标签仍要保留页面与嗅探订阅；用 <c>TabControl</c> 反而要不断搬迁控件。
/// 自绘标签条只负责「显示与命中判定」，实际的显示切换交给
/// <see cref="BrowserPane"/>，职责更清晰。
/// </para>
/// <para>
/// 本控件只抛意图（选中 / 关闭 / 新建），不直接操作标签集合，
/// 与项目既有的「面板只抛意图」约定保持一致。
/// </para>
/// </remarks>
public sealed class BrowserTabStrip : Control
{
    private const int TabWidth = 150;
    private const int TabHeight = 26;
    private const int TabGap = 2;
    private const int CloseButtonSize = 16;
    private const int NewButtonWidth = 28;
    private const int TextPadding = 8;

    private readonly List<BrowserTab> _tabs = new();

    private BrowserTab? _activeTab;
    private int _hoverIndex = -1;
    private bool _hoverClose;
    private bool _hoverNew;

    /// <summary>
    /// 初始化标签条。
    /// </summary>
    public BrowserTabStrip()
    {
        Height = 30;
        Dock = DockStyle.Top;

        // 自绘控件必须开启双缓冲，否则鼠标移动时高亮会明显闪烁
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    /// <summary>点击某个标签时触发。</summary>
    public event EventHandler<BrowserTab>? TabSelected;

    /// <summary>点击某个标签的关闭按钮时触发。</summary>
    public event EventHandler<BrowserTab>? TabCloseRequested;

    /// <summary>点击「新建标签」按钮时触发。</summary>
    public event EventHandler? NewTabRequested;

    /// <summary>当前高亮的标签（仅用于绘制）。</summary>
    /// <remarks>
    /// 标记为不参与设计器序列化：<see cref="BrowserTab"/> 不是组件，
    /// 设计器无法为其生成序列化代码，参与序列化只会在打开设计器时报错。
    /// </remarks>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public BrowserTab? ActiveTab
    {
        get => _activeTab;
        set
        {
            _activeTab = value;
            Invalidate();
        }
    }

    /// <summary>标签数量。</summary>
    public int Count => _tabs.Count;

    /// <summary>
    /// 追加一个标签。
    /// </summary>
    /// <param name="tab">标签。</param>
    public void Add(BrowserTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        _tabs.Add(tab);
        Invalidate();
    }

    /// <summary>
    /// 移除一个标签。
    /// </summary>
    /// <param name="tab">标签。</param>
    public void Remove(BrowserTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        _tabs.Remove(tab);

        if (ReferenceEquals(_activeTab, tab))
        {
            _activeTab = null;
        }

        Invalidate();
    }

    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var graphics = e.Graphics;
        graphics.Clear(Color.FromArgb(240, 240, 240));

        for (var index = 0; index < _tabs.Count; index++)
        {
            DrawTab(graphics, _tabs[index], index);
        }

        // 已移除此处的「新建标签」按钮绘制：标签数量受 MaxTabs 限制，
        // 且浏览器主入口已有新建能力（地址栏 / 菜单），这里不再重复
    }

    /// <inheritdoc />
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var hit = HitTest(e.Location);

        if (hit.Index == _hoverIndex && hit.OnClose == _hoverClose && hit.OnNew == _hoverNew)
        {
            return;
        }

        _hoverIndex = hit.Index;
        _hoverClose = hit.OnClose;
        _hoverNew = hit.OnNew;
        Invalidate();
    }

    /// <inheritdoc />
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        _hoverIndex = -1;
        _hoverClose = false;
        _hoverNew = false;
        Invalidate();
    }

    /// <inheritdoc />
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var hit = HitTest(e.Location);

        // 已禁用 OnNew 分支：不再绘制也不再响应新建按钮
        if (hit.OnNew)
        {
            return;
        }

        if (hit.Index < 0 || hit.Index >= _tabs.Count)
        {
            return;
        }

        var tab = _tabs[hit.Index];

        if (hit.OnClose)
        {
            TabCloseRequested?.Invoke(this, tab);
        }
        else
        {
            TabSelected?.Invoke(this, tab);
        }
    }

    /// <summary>
    /// 绘制单个标签。
    /// </summary>
    /// <param name="graphics">绘图对象。</param>
    /// <param name="tab">标签。</param>
    /// <param name="index">序号。</param>
    private void DrawTab(Graphics graphics, BrowserTab tab, int index)
    {
        var bounds = GetTabRect(index);
        var isActive = ReferenceEquals(tab, _activeTab);
        var isHover = index == _hoverIndex && !_hoverClose;

        var backColor = isActive
            ? Color.White
            : isHover
                ? Color.FromArgb(226, 232, 240)
                : Color.FromArgb(232, 232, 232);

        using var background = new SolidBrush(backColor);
        graphics.FillRectangle(background, bounds);

        if (isActive)
        {
            // 活动标签用顶部强调条标示，比单纯换底色更容易一眼定位
            using var accent = new SolidBrush(Color.FromArgb(0, 120, 215));
            graphics.FillRectangle(accent, bounds.Left, bounds.Top, bounds.Width, 3);
        }

        var closeRect = GetCloseRect(index);

        // 标题过长时截断，避免把关闭按钮挤出可视区
        var textBounds = new Rectangle(
            bounds.Left + TextPadding,
            bounds.Top,
            closeRect.Left - bounds.Left - TextPadding - 2,
            bounds.Height);

        TextRenderer.DrawText(
            graphics,
            tab.Title,
            Font,
            textBounds,
            isActive ? Color.FromArgb(30, 30, 30) : Color.FromArgb(90, 90, 90),
            TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        DrawCloseButton(graphics, closeRect, index == _hoverIndex && _hoverClose);
    }

    /// <summary>
    /// 绘制关闭按钮。
    /// </summary>
    /// <param name="graphics">绘图对象。</param>
    /// <param name="bounds">按钮区域。</param>
    /// <param name="hovered">是否鼠标悬停。</param>
    private static void DrawCloseButton(Graphics graphics, Rectangle bounds, bool hovered)
    {
        if (hovered)
        {
            using var hoverBrush = new SolidBrush(Color.FromArgb(220, 90, 80));
            graphics.FillRectangle(hoverBrush, bounds);
        }

        using var pen = new Pen(hovered ? Color.White : Color.FromArgb(110, 110, 110), 1.6f);
        var inset = 5;

        graphics.DrawLine(
            pen,
            bounds.Left + inset,
            bounds.Top + inset,
            bounds.Right - inset,
            bounds.Bottom - inset);

        graphics.DrawLine(
            pen,
            bounds.Right - inset,
            bounds.Top + inset,
            bounds.Left + inset,
            bounds.Bottom - inset);
    }

    /// <summary>
    /// 绘制「新建标签」按钮。
    /// </summary>
    /// <param name="graphics">绘图对象。</param>
    private void DrawNewButton(Graphics graphics)
    {
        var bounds = GetNewButtonRect();

        if (_hoverNew)
        {
            using var hoverBrush = new SolidBrush(Color.FromArgb(214, 226, 240));
            graphics.FillRectangle(hoverBrush, bounds);
        }

        using var pen = new Pen(Color.FromArgb(60, 60, 60), 1.8f);
        var centerX = bounds.Left + bounds.Width / 2;
        var centerY = bounds.Top + bounds.Height / 2;
        var half = 5;

        graphics.DrawLine(pen, centerX - half, centerY, centerX + half, centerY);
        graphics.DrawLine(pen, centerX, centerY - half, centerX, centerY + half);
    }

    /// <summary>
    /// 计算指定序号标签的矩形。
    /// </summary>
    /// <param name="index">序号。</param>
    /// <returns>标签矩形。</returns>
    private Rectangle GetTabRect(int index)
        => new(
            TabGap + index * (TabWidth + TabGap),
            2,
            TabWidth,
            TabHeight);

    /// <summary>
    /// 计算指定序号标签关闭按钮的矩形。
    /// </summary>
    /// <param name="index">序号。</param>
    /// <returns>关闭按钮矩形。</returns>
    private Rectangle GetCloseRect(int index)
    {
        var tab = GetTabRect(index);
        return new Rectangle(
            tab.Right - CloseButtonSize - 4,
            tab.Top + (tab.Height - CloseButtonSize) / 2,
            CloseButtonSize,
            CloseButtonSize);
    }

    /// <summary>
    /// 计算「新建标签」按钮的矩形。
    /// </summary>
    /// <returns>按钮矩形。</returns>
    private Rectangle GetNewButtonRect()
    {
        var left = TabGap + _tabs.Count * (TabWidth + TabGap) + TabGap;
        return new Rectangle(left, 2, NewButtonWidth, TabHeight);
    }

    /// <summary>
    /// 命中测试：判断坐标落在哪个标签 / 关闭按钮 / 新建按钮上。
    /// </summary>
    /// <param name="location">鼠标坐标。</param>
    /// <returns>命中结果。</returns>
    private (int Index, bool OnClose, bool OnNew) HitTest(Point location)
    {
        if (GetNewButtonRect().Contains(location))
        {
            return (-1, false, true);
        }

        for (var index = 0; index < _tabs.Count; index++)
        {
            if (!GetTabRect(index).Contains(location))
            {
                continue;
            }

            return (index, GetCloseRect(index).Contains(location), false);
        }

        return (-1, false, false);
    }
}
