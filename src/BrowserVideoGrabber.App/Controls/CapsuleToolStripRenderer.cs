/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.App.Controls
*文件名： CapsuleToolStripRenderer.cs
*版本号： V1.0.0.0
*唯一标识：
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13
*描述：给 ToolStripButton 绘制「胶囊」风格的悬停/选中背景，
*      视觉上接近现代应用的扁平化按钮。
*
*****************************************************************************/

using System.Drawing.Drawing2D;

namespace BrowserVideoGrabber.App.Controls;

/// <summary>
/// 胶囊风格的 ToolStrip 渲染器。
/// </summary>
/// <remarks>
/// <para>
/// 重写 <see cref="ToolStripProfessionalRenderer.OnRenderButtonBackground"/>，
/// 把 ToolStripButton 的悬停/按下/选中态绘制为圆角矩形（胶囊），
/// 配上浅灰或品牌紫色背景。
/// </para>
/// <para>
/// 未选中的普通按钮只在悬停时显示浅灰边框 + 浅灰填充；
/// <see cref="ToolStripButton.Checked"/> 的按钮用品牌紫色填充 ——
/// 嗅探开关这类「当前激活中」的按钮靠这个传递状态。
/// </para>
/// <para>
/// 其他 ToolStrip 元素（Separator、TextBox、StatusLabel 等）保持系统默认渲染。
/// </para>
/// </remarks>
public sealed class CapsuleToolStripRenderer : ToolStripProfessionalRenderer
{
    /// <summary>品牌紫色，用于 Checked / 主操作按钮的高亮背景。</summary>
    private static readonly Color BrandColor = Color.FromArgb(75, 63, 227);   // #4B3FE3

    /// <summary>品牌紫色的浅色（悬停/选中时的填充色）。</summary>
    private static readonly Color BrandSoft = Color.FromArgb(242, 247, 255);

    /// <summary>普通按钮悬停时的边框颜色。</summary>
    private static readonly Color HoverBorder = Color.FromArgb(200, 200, 210);

    /// <summary>普通按钮悬停时的填充色。</summary>
    private static readonly Color HoverFill = Color.FromArgb(242, 242, 244);

    /// <summary>普通按钮按下时的填充色。</summary>
    private static readonly Color PressedFill = Color.FromArgb(228, 228, 232);

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        // 只对普通工具栏上的按钮绘制胶囊风格；下拉菜单（右键菜单）保持系统默认渲染
        if (e.Item is not ToolStripButton button || button.Owner is null || button.Owner.IsDropDown)
        {
            base.OnRenderButtonBackground(e);
            return;
        }

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 2, button.Width, button.Height - 4);
        var radius = Math.Min(rect.Height / 2, 10);

        bool isHover = button.Selected;
        bool isPressed = button.Pressed;
        bool isChecked = button.Checked;
        bool isEnabled = button.Enabled;

        if (!isEnabled)
        {
            // 禁用态：什么都不画，只让文字/图标以灰色显示
            return;
        }

        using var path = RoundedRect(rect, radius);

        if (isChecked)
        {
            // 选中态（如嗅探开）：品牌紫色填充
            using var brush = new SolidBrush(BrandColor);
            g.FillPath(brush, path);
        }
        else if (isPressed)
        {
            using var brush = new SolidBrush(PressedFill);
            g.FillPath(brush, path);
            using var pen = new Pen(HoverBorder);
            g.DrawPath(pen, path);
        }
        else if (isHover)
        {
            using var brush = new SolidBrush(HoverFill);
            g.FillPath(brush, path);
            using var pen = new Pen(HoverBorder);
            g.DrawPath(pen, path);
        }
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
    /// 给 ToolStripButton 创建一个带 emoji/Unicode 图标的 16x16 Bitmap。
    /// </summary>
    /// <param name="glyph">图标字符（建议 1 个 emoji 或 Unicode 符号）。</param>
    /// <returns>16x16 的 Bitmap，透明背景上居中绘制 glyph。</returns>
    public static Bitmap CreateGlyphIcon(string glyph)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // emoji 字体渲染：Segoe UI Emoji 在 Windows 上能正确显示大多数符号
        using var font = new Font("Segoe UI Emoji", 11f, FontStyle.Regular, GraphicsUnit.Point);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        var rect = new RectangleF(0, 0, 16, 16);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.DrawString(glyph, font, Brushes.Black, rect, sf);

        return bmp;
    }
}
