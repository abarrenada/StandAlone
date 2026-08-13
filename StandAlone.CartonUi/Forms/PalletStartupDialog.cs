using StandAlone.CartonUi.Models;

namespace StandAlone.CartonUi.Forms;

/// <summary>
/// Small startup dialog shown when the app is launched with --pallet.
/// Collects shift number and inspector ID before opening PalletScanForm.
/// </summary>
public class PalletStartupDialog : Form
{
    private readonly NumericUpDown _shiftInput;
    private readonly TextBox       _inspectorInput;

    public int    Shift     { get; private set; }
    public string Inspector { get; private set; } = string.Empty;

    public PalletStartupDialog(AppSettings settings)
    {
        Text            = "Pallet Label — Start Session";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterScreen;
        MaximizeBox     = false;
        MinimizeBox     = false;
        ClientSize      = new Size(320, 180);
        BackColor       = Color.MidnightBlue;
        ForeColor       = Color.White;

        var layout = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 2,
            RowCount    = 4,
            Padding     = new Padding(20, 16, 20, 12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.Controls.Add(Label("Inspector:"), 0, 0);
        _inspectorInput = new TextBox
        {
            Dock      = DockStyle.Fill,
            MaxLength = 10,
            Font      = new Font("Segoe UI", 10f),
        };
        layout.Controls.Add(_inspectorInput, 1, 0);

        layout.Controls.Add(Label("Shift:"), 0, 1);
        _shiftInput = new NumericUpDown
        {
            Dock     = DockStyle.Fill,
            Minimum  = 1,
            Maximum  = 3,
            Value    = 1,
            Font     = new Font("Segoe UI", 10f),
        };
        layout.Controls.Add(_shiftInput, 1, 1);

        // Spacer row
        layout.Controls.Add(new Label(), 0, 2);
        layout.Controls.Add(new Label(), 1, 2);

        var btnOk = new Button
        {
            Text      = "Start",
            Size      = new Size(110, 36),
            Anchor    = AnchorStyles.Right,
            BackColor = Color.DarkSlateBlue,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font      = new Font("Segoe UI", 10f, FontStyle.Bold),
        };
        btnOk.Click += BtnOk_Click;

        var btnCancel = new Button
        {
            Text         = "Cancel",
            Size         = new Size(90, 36),
            Anchor       = AnchorStyles.Left,
            BackColor    = Color.DarkRed,
            ForeColor    = Color.White,
            FlatStyle    = FlatStyle.Flat,
            Font         = new Font("Segoe UI", 10f),
            DialogResult = DialogResult.Cancel,
        };

        var btnPanel = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
        };
        btnPanel.Controls.Add(btnOk);
        btnPanel.Controls.Add(btnCancel);

        layout.Controls.Add(btnPanel, 0, 3);
        layout.SetColumnSpan(btnPanel, 2);

        Controls.Add(layout);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        _inspectorInput.Focus();
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        var inspector = _inspectorInput.Text.Trim();
        if (inspector.Length == 0)
        {
            MessageBox.Show("Enter an inspector ID before starting.", "Required",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _inspectorInput.Focus();
            return;
        }

        Inspector    = inspector;
        Shift        = (int)_shiftInput.Value;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static Label Label(string text) => new()
    {
        Text      = text,
        Font      = new Font("Segoe UI", 10f),
        ForeColor = Color.LightGray,
        Dock      = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
    };
}
