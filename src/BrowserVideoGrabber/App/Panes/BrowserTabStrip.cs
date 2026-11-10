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

    /// <summary>横向滚动条高度（px）。刻意做得非常窄，只在标签放不下时出现。</summary>
    private const int ScrollbarHeight = 6;

    /// <summary>滚动条 thumb 的最小宽度（px），保证可点击。</summary>
    private const int MinThumbWidth = 30;

    private readonly List<BrowserTab> _tabs = new();

    private BrowserTab? _activeTab;
    private int _hoverIndex = -1;
    private bool _hoverClose;
    private bool _hoverNew;

    /// <summary>横向滚动偏移（px），内容超出视口时生效。</summary>
    private int _scrollOffset;

    /// <summary>滚动条 thumb 是否正被拖动。</summary>
    private bool _thumbDragging;

    /// <summary>拖动开始时的鼠标 X 与滚动偏移，用于计算拖动位移。</summary>
    private int _dragStartX;
    private int _dragStartOffset;

    /// <summary>
    /// 初始化标签条。
    /// </summary>
    public BrowserTabStrip()
    {
        // 2px 顶距 + 26px 标签 + 2px 缝隙 + 6px 滚动条 = 36
        Height = 36;
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

            if (value is not null)
            {
                EnsureTabVisible(value);
            }

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
        EnsureTabVisible(tab);
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

        // 标签变少后偏移可能越界，钳回来
        ScrollTo(_scrollOffset);
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
            var bounds = GetTabRect(index);

            // 完全落在视口外的标签不绘制（滚动后左右两侧都会有）
            if (bounds.Right < 0 || bounds.Left > ClientSize.Width)
            {
                continue;
            }

            DrawTab(graphics, _tabs[index], index);
        }

        if (NeedsScrollbar)
        {
            DrawScrollbar(graphics);
        }

        // 已移除此处的「新建标签」按钮绘制：标签数量受 MaxTabs 限制，
        // 且浏览器主入口已有新建能力（地址栏 / 菜单），这里不再重复
    }

    /// <inheritdoc />
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        // thumb 拖动中：按拖动位移映射滚动偏移，不参与标签悬停判定
        if (_thumbDragging)
        {
            if (!NeedsScrollbar)
            {
                _thumbDragging = false;
                return;
            }

            var thumbWidth = GetThumbRect().Width;
            var maxThumbX = GetScrollbarRect().Width - thumbWidth;

            if (maxThumbX > 0)
            {
                var startThumbX = (int)((long)_dragStartOffset * maxThumbX / MaxScrollOffset);
                var newThumbX = Math.Clamp(startThumbX + (e.X - _dragStartX), 0, maxThumbX);
                ScrollTo((int)((long)newThumbX * MaxScrollOffset / maxThumbX));
            }

            return;
        }

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
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _thumbDragging = false;
    }

    /// <inheritdoc />
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (!NeedsScrollbar)
        {
            return;
        }

        // 每格滚动约 3 个标签宽；向上滚 → 内容向左（偏移减小）
        var step = (TabWidth + TabGap) * 3;
        ScrollTo(_scrollOffset - Math.Sign(e.Delta) * step);
    }

    /// <inheritdoc />
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        // 视口变宽后偏移可能超出新范围，钳回来
        ScrollTo(_scrollOffset);
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

        // 滚动条优先于标签命中：两者在垂直方向不重叠，这里只是把拖动与轨道点击分流
        if (NeedsScrollbar && GetScrollbarRect().Contains(e.Location))
        {
            var thumb = GetThumbRect();

            if (thumb.Contains(e.Location))
            {
                _thumbDragging = true;
                _dragStartX = e.X;
                _dragStartOffset = _scrollOffset;
            }
            else
            {
                // 点击轨道：把 thumb 中心移动到点击位置（thumb 顶满轨道时无处可滚）
                var maxThumbX = GetScrollbarRect().Width - thumb.Width;

                if (maxThumbX > 0)
                {
                    var targetThumbX = Math.Clamp(e.X - thumb.Width / 2, 0, maxThumbX);
                    ScrollTo((int)((long)targetThumbX * MaxScrollOffset / maxThumbX));
                }
            }

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
            TabGap + index * (TabWidth + TabGap) - _scrollOffset,
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
        var left = TabGap + _tabs.Count * (TabWidth + TabGap) + TabGap - _scrollOffset;
        return new Rectangle(left, 2, NewButtonWidth, TabHeight);
    }

    /// <summary>标签内容的总宽度（px，含两侧留白）。</summary>
    private int TotalWidth => TabGap + _tabs.Count * (TabWidth + TabGap) + TabGap;

    /// <summary>最大可滚动偏移（px）；内容未溢出时为 0。</summary>
    private int MaxScrollOffset => Math.Max(0, TotalWidth - ClientSize.Width);

    /// <summary>内容是否溢出视口（决定是否绘制滚动条）。</summary>
    private bool NeedsScrollbar => MaxScrollOffset > 0;

    /// <summary>滚动条轨道矩形（控制条最底部）。</summary>
    private Rectangle GetScrollbarRect()
        => new(0, Height - ScrollbarHeight, ClientSize.Width, ScrollbarHeight);

    /// <summary>
    /// 计算 thumb（滑块）矩形：宽度按可视比例缩放，位置按当前偏移映射。
    /// </summary>
    private Rectangle GetThumbRect()
    {
        var track = GetScrollbarRect();
        var view = Math.Max(1, ClientSize.Width);

        var thumbWidth = Math.Clamp((int)((long)view * track.Width / TotalWidth), MinThumbWidth, track.Width);
        var maxThumbX = track.Width - thumbWidth;
        var maxScroll = MaxScrollOffset;

        var thumbX = maxScroll == 0
            ? 0
            : (int)((long)_scrollOffset * maxThumbX / maxScroll);

        return new Rectangle(track.Left + thumbX, track.Top + 1, thumbWidth, track.Height - 2);
    }

    /// <summary>把滚动偏移限制在合法范围并刷新。</summary>
    private void ScrollTo(int offset)
    {
        var clamped = Math.Clamp(offset, 0, MaxScrollOffset);

        if (clamped == _scrollOffset)
        {
            return;
        }

        _scrollOffset = clamped;
        Invalidate();
    }

    /// <summary>确保指定标签完整落在视口内（必要时滚动）。</summary>
    /// <param name="tab">目标标签。</param>
    private void EnsureTabVisible(BrowserTab tab)
    {
        var index = _tabs.IndexOf(tab);
        if (index < 0 || MaxScrollOffset == 0)
        {
            return;
        }

        // rect 基于当前偏移计算：右缘超界则右滚，左缘出界则左滚
        var rect = GetTabRect(index);
        var offset = _scrollOffset;

        if (rect.Right > ClientSize.Width)
        {
            offset += rect.Right - ClientSize.Width;
        }

        if (rect.Left < 0)
        {
            offset += rect.Left;
        }

        ScrollTo(offset);
    }

    /// <summary>绘制底部窄滚动条（仅在内容溢出时由 <see cref="OnPaint"/> 调用）。</summary>
    private void DrawScrollbar(Graphics graphics)
    {
        var track = GetScrollbarRect();

        using var trackBrush = new SolidBrush(Color.FromArgb(224, 224, 224));
        graphics.FillRectangle(trackBrush, track);

        var thumb = GetThumbRect();

        using var thumbBrush = new SolidBrush(Color.FromArgb(150, 150, 150));
        graphics.FillRectangle(thumbBrush, thumb);
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
