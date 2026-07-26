namespace StandAlone.CartonUi.Forms;

/// <summary>
/// Dialog to enter a stack number for reprint.
/// Equivalent to the Progress F3 / <c>frm-stknum</c> frame interaction.
/// </summary>
public class ReprintByStackForm : Form
{
    public int StackNumber { get; private set; }

    public ReprintByStackForm(int defaultStack)
    {
        Text = "Enter Stack Number for Reprint";
        ClientSize = new Size(360, 160);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.F4) { DialogResult = DialogResult.Cancel; Close(); } };

        var lbl = new Label
        {
            Text = "Stack Number (1–9):",
            Location = new Point(16, 28),
            Size = new Size(160, 24),
            Font = new Font("Segoe UI", 10f),
        };

        var numInput = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 9,
            Value = defaultStack >= 1 && defaultStack <= 9 ? defaultStack : 1,
            Location = new Point(186, 24),
            Size = new Size(80, 30),
            Font = new Font("Segoe UI", 11f),
        };

        var btnOk = new Button
        {
            Text = "Reprint",
            Location = new Point(60, 90),
            Size = new Size(110, 38),
            BackColor = Color.DarkGreen,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.OK,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        };
        btnOk.Click += (_, _) => StackNumber = (int)numInput.Value;

        var btnCancel = new Button
        {
            Text = "Cancel  [F4]",
            Location = new Point(190, 90),
            Size = new Size(110, 38),
            BackColor = Color.DarkRed,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.Cancel,
            Font = new Font("Segoe UI", 10f),
        };

        AcceptButton = btnOk;
        CancelButton = btnCancel;
        Controls.AddRange(new Control[] { lbl, numInput, btnOk, btnCancel });
    }
}
