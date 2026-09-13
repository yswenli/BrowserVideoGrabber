/****************************************************************************
*Copyright (c) 2026 RiverLand All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.App.Dialogs
*文件名： AboutForm
*版本号： V1.0.0.0
*唯一标识：9d3b8a67-2f14-4c6e-9a25-7f5e2c4b8d31
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 12:40:00
*描述：主菜单「关于」对话框，展示程序名称、简介、版本与作者等信息。
*
*=================================================
*修改标记
*修改时间：2026/9/13 12:40:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.App;

using System.Reflection;

namespace BrowserVideoGrabber.App.Dialogs;

/// <summary>
/// 「关于」对话框。
/// </summary>
/// <remarks>
/// 信息全部取自常量与程序集元数据，不依赖任何外部资源：
/// 版本从 <see cref="Assembly"/> 读取（多项目装配下不会把版本写死在两处），
/// 版本读取失败时回退为 <see cref="FallbackVersion"/>，保证对话框任何情况下都能弹出。
/// </remarks>
public sealed class AboutForm : Form
{
    /// <summary>程序集版本读取失败时的兜底版本号。</summary>
    private const string FallbackVersion = "1.0.0.0";

    /// <summary>程序名称。</summary>
    private const string ProductTitle = "BrowserVideoGrabber · 页面视频嗅探下载器";

    /// <summary>程序简介。含 <see cref="Environment.NewLine"/>，必须是运行期字段而非编译期常量。</summary>
    private static readonly string ProductDescription =
        "一款内置浏览器的一体化视频嗅探下载工具。" + Environment.NewLine + Environment.NewLine +
        "核心特性：" + Environment.NewLine +
        "• 自动嗅探页面中的视频与直播流（HLS / DASH / Smooth Streaming / MP4），" + Environment.NewLine +
        "  多标签并发浏览、独立索引去重，开发者工具能看到的地址它就能抓到。" + Environment.NewLine +
        "• HLS 分片级下载：AES-128 自动解密、分片级 HTTP Range 校验、断点续传，" + Environment.NewLine +
        "  最后由 ffmpeg 封装成一个完整的 MP4 文件。" + Environment.NewLine +
        "• MP4 直链支持多线程分片并发下载，显著缩短大文件下载时间。" + Environment.NewLine +
        "• 多标签内置浏览器，共享同一份 Cookie 与登录态。" + Environment.NewLine + Environment.NewLine +
        "技术栈：.NET 10 / WinForms / Microsoft.Web.WebView2 / ffmpeg。";

    /// <summary>免责声明文本。</summary>
    private static readonly string DisclaimerText =
        "⚠ 本软件仅供技术学习、研究与个人合法用途。" + Environment.NewLine + Environment.NewLine +
        "1. 用户通过本软件获取和下载的任何内容（包括但不限于视频、音频、直播流），" +
        "必须遵守中华人民共和国相关法律法规、遵守内容源网站的用户协议与版权政策。" + Environment.NewLine +
        "2. 严禁将本软件用于侵犯他人知识产权、商业牟利、传播淫秽或违反公序良俗的内容、" +
        "或从事其他任何违法违规活动。" + Environment.NewLine +
        "3. 因用户违规使用本软件而产生的一切直接或间接法律责任，由用户本人独立承担，" +
        "本软件作者及相关方不承担任何连带责任。" + Environment.NewLine +
        "4. 本软件嗅探到的视频地址、加密参数等信息均来源于用户合法访问的页面，" +
        "作者不对嗅探结果的准确性、可用性、完整性做任何明示或默示的保证。" + Environment.NewLine +
        "5. 如您在使用过程中发现任何权利主张、合规风险或技术问题，请立即停止使用并联系相关方。";

    /// <summary>
    /// 初始化「关于」对话框。
    /// </summary>
    public AboutForm()
    {
        Text = "关于";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(630, 520);
        MinimumSize = new Size(630, 520);
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Icon = AppIcon.Load();

        var iconBox = new PictureBox
        {
            Image = AppIcon.Load()?.ToBitmap(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(48, 48),
            Location = new Point(20, 20)
        };

        var titleLabel = new Label
        {
            Text = ProductTitle,
            Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(84, 16),
            Size = new Size(526, 30)
        };

        var versionLabel = new Label
        {
            Text = $"版本 {ReadVersion()}",
            ForeColor = Color.FromArgb(110, 110, 110),
            AutoSize = true,
            Location = new Point(86, 48)
        };

        var separator = new Label
        {
            BorderStyle = BorderStyle.Fixed3D,
            Height = 2,
            Location = new Point(20, 84),
            Size = new Size(590, 2)
        };

        var descriptionLabel = new Label
        {
            Text = ProductDescription,
            AutoSize = false,
            Location = new Point(20, 94),
            Size = new Size(590, 180)
        };

        var disclaimerTitle = new Label
        {
            Text = "使用免责声明",
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(200, 90, 0),
            AutoSize = true,
            Location = new Point(20, 282)
        };

        var disclaimerBox = new RichTextBox
        {
            Text = DisclaimerText,
            ReadOnly = true,
            Multiline = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            BackColor = Color.FromArgb(253, 248, 235),
            ForeColor = Color.FromArgb(90, 60, 0),
            BorderStyle = BorderStyle.FixedSingle,
            WordWrap = true,
            Location = new Point(20, 308),
            Size = new Size(590, 140),
            Font = new Font("Microsoft YaHei UI", 8.5F)
        };

        var authorLabel = new Label
        {
            Text = "作者：yswenli（yswenli@outlook.com）",
            AutoSize = true,
            Location = new Point(20, 462)
        };

        var copyrightLabel = new Label
        {
            Text = "Copyright © 2026 RiverLand All Rights Reserved.",
            ForeColor = Color.FromArgb(110, 110, 110),
            AutoSize = true,
            Location = new Point(20, 484)
        };

        var okButton = new Button
        {
            Text = "确定",
            DialogResult = DialogResult.OK,
            Size = new Size(84, 28),
            Location = new Point(526, 484),
            FlatStyle = FlatStyle.System
        };

        Controls.AddRange([
            iconBox,
            titleLabel,
            versionLabel,
            separator,
            descriptionLabel,
            disclaimerTitle,
            disclaimerBox,
            authorLabel,
            copyrightLabel,
            okButton]);

        AcceptButton = okButton;
        CancelButton = okButton;
    }

    /// <summary>
    /// 读取程序集版本号。
    /// </summary>
    /// <returns>形如 <c>1.0.0</c> 的版本文本；读取失败时返回兜底版本。</returns>
    private static string ReadVersion()
    {
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? FallbackVersion : $"{version.Major}.{version.Minor}.{version.Build}";
        }
        catch (Exception exception) when (exception is System.Security.SecurityException
                                              or System.IO.IOException
                                              or System.BadImageFormatException)
        {
            // 程序集元数据读取失败不影响「关于」本身
            return FallbackVersion;
        }
    }
}