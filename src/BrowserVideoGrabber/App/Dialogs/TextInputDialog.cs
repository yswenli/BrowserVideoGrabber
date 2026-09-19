/****************************************************************************
*Copyright (c) 2026 yswenli All Rights Reserved.
*CLR版本： .net10.0
*机器名称：WALLE
*公司名称：Walle
*命名空间：BrowserVideoGrabber.App.Dialogs
*文件名： TextInputDialog
*版本号： V1.0.0.0
*唯一标识：47ec922d-f275-4b23-becd-bf87e3ae1d47
*当前的用户域：WALLE
*创建人： yswenli
*电子邮箱：yswenli@outlook.com
*创建时间：2026/9/13 02:22:00
*描述：单行文本输入对话框，供收藏改名等场景复用。
*
*=================================================
*修改标记
*修改时间：2026/9/13 02:22:00
*修改人： yswenli
*版本号： V1.0.0.0
*描述：
*
*****************************************************************************/

using BrowserVideoGrabber.App;

namespace BrowserVideoGrabber.App.Dialogs;

/// <summary>
/// 单行文本输入对话框。
/// </summary>
/// <remarks>
/// 抽出为独立对话框而不是每次内联拼控件：收藏改名、后续可能的重命名场景都需要它，
/// 内联复制会导致每处的校验与按钮布局各写一遍而逐渐走样。
/// </remarks>
public sealed class TextInputDialog : Form
{
    private readonly TextBox _textBox = new() { Dock = DockStyle.Fill };

    /// <summary>
    /// 初始化输入对话框。
    /// </summary>
    /// <param name="title">窗体标题。</param>
    /// <param name="prompt">提示文本。</param>
    /// <param name="initialValue">初始值。</param>
    public TextInputDialog(string title, string prompt, string? initialValue = null)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 140);
        MinimumSize = new Size(360, 140);
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Icon = AppIcon.Load();

        Value = initialValue ?? string.Empty;
        _textBox.Text = Value;

        var promptLabel = new Label
        {
            Text = prompt,
            Dock = DockStyle.Top,
            Height = 34,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0)
        };

        var inputPanel = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(4, 0, 4, 0) };
        inputPanel.Controls.Add(_textBox);

        var okButton = new Button
        {
            Text = "确定",
            DialogResult = DialogResult.OK,
            Width = 84,
            Height = 28,
            FlatStyle = FlatStyle.System
        };

        var cancelButton = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Width = 84,
            Height = 28,
            FlatStyle = FlatStyle.System
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 6, 8, 0)
        };
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(okButton);

        Controls.Add(inputPanel);
        Controls.Add(buttonPanel);
        Controls.Add(promptLabel);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        okButton.Click += (_, _) =>
        {
            Value = _textBox.Text.Trim();

            // 空值不允许提交：改名成空会让列表出现一行没有文字的条目
            if (string.IsNullOrWhiteSpace(Value))
            {
                DialogResult = DialogResult.None;
            }
        };
    }

    /// <summary>用户输入的值（已去除首尾空白）。</summary>
    public string Value { get; private set; }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _textBox.Dispose();
        }

        base.Dispose(disposing);
    }
}
