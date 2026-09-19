/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.App.Controls
*文件名： TrayMenuRenderer
*版本号： V1.0.0.0
*唯一标识：b3f5c1a2-7e44-4d18-9c06-1a2b3c4d5e6f
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/19 22:54:00
*描述：给托盘右键菜单（ContextMenuStrip）绘制扁平、现代风格的菜单项，
*      视觉对齐 WorkBuddy 的整体风格：白色菜单底、1px 浅灰细边框、
*      品牌紫（#4B3FE3）悬浮高亮（圆角胶囊）、品牌紫文字，
*      取代 WinForms 默认的灰色 3D 下拉菜单外观。
*
*=================================================
*修改标记
*修改时间：2026/9/19 22:54:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using System.Drawing.Drawing2D;

namespace BrowserVideoGrabber.App.Controls;

/// <summary>
/// 托盘菜单的现代扁平渲染器。
/// </summary>
/// <remarks>
/// <para>
/// 继承 <see cref="ToolStripProfessionalRenderer"/> 并注入一套自定义
/// <see cref="ProfessionalColorTable"/>（见 <see cref="TrayColorTable"/>），
/// 把默认下拉菜单的灰色渐变背景、3D 边框全部换掉：
/// 菜单底是纯白、边框是 1px 浅灰、悬浮项是品牌紫色浅填充的圆角胶囊。
/// </para>
/// <para>
/// 品牌色与工具栏的 <see cref="CapsuleToolStripRenderer"/> 保持一致（#4B3FE3），
/// 保证托盘菜单和主窗体工具栏是同一套设计语言；悬浮文字也染成品牌紫，
/// 强化「这是本应用的菜单」的视觉归属。
/// </para>
/// <para>
/// 左下角不保留图片列（<c>ShowImageMargin = false</c>），图标直接内联在文字左侧，
/// 菜单因此更干净、不留空白带。
/// </para>
/// </remarks>
public sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
{
    /// <summary>品牌紫色，用于悬浮文字与胶囊高亮的基色（与工具栏一致）。</summary>
    private static readonly Color BrandColor = Color.FromArgb(75, 63, 227);   // #4B3FE3

    /// <summary>品牌紫色的浅色，悬浮项的圆角胶囊填充色。</summary>
    private static readonly Color BrandSoft = Color.FromArgb(242, 247, 255);

    /// <summary>菜单背景色：纯白。</summary>
    private static readonly Color MenuBack = Color.White;

    /// <summary>菜单边框与分隔线颜色：浅灰，替代默认的 3D 灰边。</summary>
    private static readonly Color Hairline = Color.FromArgb(225, 227, 232);

    /// <summary>普通（非悬浮）菜单文字颜色：近黑灰，保证可读性。</summary>
    private static readonly Color ItemText = Color.FromArgb(40, 40, 45);

    /// <summary>
    /// 初始化渲染器，注入托盘专用的配色表。
    /// </summary>
    public TrayMenuRenderer() : base(new TrayColorTable())
    {
    }

    /// <summary>
    /// 菜单背景：纯白平铺，去掉默认渐变。
    /// </summary>
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var brush = new SolidBrush(MenuBack);
        g.FillRectangle(brush, e.AffectedBounds);
    }

    /// <summary>
    /// 菜单外边框：1px 浅灰细线，取代默认的立体灰边。
    /// </summary>
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is not ContextMenuStrip)
        {
            base.OnRenderToolStripBorder(e);
            return;
        }

        var g = e.Graphics;
        var rect = new Rectangle(
            e.AffectedBounds.X,
            e.AffectedBounds.Y,
            e.AffectedBounds.Width - 1,
            e.AffectedBounds.Height - 1);

        using var pen = new Pen(Hairline, 1);
        g.DrawRectangle(pen, rect);
    }

    /// <summary>
    /// 菜单项背景：仅悬浮态绘制圆角胶囊高亮；未悬浮时透明，露出白色菜单底。
    /// </summary>
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item is not ToolStripMenuItem item || !item.Selected)
        {
            base.OnRenderMenuItemBackground(e);
            return;
        }

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // 整行内缩 2px，画成「悬浮在白色底上的圆角胶囊」，与 WorkBuddy 菜单观感一致
        var row = e.Item.Bounds;
        var rect = new Rectangle(row.X + 2, row.Y + 1, row.Width - 4, row.Height - 2);
        var radius = (rect.Height - 2) / 2;

        using var path = RoundedRect(rect, radius);
        using var brush = new SolidBrush(BrandSoft);
        g.FillPath(brush, path);
    }

    /// <summary>
    /// 菜单文字：悬浮态染成品牌紫，其余保持近黑灰。
    /// </summary>
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item is ToolStripMenuItem { Selected: true })
        {
            e.TextColor = BrandColor;
        }
        else
        {
            e.TextColor = ItemText;
        }

        base.OnRenderItemText(e);
    }

    /// <summary>
    /// 分隔线：居中一条浅灰细线，替代默认的凹凸双线。
    /// </summary>
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        if (e.Item is not ToolStripMenuItem item)
        {
            base.OnRenderSeparator(e);
            return;
        }

        var g = e.Graphics;
        var rect = item.Bounds;
        var y = rect.Y + (rect.Height / 2);

        using var pen = new Pen(Hairline, 1);
        g.DrawLine(pen, rect.X + 2, y, rect.Right - 2, y);
    }

    /// <summary>
    /// 不绘制左侧图片列（<c>ShowImageMargin = false</c> 已由调用方设置），保持菜单纯净。
    /// </summary>
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
    }

    /// <summary>
    /// 圆角矩形辅助方法。
    /// </summary>
    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var diameter = radius * 2;
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

        // 左上
        path.AddArc(arc, 180, 90);
        // 右上
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        // 右下
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        // 左下
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);

        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// 托盘菜单专用配色表：把所有默认渐变/3D 灰都替换为扁平白 + 浅灰。
    /// </summary>
    private sealed class TrayColorTable : ProfessionalColorTable
    {
        /// <inheritdoc />
        public override Color ToolStripDropDownBackground => MenuBack;

        /// <inheritdoc />
        public override Color MenuBorder => Hairline;

        /// <inheritdoc />
        public override Color MenuItemBorder => Color.Transparent;

        /// <inheritdoc />
        public override Color MenuItemSelected => BrandSoft;

        /// <inheritdoc />
        public override Color MenuItemSelectedGradientBegin => BrandSoft;

        /// <inheritdoc />
        public override Color MenuItemSelectedGradientEnd => BrandSoft;

        /// <inheritdoc />
        public override Color ImageMarginGradientBegin => MenuBack;

        /// <inheritdoc />
        public override Color ImageMarginGradientMiddle => MenuBack;

        /// <inheritdoc />
        public override Color ImageMarginGradientEnd => MenuBack;

        /// <inheritdoc />
        public override Color SeparatorDark => Hairline;

        /// <inheritdoc />
        public override Color SeparatorLight => Hairline;
    }
}
