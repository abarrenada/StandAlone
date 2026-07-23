using LabelDecisionApp.CartonUi.Models;
using LabelDecisionApp.CartonUi.Services;

namespace LabelDecisionApp.CartonUi.Forms;

/// <summary>
/// Master password-protected settings dialog.
/// </summary>
public class SettingsForm : Form
{
    private AppSettings _settings = null!;

    public SettingsForm()
    {
        _settings = SettingsManager.Load();

        Text = "Settings — Master Password Required";
        ClientSize = new Size(1100, 750);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;

        BuildPasswordPanel();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  PASSWORD PANEL (initial state)
    // ─────────────────────────────────────────────────────────────────────────
    private void BuildPasswordPanel()
    {
        Controls.Clear();

        var lbl = new Label
        {
            Text = "Master Password Required",
            Location = new Point(40, 40),
            Size = new Size(400, 32),
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.White,
        };

        var pwdLabel = new Label
        {
            Text = "Password:",
            Location = new Point(40, 100),
            Size = new Size(100, 28),
            Font = new Font("Segoe UI", 10f),
            ForeColor = Color.White,
        };

        var pwdInput = new TextBox
        {
            Location = new Point(150, 96),
            Size = new Size(300, 32),
            PasswordChar = '*',
            Font = new Font("Segoe UI", 11f),
        };

        var btnOk = new Button
        {
            Text = "Unlock",
            Location = new Point(200, 160),
            Size = new Size(120, 44),
            BackColor = Color.DarkGreen,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        };
        btnOk.Click += (_, _) =>
        {
            if (_settings.VerifyPassword(pwdInput.Text))
            {
                BuildSettingsPanel();
            }
            else
            {
                MessageBox.Show("Incorrect password.", "Access Denied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                pwdInput.Clear();
                pwdInput.Focus();
            }
        };

        var btnCancel = new Button
        {
            Text = "Cancel",
            Location = new Point(340, 160),
            Size = new Size(110, 44),
            BackColor = Color.DarkRed,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.Cancel,
            Font = new Font("Segoe UI", 10f),
        };

        BackColor = Color.MidnightBlue;
        ForeColor = Color.White;

        Controls.AddRange(new Control[] { lbl, pwdLabel, pwdInput, btnOk, btnCancel });
        CancelButton = btnCancel;
        AcceptButton = btnOk;
        pwdInput.Focus();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  SETTINGS PANEL (after password verified)
    // ─────────────────────────────────────────────────────────────────────────
    private void BuildSettingsPanel()
    {
        Controls.Clear();

        var scroll = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 12,
            AutoScroll = true,
            BackColor = Color.MidnightBlue,
        };
        scroll.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        scroll.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;

        // CSV Paths section
        AddLabel(scroll, row++, 0, "━━ CSV File Paths ━━", true);
        AddRow(scroll, row++, "Boxes CSV:", new TextBox { Text = _settings.BoxesCsvPath });
        AddRow(scroll, row++, "Stackers CSV:", new TextBox { Text = _settings.StackersCsvPath });
        AddRow(scroll, row++, "Itemdet CSV:", new TextBox { Text = _settings.ItemdetCsvPath });
        AddRow(scroll, row++, "MItemdet CSV:", new TextBox { Text = _settings.MitemdetCsvPath });

        // Production settings
        AddLabel(scroll, row++, 0, "━━ Production ━━", true);
        var lineInput = new NumericUpDown { Minimum = 1, Maximum = 99 };
        lineInput.Value = _settings.ProductionLineNumber;
        AddRow(scroll, row++, "Line Number:", lineInput);

        // PLC settings
        AddLabel(scroll, row++, 0, "━━ PLC Connection ━━", true);
        var connTypeCombo = new ComboBox { Text = _settings.PlcConnectionType, DropDownStyle = ComboBoxStyle.DropDownList };
        connTypeCombo.Items.AddRange(new[] { "SerialPort", "IP" });
        AddRow(scroll, row++, "Connection Type:", connTypeCombo);
        AddRow(scroll, row++, "Address (COM/IP):", new TextBox { Text = _settings.PlcAddress });
        var baudInput = new NumericUpDown { Minimum = 300, Maximum = 115200 };
        baudInput.Value = _settings.PlcBaudRate;
        AddRow(scroll, row++, "Baud Rate:", baudInput);

        // Label output
        AddLabel(scroll, row++, 0, "━━ Label Output ━━", true);
        var outputTypeCombo = new ComboBox { Text = _settings.LabelOutputType, DropDownStyle = ComboBoxStyle.DropDownList };
        outputTypeCombo.Items.AddRange(new[] { "NiceLabel Xml", "SerialPort", "HttpPost" });
        AddRow(scroll, row++, "Output Type:", outputTypeCombo);
        AddRow(scroll, row++, "Output Address:", new TextBox { Text = _settings.LabelOutputAddress });

        Controls.Add(scroll);

        // Save/Cancel buttons
        var panel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            BackColor = Color.DarkSlateGray,
        };

        var btnSave = new Button
        {
            Text = "Save",
            Location = new Point(200, 8),
            Size = new Size(120, 40),
            BackColor = Color.DarkGreen,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        };
        btnSave.Click += (_, _) =>
        {
            // Collect values from controls and save
            CollectSettingsFromControls(scroll);
            SettingsManager.Save(_settings);
            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCancel = new Button
        {
            Text = "Cancel",
            Location = new Point(340, 8),
            Size = new Size(120, 40),
            BackColor = Color.DarkRed,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.Cancel,
            Font = new Font("Segoe UI", 10f),
        };

        panel.Controls.AddRange(new Control[] { btnSave, btnCancel });
        Controls.Add(panel);

        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }

    private void AddLabel(TableLayoutPanel panel, int row, int col, string text, bool isHeader)
    {
        var lbl = new Label
        {
            Text = text,
            Font = isHeader ? new Font("Segoe UI", 9.5f, FontStyle.Bold) : new Font("Segoe UI", 9.5f),
            ForeColor = Color.LightYellow,
            AutoSize = true,
        };
        panel.Controls.Add(lbl, col, row);
        if (col == 0) panel.SetColumnSpan(lbl, 2);
    }

    private void AddRow(TableLayoutPanel panel, int row, string labelText, Control input)
    {
        var lbl = new Label
        {
            Text = labelText,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.White,
            AutoSize = true,
        };
        panel.Controls.Add(lbl, 0, row);
        input.Width = 750;
        input.Font = new Font("Segoe UI", 9f);
        panel.Controls.Add(input, 1, row);
    }

    private void CollectSettingsFromControls(TableLayoutPanel panel)
    {
        // Simple approach: look for controls by type and update _settings
        var textBoxes = panel.Controls.OfType<TextBox>().ToList();
        var numericUpDowns = panel.Controls.OfType<NumericUpDown>().ToList();
        var combos = panel.Controls.OfType<ComboBox>().ToList();

        if (textBoxes.Count >= 4)
        {
            _settings.BoxesCsvPath = textBoxes[0].Text;
            _settings.StackersCsvPath = textBoxes[1].Text;
            _settings.ItemdetCsvPath = textBoxes[2].Text;
            _settings.MitemdetCsvPath = textBoxes[3].Text;
        }
        if (textBoxes.Count >= 6)
        {
            _settings.PlcAddress = textBoxes[4].Text;
            _settings.LabelOutputAddress = textBoxes[5].Text;
        }

        if (numericUpDowns.Count >= 2)
        {
            _settings.ProductionLineNumber = (int)numericUpDowns[0].Value;
            _settings.PlcBaudRate = (int)numericUpDowns[1].Value;
        }

        if (combos.Count >= 2)
        {
            _settings.PlcConnectionType = combos[0].Text;
            _settings.LabelOutputType = combos[1].Text;
        }
    }
}
