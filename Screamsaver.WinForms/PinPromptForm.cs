namespace Screamsaver.WinForms;

/// <summary>
/// Modal PIN-entry dialog shared by the tray app and the uninstall helper.
/// </summary>
public class PinPromptForm : Form
{
    private readonly TextBox _pinBox;
    public string EnteredPin => _pinBox.Text;

    public PinPromptForm(string prompt = "Enter PIN to continue:")
    {
        Text = "Screamsaver — Enter PIN";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(300, 110);

        var label = new Label { Text = prompt, Location = new Point(12, 12), AutoSize = true };

        _pinBox = new TextBox
        {
            PasswordChar = '*',
            Location = new Point(12, 36),
            Width = 276
        };
        _pinBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) AcceptPin(); };

        var ok = new Button
        {
            Text = "OK",
            Location = new Point(132, 72),
            Width = 75,
            DialogResult = DialogResult.OK
        };
        ok.Click += (_, _) => AcceptPin();

        var cancel = new Button
        {
            Text = "Cancel",
            Location = new Point(213, 72),
            Width = 75,
            DialogResult = DialogResult.Cancel
        };

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.AddRange(new Control[] { label, _pinBox, ok, cancel });
    }

    private void AcceptPin() { DialogResult = DialogResult.OK; Close(); }
}
