namespace StandAlone.CartonUi.Forms;

/// <summary>
/// Shows the contents of the NoGo stop-reason file.
/// Equivalent to the Progress <c>ShowStopReason</c> internal procedure.
/// If the file contains "user must reset", a Reset button is offered.
/// </summary>
public class StopReasonForm : Form
{
    public StopReasonForm(string noGoFilePath, Action sendReset)
    {
        Text = "Carton Printing Stopped Reason";
        ClientSize = new Size(720, 520);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.Black;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode is Keys.Escape or Keys.F4) Close(); };

        var lines = Array.Empty<string>();
        bool mustReset = false;

        try
        {
            lines = File.ReadAllLines(noGoFilePath);
            mustReset = lines.Any(l => l.Contains("user must reset", StringComparison.OrdinalIgnoreCase));
        }
        catch { /* file may not exist */ }

        var rtb = new RichTextBox
        {
            Location = new Point(8, 8),
            Size = new Size(700, 400),
            ReadOnly = true,
            Font = new Font("Courier New", 9.5f),
            BackColor = Color.Black,
            ForeColor = Color.Yellow,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            WordWrap = true,
            Text = string.Join(Environment.NewLine, lines.Take(20)),
        };
        Controls.Add(rtb);

        int btnY = 430;

        var btnClose = new Button
        {
            Text = "Close",
            Location = new Point(mustReset ? 200 : 300, btnY),
            Size = new Size(120, 40),
            BackColor = Color.DarkSlateGray,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.Cancel,
            Font = new Font("Segoe UI", 10f),
        };
        Controls.Add(btnClose);
        CancelButton = btnClose;

        if (mustReset)
        {
            var btnReset = new Button
            {
                Text = "Reset Printer",
                Location = new Point(360, btnY),
                Size = new Size(160, 40),
                BackColor = Color.DarkRed,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            };
            btnReset.Click += (_, _) =>
            {
                var answer = MessageBox.Show(
                    "Reset status to print cartons?",
                    "Confirm Reset",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (answer == DialogResult.Yes)
                {
                    sendReset();
                    DialogResult = DialogResult.OK;
                    Close();
                }
            };
            Controls.Add(btnReset);
        }
    }
}
