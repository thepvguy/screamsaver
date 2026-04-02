namespace Screamsaver.TrayApp;

public class HelpForm : Form
{
    public HelpForm()
    {
        Text            = "Screamsaver Help";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        StartPosition   = FormStartPosition.CenterScreen;
        ClientSize      = new Size(560, 480);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(MakePage("Overview",        HelpText.Overview));
        tabs.TabPages.Add(MakePage("Tray Icon",       HelpText.TrayIcon));
        tabs.TabPages.Add(MakePage("Settings",        HelpText.Settings));
        tabs.TabPages.Add(MakePage("PIN & Security",  HelpText.PinAndSecurity));
        tabs.TabPages.Add(MakePage("Troubleshooting", HelpText.Troubleshooting));

        var closeBtn = new Button
        {
            Text   = "Close",
            Width  = 80,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        closeBtn.Click += (_, _) => Close();

        var panel = new Panel { Dock = DockStyle.Bottom, Height = 40 };
        closeBtn.Location = new Point(ClientSize.Width - closeBtn.Width - 12, 8);
        panel.Controls.Add(closeBtn);

        Controls.Add(tabs);
        Controls.Add(panel);
    }

    private static TabPage MakePage(string title, string content)
    {
        var page = new TabPage(title);
        var rtb  = new RichTextBox
        {
            Dock        = DockStyle.Fill,
            ReadOnly    = true,
            BackColor   = SystemColors.Window,
            BorderStyle = BorderStyle.None,
            Font        = new Font("Segoe UI", 9.5f),
            ScrollBars  = RichTextBoxScrollBars.Vertical,
            DetectUrls  = false,
            WordWrap    = true,
            Margin      = new Padding(8)
        };

        rtb.SuspendLayout();
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith("# "))
            {
                if (rtb.TextLength > 0) rtb.AppendText(Environment.NewLine);
                AppendStyled(rtb, line[2..] + Environment.NewLine, bold: true,  size: 10.5f, color: Color.FromArgb(0, 80, 160));
            }
            else if (line.StartsWith("## "))
            {
                AppendStyled(rtb, Environment.NewLine + line[3..] + Environment.NewLine, bold: true,  size: 9.5f,  color: Color.FromArgb(50, 50, 50));
            }
            else if (line.StartsWith("• "))
            {
                AppendStyled(rtb, "  " + line + Environment.NewLine, bold: false, size: 9.5f,  color: SystemColors.ControlText);
            }
            else if (line == string.Empty)
            {
                rtb.AppendText(Environment.NewLine);
            }
            else
            {
                AppendStyled(rtb, line + Environment.NewLine, bold: false, size: 9.5f, color: SystemColors.ControlText);
            }
        }
        rtb.SelectionStart = 0;
        rtb.ResumeLayout();

        page.Controls.Add(rtb);
        return page;
    }

    private static void AppendStyled(RichTextBox rtb, string text, bool bold, float size, Color color)
    {
        int start = rtb.TextLength;
        rtb.AppendText(text);
        rtb.Select(start, text.Length);
        // Font wraps a GDI HFONT — dispose immediately after setting SelectionFont
        // because RichTextBox copies the font data into the RTF run (SMELL-4).
        using var font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
        rtb.SelectionFont  = font;
        rtb.SelectionColor = color;
        rtb.SelectionLength = 0;
    }
}
