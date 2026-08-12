using StandAlone.CartonUi.Models;
using StandAlone.CartonUi.Services;

namespace StandAlone.CartonUi.Forms;

/// <summary>
/// Master password-protected settings dialog.
/// </summary>
public class SettingsForm : Form
{
    private static readonly string[] LabelOutputTypes =
    {
        "NiceLabel Xml",
        "SerialPort",
        "NetworkPrinter",
        "HttpPost",
    };

    private static readonly string[] CartonPrintModes =
    {
        "PLC Signal",
        "Manual Qty",
    };

    private static readonly string[] ThermalPrinterTypes =
    {
        "SATO",
        "IPL",
        "ZPL",
        "Fingerprint",
    };

    private AppSettings _settings = null!;

    public SettingsForm()
    {
        _settings = SettingsManager.Load();

        Text = "Settings — Master Password Required";
        ClientSize = new Size(1100, 600);
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
        btnCancel.Click += (_, _) => Close();

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
        SuspendLayout();

        var scroll = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 15,
            AutoScroll = true,
            BackColor = Color.MidnightBlue,
        };
        scroll.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        scroll.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;

        // CSV Paths section
        AddLabel(scroll, row++, 0, "━━ CSV File Paths ━━", true);
        AddRow(scroll, row++, "Boxes CSV:", new TextBox { Name = "BoxesCsvPath", Text = _settings.BoxesCsvPath });
        AddRow(scroll, row++, "Stackers CSV:", new TextBox { Name = "StackersCsvPath", Text = _settings.StackersCsvPath });
        AddRow(scroll, row++, "Itemdet CSV:", new TextBox { Name = "ItemdetCsvPath", Text = _settings.ItemdetCsvPath });
        AddRow(scroll, row++, "MItemdet CSV:", new TextBox { Name = "MitemdetCsvPath", Text = _settings.MitemdetCsvPath });

        // Production settings
        AddLabel(scroll, row++, 0, "━━ Production ━━", true);
        var lineInput = new NumericUpDown { Name = "ProductionLineNumber", Minimum = 1, Maximum = 99 };
        lineInput.Value = _settings.ProductionLineNumber;
        AddRow(scroll, row++, "Line Number:", lineInput);

        // PLC settings
        AddLabel(scroll, row++, 0, "━━ PLC Connection ━━", true);
        var connTypeCombo = new ComboBox
        {
            Name = "PlcConnectionType",
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        connTypeCombo.Items.AddRange(new[] { "SerialPort", "IP" });
        connTypeCombo.SelectedItem = string.Equals(_settings.PlcConnectionType, "IP", StringComparison.OrdinalIgnoreCase)
            ? "IP"
            : "SerialPort";
        AddRow(scroll, row++, "Connection Type:", connTypeCombo);
        AddRow(scroll, row++, "Address (COM/IP):", new TextBox { Name = "PlcAddress", Text = _settings.PlcAddress });
        var baudInput = new NumericUpDown { Name = "PlcBaudRate", Minimum = 300, Maximum = 115200 };
        baudInput.Value = _settings.PlcBaudRate;
        AddRow(scroll, row++, "Baud Rate:", baudInput);

        // Label output
        AddLabel(scroll, row++, 0, "━━ Label Output ━━", true);
        var outputTypeCombo = new ComboBox { Name = "LabelOutputType", DropDownStyle = ComboBoxStyle.DropDownList };
        outputTypeCombo.Items.AddRange(LabelOutputTypes);
        outputTypeCombo.SelectedItem = LabelOutputTypes.Contains(_settings.LabelOutputType)
            ? _settings.LabelOutputType
            : "NiceLabel Xml";
        AddRow(scroll, row++, "Output Type:", outputTypeCombo);
        AddRow(scroll, row++, "Output Address:", new TextBox { Name = "LabelOutputAddress", Text = _settings.LabelOutputAddress });

        var thermalTypeCombo = new ComboBox { Name = "ThermalPrinterType", DropDownStyle = ComboBoxStyle.DropDownList };
        thermalTypeCombo.Items.AddRange(ThermalPrinterTypes);
        thermalTypeCombo.SelectedItem = ThermalPrinterTypes.Contains(_settings.ThermalPrinterType)
            ? _settings.ThermalPrinterType
            : "SATO";
        AddRow(scroll, row++, "Thermal Type:", thermalTypeCombo);

        // Carton process mode
        AddLabel(scroll, row++, 0, "━━ Carton Process ━━", true);
        var cartonModeCombo = new ComboBox { Name = "CartonPrintMode", DropDownStyle = ComboBoxStyle.DropDownList };
        cartonModeCombo.Items.AddRange(CartonPrintModes);
        cartonModeCombo.SelectedItem = CartonPrintModes.Contains(_settings.CartonPrintMode)
            ? _settings.CartonPrintMode
            : "PLC Signal";
        AddRow(scroll, row++, "Carton Mode:", cartonModeCombo);

        // Pallet label settings
        AddLabel(scroll, row++, 0, "━━ Pallet Label ━━", true);
        AddRow(scroll, row++, "Plant Name:", new TextBox { Name = "PlantName", Text = _settings.PlantName });
        AddRow(scroll, row++, "Pallet Location:", new TextBox { Name = "PalletLocation", Text = _settings.PalletLocation });

        // Save/Cancel buttons (always visible footer)
        var panel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 64,
            BackColor = Color.DarkSlateGray,
        };

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(16, 10, 16, 10),
            BackColor = Color.DarkSlateGray,
        };

        var btnSave = new Button
        {
            Text = "Save",
            Size = new Size(120, 40),
            BackColor = Color.DarkGreen,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(12, 0, 0, 0),
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
            Size = new Size(120, 40),
            BackColor = Color.DarkRed,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.Cancel,
            Margin = new Padding(12, 0, 0, 0),
            Font = new Font("Segoe UI", 10f),
        };
        btnCancel.Click += (_, _) => Close();

        buttonRow.Controls.Add(btnCancel);
        buttonRow.Controls.Add(btnSave);
        panel.Controls.Add(buttonRow);

        Controls.Add(panel);
        Controls.Add(scroll);
        panel.BringToFront();

        AcceptButton = btnSave;
        CancelButton = btnCancel;
        ResumeLayout(true);
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
        var allControls = panel.Controls.Cast<Control>().ToList();

        string GetText(string name) => allControls.FirstOrDefault(c => c.Name == name)?.Text ?? string.Empty;
        string GetComboValue(string name, string fallback)
        {
            if (allControls.FirstOrDefault(c => c.Name == name) is ComboBox combo)
            {
                if (combo.SelectedItem is string selected && !string.IsNullOrWhiteSpace(selected))
                    return selected;
                if (!string.IsNullOrWhiteSpace(combo.Text))
                    return combo.Text;
            }
            return fallback;
        }
        int GetInt(string name, int fallback)
        {
            if (allControls.FirstOrDefault(c => c.Name == name) is NumericUpDown n)
                return (int)n.Value;
            return fallback;
        }

        _settings.BoxesCsvPath = GetText("BoxesCsvPath");
        _settings.StackersCsvPath = GetText("StackersCsvPath");
        _settings.ItemdetCsvPath = GetText("ItemdetCsvPath");
        _settings.MitemdetCsvPath = GetText("MitemdetCsvPath");
        _settings.PlcAddress = GetText("PlcAddress");
        _settings.LabelOutputAddress = GetText("LabelOutputAddress");

        _settings.ProductionLineNumber = GetInt("ProductionLineNumber", _settings.ProductionLineNumber);
        _settings.PlcBaudRate = GetInt("PlcBaudRate", _settings.PlcBaudRate);

        _settings.PlcConnectionType = GetComboValue("PlcConnectionType", "SerialPort");
        _settings.LabelOutputType = GetComboValue("LabelOutputType", "NiceLabel Xml");
        _settings.ThermalPrinterType = GetComboValue("ThermalPrinterType", "SATO");
        _settings.CartonPrintMode = GetComboValue("CartonPrintMode", "PLC Signal");

        _settings.PlantName = GetText("PlantName");
        _settings.PalletLocation = GetText("PalletLocation");
    }
}
