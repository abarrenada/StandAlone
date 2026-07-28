using StandAlone.CartonUi.Forms;
using StandAlone.CartonUi.Models;
using StandAlone.CartonUi.Services;
using System.ComponentModel;

namespace StandAlone.CartonUi;

/// <summary>
/// Main form for the Carton Label Printing module.
/// Implements all functionality from dtlbl067.p:
///   • Startup panel  — shift / inspector / label size / optional primary items
///   • Browse panel   — last 12 boxes, auto-refresh, stacker lookup
///   • F1 Reprint selected box
///   • F3 Reprint by stack number
///   • F4 New setup
///   • F6 View stop reason  (enabled only when printer is stopped)
///   • F7 View event log
///   • F8 View statistics report
/// All F-key actions are also available as labelled buttons.
/// </summary>
public class MainForm : Form
{
    // ── Configuration + services ─────────────────────────────────────────────
    private readonly CartonAppConfig _config;
    private readonly IBoxRepository _boxRepo;
    private readonly IPlcPipeService _plcPipe;
    private AppSettings _settings = null!;

    // ── Session state (set in startup panel) ─────────────────────────────────
    private int _shift;
    private string _inspector = string.Empty;
    private string _labelSize = string.Empty;

    // ── Browse state ──────────────────────────────────────────────────────────
    private readonly BindingList<BoxBrowseRow> _browseRows = new();
    private bool _isStopped;
    private DateTime _lastBoxTime = DateTime.MinValue;
    private System.Windows.Forms.Timer _refreshTimer = null!;

    // ── Startup panel controls ────────────────────────────────────────────────
    private Panel _startupPanel = null!;
    private NumericUpDown _shiftInput = null!;
    private TextBox _inspectorInput = null!;
    private ComboBox _labelSizeCombo = null!;
    private TextBox? _primaryItemInput;
    private Label? _primaryItemDescLabel;  // ← Description display below Primary Item
    private TextBox? _secondaryItemInput;
    private NumericUpDown? _manualQtyInput;
    private Button _btnBegin = null!;

    // ── Browse panel controls ─────────────────────────────────────────────────
    private Panel _browsePanel = null!;
    private DataGridView _browseGrid = null!;
    private Label _browseTitleLabel = null!;
    private Label _stoppedLabel = null!;
    private Label _statusLabel = null!;
    private Button _btnF1Reprint = null!;
    private Button _btnF3ReprintByStack = null!;
    private Button _btnF4NewSetup = null!;
    private Button _btnF6StopReason = null!;
    private Button _btnF7EventLog = null!;
    private Button _btnF8Stats = null!;

    // ═══════════════════════════════════════════════════════════════════════════
    //  CONSTRUCTOR
    // ═══════════════════════════════════════════════════════════════════════════
    public MainForm(string? baseDirectory = null)
    {
        var baseDir = baseDirectory ?? AppContext.BaseDirectory;
        _config  = CartonConfigLoader.Load(baseDir);
        _boxRepo = new FileBoxRepository(_config.DataDirectory);
        _plcPipe = new FilePlcPipeService();
        _settings = SettingsManager.Load();

        AutoScaleMode = AutoScaleMode.None;
        Text = $"Carton Label Printing — Line {_config.LineNumber:00}";
        ClientSize = new Size(1120, 690);
        BackColor = Color.MidnightBlue;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10f);
        KeyPreview = true;
        KeyDown += MainForm_KeyDown;

        BuildStartupPanel();
        BuildBrowsePanel();
        ShowStartup();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  STARTUP PANEL — equivalent to Progress startup-frm frame
    // ═══════════════════════════════════════════════════════════════════════════
    private void BuildStartupPanel()
    {
        _startupPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.MidnightBlue };

        const int labelX = 40;
        const int labelW = 200;
        const int inputX = 250;
        const int inputW = 240;

        // ── Title ──────────────────────────────────────────────────────────────
        _startupPanel.Controls.Add(new Label
        {
            Text = $" Enter Startup Information for Line {_config.LineNumber:00} ",
            Location = new Point(labelX, 40),
            AutoSize = true,
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = Color.LightYellow,
            BackColor = Color.DarkSlateBlue,
            Padding = new Padding(8, 4, 8, 4),
        });

        // ── Settings button (top-right) ───────────────────────────────────────
        var btnSettings = new Button
        {
            Text = "⚙ Settings",
            Location = new Point(990, 8), Size = new Size(110, 36),
            BackColor = Color.DarkSlateGray, ForeColor = Color.LightGray,
            FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9f),
        };
        btnSettings.Click += (_, _) =>
        {
            using var dlg = new SettingsForm();
            dlg.ShowDialog(this);

            _settings = SettingsManager.Load();
            Controls.Remove(_startupPanel);
            _startupPanel.Dispose();
            BuildStartupPanel();
            ShowStartup();
        };
        _startupPanel.Controls.Add(btnSettings);

        // ── Shift  y=120 ──────────────────────────────────────────────────────
        _startupPanel.Controls.Add(MakeLabel("Shift:", new Point(labelX, 124), labelW));
        _shiftInput = new NumericUpDown
        {
            Location = new Point(inputX, 120), Size = new Size(80, 30),
            Minimum = 1, Maximum = 9, Value = 1,
            Font = new Font("Segoe UI", 11f),
        };
        _startupPanel.Controls.Add(_shiftInput);

        // ── Inspector  y=180 ──────────────────────────────────────────────────
        _startupPanel.Controls.Add(MakeLabel("Inspector:", new Point(labelX, 184), labelW));
        _inspectorInput = new TextBox
        {
            Location = new Point(inputX, 180), Size = new Size(inputW, 30),
            MaxLength = 20, Font = new Font("Segoe UI", 11f),
        };
        _startupPanel.Controls.Add(_inspectorInput);

        // ── Label Size  y=240 ─────────────────────────────────────────────────
        _startupPanel.Controls.Add(MakeLabel("Label Size:", new Point(labelX, 244), labelW));
        _labelSizeCombo = new ComboBox
        {
            Location = new Point(inputX, 240), Size = new Size(inputW, 30),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 11f),
        };
        PopulateLabelSizes();
        _startupPanel.Controls.Add(_labelSizeCombo);

        // Optional size reference list (below combo, 276-336)
        var sizeListText = BuildLabelSizeListText();
        if (!string.IsNullOrEmpty(sizeListText))
        {
            _startupPanel.Controls.Add(new Label
            {
                Location = new Point(inputX, 276), Size = new Size(600, 60),
                ForeColor = Color.LightCyan, Font = new Font("Courier New", 8f),
                Text = sizeListText,
            });
        }

        // ── Primary Item  y=350 ───────────────────────────────────────────────
        int nextY = 350;
        if (_config.DoesManStk && !_config.DoesMexico)
        {
            _startupPanel.Controls.Add(MakeLabel("Primary Item:", new Point(labelX, nextY + 4), labelW));
            _primaryItemInput = new TextBox
            {
                Location = new Point(inputX, nextY), Size = new Size(inputW, 30),
                MaxLength = 15, Font = new Font("Segoe UI", 11f),
                Text = _settings.CurrentPrimaryItem,
            };
            _primaryItemInput.Leave += (_, _) => _ = RefreshPrimaryItemDescriptionAsync();
            _primaryItemInput.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Return)
                {
                    e.SuppressKeyPress = true;
                    _ = RefreshPrimaryItemDescriptionAsync();
                    _labelSizeCombo.Focus();
                }
            };
            _startupPanel.Controls.Add(_primaryItemInput);
            nextY += 40;

            _primaryItemDescLabel = new Label
            {
                Location = new Point(inputX, nextY), Size = new Size(inputW + 200, 24),
                ForeColor = Color.LightCyan, Font = new Font("Segoe UI", 9f),
                AutoSize = false, TextAlign = ContentAlignment.TopLeft,
                Text = "(Enter item number to see description)",
            };
            _startupPanel.Controls.Add(_primaryItemDescLabel);
            nextY += 40;
        }

        // ── 2nd Primary Item ──────────────────────────────────────────────────
        if (_config.DoesTwoPrims && _config.DoesManStk && !_config.DoesMexico)
        {
            _startupPanel.Controls.Add(MakeLabel("2nd Primary Item:", new Point(labelX, nextY + 4), labelW));
            _secondaryItemInput = new TextBox
            {
                Location = new Point(inputX, nextY), Size = new Size(inputW, 30),
                MaxLength = 15, Font = new Font("Segoe UI", 11f),
                Text = _settings.CurrentSecondaryItem,
            };
            _startupPanel.Controls.Add(_secondaryItemInput);
            nextY += 50;
        }

        if (string.Equals(_settings.CartonPrintMode, "Manual Qty", StringComparison.OrdinalIgnoreCase))
        {
            _startupPanel.Controls.Add(MakeLabel("Carton Qty:", new Point(labelX, nextY + 4), labelW));
            _manualQtyInput = new NumericUpDown
            {
                Location = new Point(inputX, nextY), Size = new Size(120, 30),
                Minimum = 1, Maximum = 999, Value = 1,
                Font = new Font("Segoe UI", 11f),
            };
            _startupPanel.Controls.Add(_manualQtyInput);
            nextY += 50;
        }

        // ── Begin / Exit buttons ──────────────────────────────────────────────
        int btnY = nextY + 30;
        _btnBegin = new Button
        {
            Text = "Begin  [Enter]",
            Size = new Size(160, 44), Location = new Point(inputX, btnY),
            BackColor = Color.DarkGreen, ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 11f, FontStyle.Bold),
        };
        _btnBegin.Click += BtnBegin_Click;
        _startupPanel.Controls.Add(_btnBegin);

        var btnExit = new Button
        {
            Text = "Exit  [F4]",
            Size = new Size(130, 44), Location = new Point(inputX + 176, btnY),
            BackColor = Color.DarkRed, ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 11f),
        };
        btnExit.Click += (_, _) => Application.Exit();
        _startupPanel.Controls.Add(btnExit);

        AcceptButton = _btnBegin;
        Controls.Add(_startupPanel);
    }

    private void PopulateLabelSizes()
    {
        if (_config.LabelSizes.Count > 0)
        {
            foreach (var sz in _config.LabelSizes)
            {
                bool include = _config.DoesMexico ? sz.IncludeMexico : sz.IncludeUs;
                if (include) _labelSizeCombo.Items.Add(sz);
            }
        }
        else
        {
            // Default fallback when LabelSzPrmpt is absent
            _labelSizeCombo.Items.Add(new LabelSizeOption { SizeCode = "4.5x3", Prompt = "4.5x3 Standard",  IncludeUs = true });
            _labelSizeCombo.Items.Add(new LabelSizeOption { SizeCode = "2x7",   Prompt = "2x7 Narrow",      IncludeUs = true });
        }
        if (_labelSizeCombo.Items.Count > 0)
            _labelSizeCombo.SelectedIndex = 0;
    }

    private string BuildLabelSizeListText()
    {
        // Progress: two columns, 5 items each (aSzPrmpt[1-5] left, aSzPrmpt[6-10] right)
        var sizes = _config.LabelSizes;
        if (sizes.Count == 0) return string.Empty;
        var col1 = sizes.Take(5).ToList();
        var col2 = sizes.Skip(5).Take(5).ToList();
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Math.Max(col1.Count, col2.Count); i++)
        {
            var left  = i < col1.Count ? $"{col1[i].SizeCode,-9}{col1[i].Prompt,-20}" : new string(' ', 29);
            var right = i < col2.Count ? $"{col2[i].SizeCode,-9}{col2[i].Prompt}" : string.Empty;
            sb.AppendLine(left + right);
        }
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Startup submit handler
    // ─────────────────────────────────────────────────────────────────────────
    private void BtnBegin_Click(object? sender, EventArgs e)
    {
        _shift = (int)_shiftInput.Value;

        _inspector = _inspectorInput.Text.Trim();
        if (_inspector.Length == 0)
        {
            MessageBox.Show("Inspector must be entered.", "Input Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _inspectorInput.Focus();
            return;
        }
        // Progress: "do while length(prt-inspector) lt 2" pad to at least 2 chars
        while (_inspector.Length < 2) _inspector += " ";

        if (_labelSizeCombo.SelectedItem is not LabelSizeOption selectedSize)
        {
            MessageBox.Show("Please select a valid label size.", "Input Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _labelSizeCombo.Focus();
            return;
        }
        _labelSize = selectedSize.SizeCode;
        // Validate against comma-delimited list (same as Progress index check)
        if (_config.ValidLabelSizes.Length > 2 &&
            !_config.ValidLabelSizes.Contains($",{_labelSize},", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Valid Label Size Only.", "Input Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _labelSizeCombo.Focus();
            return;
        }

        // Save primary and secondary items for next session
        if (_primaryItemInput is not null)
            _settings.CurrentPrimaryItem = _primaryItemInput.Text.Trim();
        if (_secondaryItemInput is not null)
            _settings.CurrentSecondaryItem = _secondaryItemInput.Text.Trim();
        SettingsManager.Save(_settings);

        if (string.Equals(_settings.CartonPrintMode, "Manual Qty", StringComparison.OrdinalIgnoreCase))
        {
            _ = SendManualCartonPrintAsync();
            return;
        }

        // Progress: "50, ," + shift + "," + inspector + "," + labelSize → PLC pipe
        try { _plcPipe.SendStartup(_shift, _inspector.Trim(), _labelSize, _config.PlcPipePath); }
        catch (Exception ex) { SetStatus($"Warning: PLC pipe write failed: {ex.Message}"); }

        ShowBrowse();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Simulate carton read from PLC (test/debug feature)
    // ─────────────────────────────────────────────────────────────────────────
    private void BtnSimulateCartonRead_Click(object? sender, EventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // Get primary item number (from startup form or default to hardcoded sample)
                string primaryItem = _settings.CurrentPrimaryItem?.Trim() ?? "FL9036MOD1P4";
                if (string.IsNullOrEmpty(primaryItem))
                    primaryItem = "FL9036MOD1P4";

                // Look up the item in itemdet.csv to get IRef, description, shade, size
                var itemDetail = await _boxRepo.GetItemDetailByNumberAsync(primaryItem, _config.DoesMexico, CancellationToken.None);
                if (itemDetail == null)
                {
                    MessageBox.Show($"Cannot find item '{primaryItem}' in item master.\r\nPlease enter a valid Primary Item on startup.", 
                        "Item Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Generate test stack number (cycle through 1-3 for visibility)
                var existingStacks = new List<int>();
                var boxFile = Path.Combine(_config.DataDirectory, $"boxes{_config.LineNumber:00}.csv");
                if (File.Exists(boxFile))
                {
                    foreach (var line in File.ReadAllLines(boxFile))
                    {
                        if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                        {
                            var parts = line.Split(',');
                            if (parts.Length >= 4 && int.TryParse(parts[3].Trim(), out var sn))
                                existingStacks.Add(sn);
                        }
                    }
                }
                int testStack = (existingStacks.Count % 3) + 1;
                string stackNum = $" {testStack}";

                // Build PLC message: stack at position 0-1, item description at position 32-61 (30 chars)
                // Format: "XX[31 spaces]ITEM_DESC[padding]"
                string plcMsg = stackNum + new string(' ', 31) + itemDetail.ItemNumber.PadRight(30);
                if (plcMsg.Length < 65) plcMsg = plcMsg.PadRight(65);

                // Create box record (CSV: RecId, LineId, MakeTime, StackNum, PlcMsg, ErrMsg, PrintNum)
                // Don't quote fields - just use raw values
                int nextRecId = existingStacks.Count + 1;
                var now = DateTime.Now;
                string boxRecord = $"{nextRecId},{_config.LineNumber},{now:yyyy-MM-dd HH:mm:ss},{stackNum},{plcMsg},,1";

                // Create stacker record with item details (CSV: LineId, StackNum, IRef, PlcMsg, Shade, Size, ErrMsg)
                int shade = itemDetail.Shade;
                string size = "L"; // Default size
                string stackerRecord = $"{_config.LineNumber},{stackNum},{itemDetail.IRef},{plcMsg},{shade},{size},";

                // Append to CSV files
                AppendToFile(boxFile, boxRecord);
                var stackerFile = Path.Combine(_config.DataDirectory, $"stackers{_config.LineNumber:00}.csv");
                AppendToFile(stackerFile, stackerRecord);

                // Show confirmation
                MessageBox.Show(
                    $"✓ Simulated carton read: Stack {testStack}\r\n" +
                    $"  Item: {primaryItem}\r\n" +
                    $"  IRef: {itemDetail.IRef}\r\n" +
                    $"  Shade: {shade}, Size: {size}\r\n" +
                    $"\r\nWill appear in browse panel in 1 second.\r\n" +
                    $"Select it and press F1 to print.",
                    "Carton Simulated", MessageBoxButtons.OK, MessageBoxIcon.Information);

                // Wait for refresh to pick up the data, then auto-trigger print
                await Task.Delay(1500);
                this.Invoke(() =>
                {
                    // Find and select the newest row
                    if (_browseRows.Count > 0)
                    {
                        _browseGrid.ClearSelection();
                        _browseGrid.Rows[0].Selected = true;
                        // Trigger F1 reprint
                        BtnF1Reprint_Click(null, EventArgs.Empty);
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error simulating carton read:\r\n{ex.Message}", "Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        });
    }

    private async Task SendManualCartonPrintAsync()
    {
        if (_primaryItemInput is null || _manualQtyInput is null)
        {
            SetStatus("Manual print controls are unavailable.");
            return;
        }

        var itemNumber = _primaryItemInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(itemNumber))
        {
            MessageBox.Show("Enter a primary item before manual printing.", "Input Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _primaryItemInput.Focus();
            return;
        }

        var quantity = (int)_manualQtyInput.Value;
        if (quantity < 1 || quantity > 999)
        {
            MessageBox.Show("Carton quantity must be between 1 and 999.", "Input Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _manualQtyInput.Focus();
            return;
        }

        try
        {
            var itemDetail = await _boxRepo.GetItemDetailByNumberAsync(itemNumber, _config.DoesMexico, CancellationToken.None);
            if (itemDetail is null)
            {
                MessageBox.Show($"Cannot find item '{itemNumber}' in item master.", "Item Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_settings.LabelOutputAddress))
            {
                SetStatus("Warning: Label output address is empty.");
                return;
            }

            var job = new ManualCartonPrintJob(
                itemDetail.ItemNumber,
                quantity,
                _inspector,
                _shift,
                _labelSize,
                itemDetail.GetPrimaryItemDescription(),
                Environment.UserName,
                DateTime.Now);

            var exporter = new NiceLabelXmlExporter(_settings.LabelOutputAddress);
            var success = await exporter.ExportManualCartonAsync(job, CancellationToken.None);
            if (success)
            {
                SetStatus($"Manual carton print sent for {itemDetail.ItemNumber} x{quantity}.");
                ShowBrowse();
            }
            else
            {
                SetStatus("Warning: Failed to send manual carton print job.");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Manual print error: {ex.Message}");
        }
    }

    private void AppendToFile(string filePath, string record)
    {
        // Ensure directory exists
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // Append record with newline
        if (File.Exists(filePath))
            File.AppendAllText(filePath, Environment.NewLine + record);
        else
            File.WriteAllText(filePath, record);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  BROWSE PANEL — equivalent to Progress outerloop repeat / frame boxq
    // ═══════════════════════════════════════════════════════════════════════════
    private void BuildBrowsePanel()
    {
        _browsePanel = new Panel { Dock = DockStyle.None, BackColor = Color.MidnightBlue, Visible = false };

        // Title (shows shift + line number)
        _browseTitleLabel = new Label
        {
            Location = new Point(8, 8), Size = new Size(1100, 28),
            ForeColor = Color.LightYellow, TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.DarkSlateBlue, Font = new Font("Segoe UI", 11f, FontStyle.Bold),
        };
        _browsePanel.Controls.Add(_browseTitleLabel);

        // Browse grid (12 rows — same as Progress "12 down" in frame boxq)
        _browseGrid = new DataGridView
        {
            Location = new Point(8, 42), Size = new Size(1100, 342),
            AllowUserToAddRows = false, ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoGenerateColumns = false,
            BackgroundColor = Color.LightSteelBlue,
            BorderStyle = BorderStyle.FixedSingle,
            ForeColor = Color.Black,
            MultiSelect = false,
            Font = new Font("Courier New", 9f),
            RowHeadersVisible = false,
            ColumnHeadersHeight = 26,
        };
        _browseGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "RecId",          DataPropertyName = "RecId",         Width = 68  });
        _browseGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Time",           DataPropertyName = "TimeDisplay",   Width = 78  });
        _browseGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Sorter Message", DataPropertyName = "SorterMessage", Width = 420 });
        _browseGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Part/*Error",    DataPropertyName = "PartOrError",   Width = 534 });
        _browseGrid.DataSource = _browseRows;
        _browseGrid.SelectionChanged += (_, _) => UpdateButtonStates();
        _browsePanel.Controls.Add(_browseGrid);

        // Stopped indicator
        _stoppedLabel = new Label
        {
            Location = new Point(8, 392), Size = new Size(200, 26),
            ForeColor = Color.Red, Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        };
        _browsePanel.Controls.Add(_stoppedLabel);

        // Instruction message (Progress: "Clear printer of labels...")
        var msgLabel = new Label
        {
            Text = "Clear printer of labels and then choose box(es) to reprint",
            Location = new Point(215, 392), Size = new Size(750, 26),
            ForeColor = Color.LightYellow, Font = new Font("Segoe UI", 10f),
        };
        _browsePanel.Controls.Add(msgLabel);

        // ── Function-key buttons ─────────────────────────────────────────────
        int btnY = 430, btnX = 8;
        const int gap = 6;

        _btnF1Reprint = MakeFuncButton("[F1] Reprint Selected",  Color.DarkGreen,     new Point(btnX, btnY), 188); btnX += 188 + gap;
        _btnF3ReprintByStack = MakeFuncButton("[F3] Reprint by Stack #", Color.DarkSlateBlue, new Point(btnX, btnY), 200); btnX += 200 + gap;
        _btnF4NewSetup = MakeFuncButton("[F4] New Setup",         Color.SaddleBrown,   new Point(btnX, btnY), 160); btnX += 160 + gap;
        _btnF6StopReason = MakeFuncButton("[F6] Stop Reason",      Color.DarkRed,       new Point(btnX, btnY), 170); btnX += 170 + gap;
        _btnF7EventLog = MakeFuncButton("[F7] Event Log",         Color.DarkOliveGreen,new Point(btnX, btnY), 155); btnX += 155 + gap;
        _btnF8Stats = MakeFuncButton("[F8] Statistics",        Color.DarkOliveGreen,new Point(btnX, btnY), 155);

        _btnF1Reprint.Click      += BtnF1Reprint_Click;
        _btnF3ReprintByStack.Click += BtnF3ReprintByStack_Click;
        _btnF4NewSetup.Click     += (_, _) => ShowStartup();
        _btnF6StopReason.Click   += BtnF6StopReason_Click;
        _btnF7EventLog.Click     += BtnF7EventLog_Click;
        _btnF8Stats.Click        += BtnF8Stats_Click;

        _browsePanel.Controls.AddRange(new Control[]
        {
            _btnF1Reprint, _btnF3ReprintByStack, _btnF4NewSetup,
            _btnF6StopReason, _btnF7EventLog, _btnF8Stats,
        });

        // Status bar
        _statusLabel = new Label
        {
            Location = new Point(8, 486), Size = new Size(1100, 48),
            ForeColor = Color.LightGray, Font = new Font("Segoe UI", 9.5f),
        };
        _browsePanel.Controls.Add(_statusLabel);

        // Simulate Carton Read button (bottom-right)
        var btnSimulate = new Button
        {
            Text = "📦 Manual PLC",
            Location = new Point(990, 545), Size = new Size(110, 44),
            BackColor = Color.DarkOrange, ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        };
        btnSimulate.Click += BtnSimulateCartonRead_Click;
        _browsePanel.Controls.Add(btnSimulate);

        // Auto-refresh timer (1 second — same as Progress "pause 1" in choose row)
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _refreshTimer.Tick += async (_, _) => await RefreshBrowseAsync();

        Controls.Add(_browsePanel);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Panel switching
    // ─────────────────────────────────────────────────────────────────────────
    private void ShowStartup()
    {
        _refreshTimer.Stop();
        _browsePanel.Dock    = DockStyle.None;
        _browsePanel.Visible = false;
        _startupPanel.Dock    = DockStyle.Fill;
        _startupPanel.Visible = true;
        _shiftInput.Focus();
    }

    private void ShowBrowse()
    {
        _startupPanel.Dock    = DockStyle.None;
        _startupPanel.Visible = false;
        _browsePanel.Dock    = DockStyle.Fill;
        _browsePanel.Visible = true;
        _browseTitleLabel.Text = $"Shift {_shift}   Last Boxes Out of SortLine {_config.LineNumber:00}";
        _lastBoxTime = DateTime.MinValue; // force immediate repaint
        _ = RefreshBrowseAsync();
        _refreshTimer.Start();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  BROWSE DATA LOADING
    // ═══════════════════════════════════════════════════════════════════════════
    private async Task RefreshBrowseAsync()
    {
        try
        {
            var boxes = await _boxRepo.GetLastBoxesAsync(_config.LineNumber, 12, CancellationToken.None);

            // Only repaint when newest box time changed (Progress: oldlastboxtime check)
            var newest = boxes.FirstOrDefault()?.MakeTime ?? DateTime.MinValue;
            bool changed = newest != _lastBoxTime;
            _lastBoxTime = newest;
            if (!changed && _browseRows.Count > 0)
            {
                // Still refresh stopped state
                CheckStoppedState();
                return;
            }

            _browseRows.Clear();
            foreach (var box in boxes)
            {
                var row = new BoxBrowseRow
                {
                    RecId         = box.RecId,
                    TimeDisplay   = box.TimeDisplay,
                    SorterMessage = box.SorterMessage,
                    PartOrError   = box.ErrMsg,
                    StackNum      = box.StackNum,
                    HasError      = !string.IsNullOrEmpty(box.ErrMsg),
                };

                // Progress: if box.bx-printnum > 0 and box.bx-errmsg eq "" → stacker lookup
                if (box.CanPrint)
                {
                    var stacker = await _boxRepo.GetStackerAsync(
                        _config.LineNumber, box.StackNum.Trim(), CancellationToken.None);
                    if (stacker is not null)
                    {
                        var display = await _boxRepo.GetItemDisplayAsync(
                            stacker.IRef, stacker.IsMexicoItem,
                            stacker.Shade, stacker.Size,
                            _config.W4DigitShade, CancellationToken.None);
                        if (display is not null) row.PartOrError = display;
                    }
                }
                _browseRows.Add(row);
            }

            CheckStoppedState();
            UpdateButtonStates();
        }
        catch (Exception ex)
        {
            SetStatus($"Refresh error: {ex.Message}");
        }
    }

    private void CheckStoppedState()
    {
        _isStopped = false;
        try
        {
            if (File.Exists(_config.NoGoFilePath))
            {
                var content = File.ReadAllText(_config.NoGoFilePath);
                // Progress: if substring(stpchr,1,2) eq "NO" then IsStopped = true
                _isStopped = content.TrimStart().StartsWith("NO", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { /* file may be locked or absent */ }

        _stoppedLabel.Text = _isStopped ? "*** STOPPED ***" : string.Empty;
    }

    /// <summary>
    /// Async lookup: Primary Item description (ColorDesc | ShapeDesc | SeriesDesc).
    /// Called when user presses Enter or Tab in the Primary Item field.
    /// Updates the description label with lookup result or error message.
    /// </summary>
    private async Task RefreshPrimaryItemDescriptionAsync()
    {
        if (_primaryItemInput == null || _primaryItemDescLabel == null)
            return;

        var itemNumber = _primaryItemInput.Text.Trim();
        if (itemNumber.Length == 0)
        {
            _primaryItemDescLabel.Text = "(Enter item number to see description)";
            _primaryItemDescLabel.ForeColor = Color.LightCyan;
            return;
        }

        try
        {
            // Search both itemdet and mitemdet CSV files
            var itemDetail = await _boxRepo.GetItemDetailByNumberAsync(
                itemNumber, searchMexicoAlso: true, CancellationToken.None);

            if (itemDetail != null)
            {
                // Display format: "ColorDesc | ShapeDesc | SeriesDesc"
                var description = itemDetail.GetPrimaryItemDescription();
                _primaryItemDescLabel.Text = description;
                _primaryItemDescLabel.ForeColor = Color.LightGreen;
            }
            else
            {
                // Item not found
                _primaryItemDescLabel.Text = $"⚠ Item '{itemNumber}' not found in item master";
                _primaryItemDescLabel.ForeColor = Color.Salmon;
            }
        }
        catch (Exception ex)
        {
            _primaryItemDescLabel.Text = $"Error loading description: {ex.Message}";
            _primaryItemDescLabel.ForeColor = Color.Salmon;
        }
    }

    private void UpdateButtonStates()
    {
        bool rowOk = _browseGrid.CurrentRow?.DataBoundItem is BoxBrowseRow r && !r.HasError;
        _btnF1Reprint.Enabled    = rowOk;
        _btnF6StopReason.Enabled = _isStopped;
        _btnF7EventLog.Enabled   = _config.ResolvedLogFilePath is not null;
        _btnF8Stats.Enabled      = _config.ResolvedRptFilePath is not null;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  BUTTON / F-KEY HANDLERS
    // ═══════════════════════════════════════════════════════════════════════════

    // F1 — Reprint selected box
    private void BtnF1Reprint_Click(object? sender, EventArgs e)
    {
        if (_browseGrid.CurrentRow?.DataBoundItem is not BoxBrowseRow row)
        {
            SetStatus("Select a box row to reprint.");
            return;
        }
        if (row.HasError)
        {
            MessageBox.Show("CANNOT PRINT THIS — box has an error.", "Reprint",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Find the corresponding box record for export
        _ = Task.Run(async () =>
        {
            try
            {
                var boxes = await _boxRepo.GetLastBoxesAsync(_config.LineNumber, 12, CancellationToken.None);
                var box = boxes.FirstOrDefault(b => b.StackNum.Trim() == row.StackNum.Trim());
                if (box != null)
                {
                    var stacker = await _boxRepo.GetStackerAsync(_config.LineNumber, row.StackNum.Trim(), CancellationToken.None);
                    // ItemDisplay is the "PartOrError" field from the row
                    await ExportLabelToNiceLabelXmlAsync(box, stacker, row.PartOrError, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Export failed: {ex.Message}");
            }
        });

        SendReprint(row.StackNum.Trim());
    }

    // F3 — Reprint by stack number (dialog)
    private void BtnF3ReprintByStack_Click(object? sender, EventArgs e)
    {
        int defaultStack = 1;
        if (_browseGrid.CurrentRow?.DataBoundItem is BoxBrowseRow row &&
            int.TryParse(row.StackNum.Trim(), out var sn))
            defaultStack = sn;

        using var dlg = new ReprintByStackForm(defaultStack);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _ = ValidateAndReprintByStackAsync(dlg.StackNumber.ToString("00"));
    }

    private async Task ValidateAndReprintByStackAsync(string stackNum)
    {
        // Progress: find stacker where st-errmsg begins "*OK" or st-errmsg eq ""
        var stacker = await _boxRepo.GetStackerAsync(_config.LineNumber, stackNum, CancellationToken.None);
        if (stacker is null || !stacker.IsValid)
        {
            MessageBox.Show(
                $"Stack {stackNum} SETUP: {stacker?.ErrMsg ?? "not found"}",
                "Reprint by Stack", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        SendReprint(stackNum);
    }

    private void SendReprint(string stackNum)
    {
        try
        {
            // Progress: p-plc-info = stackNum + ",Y, ,  ," → echo >> plc##
            _plcPipe.SendReprint(stackNum, _config.PlcPipePath);
            SetStatus($"Reprint request sent for stack {stackNum}.");
            _ = RefreshBrowseAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Error sending reprint: {ex.Message}");
        }
    }

    /// <summary>
    /// Exports label data to NiceLabel XML format if configured.
    /// </summary>
    private async Task ExportLabelToNiceLabelXmlAsync(BoxRecord box, StackerRecord? stacker, string itemDisplay, CancellationToken ct)
    {
        try
        {
            // Check if output type is NiceLabel Xml
            var outputType = _settings.LabelOutputType?.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(outputType, "NiceLabelXml", StringComparison.OrdinalIgnoreCase))
                return;

            if (string.IsNullOrWhiteSpace(_settings.LabelOutputAddress))
            {
                SetStatus("Warning: NiceLabel Xml selected but Output Address is empty.");
                return;
            }

            var exporter = new NiceLabelXmlExporter(_settings.LabelOutputAddress);
            var success = await exporter.ExportAsync(box, stacker, itemDisplay, ct);

            if (success)
                SetStatus($"Label exported to: {_settings.LabelOutputAddress}");
            else
                SetStatus($"Warning: Failed to export label to {_settings.LabelOutputAddress}");
        }
        catch (Exception ex)
        {
            SetStatus($"Export error: {ex.Message}");
        }
    }

    // F6 — View stop reason
    private void BtnF6StopReason_Click(object? sender, EventArgs e)
    {
        if (!_isStopped) return;

        using var dlg = new StopReasonForm(_config.NoGoFilePath, () =>
        {
            // Progress: echo RESET >> Match##.txt
            try { _plcPipe.SendReset(_config.MatchFilePath); }
            catch (Exception ex) { SetStatus($"Reset error: {ex.Message}"); }
        });
        dlg.ShowDialog(this);
        _ = RefreshBrowseAsync();
    }

    // F7 — View event log
    private void BtnF7EventLog_Click(object? sender, EventArgs e)
    {
        var path = _config.ResolvedLogFilePath;
        if (path is null) { SetStatus("Log file not found."); return; }
        using var viewer = new FileViewerForm($"Scanner/Carton Event Log — Line {_config.LineNumber:00}", path);
        viewer.ShowDialog(this);
        _ = RefreshBrowseAsync();
    }

    // F8 — View statistics report
    private void BtnF8Stats_Click(object? sender, EventArgs e)
    {
        var path = _config.ResolvedRptFilePath;
        if (path is null) { SetStatus("Statistics report not found."); return; }
        using var viewer = new FileViewerForm($"Scanner Statistics Report — Line {_config.LineNumber:00}", path);
        viewer.ShowDialog(this);
        _ = RefreshBrowseAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  KEYBOARD SHORTCUTS  (F1-F8 hardware keys work identically to buttons)
    // ═══════════════════════════════════════════════════════════════════════════
    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_browsePanel.Visible)
        {
            switch (e.KeyCode)
            {
                case Keys.F1: if (_btnF1Reprint.Enabled)    BtnF1Reprint_Click(null, EventArgs.Empty);      e.Handled = true; break;
                case Keys.F3:                               BtnF3ReprintByStack_Click(null, EventArgs.Empty); e.Handled = true; break;
                case Keys.F4:                               ShowStartup();                                    e.Handled = true; break;
                case Keys.F6: if (_btnF6StopReason.Enabled) BtnF6StopReason_Click(null, EventArgs.Empty);    e.Handled = true; break;
                case Keys.F7: if (_btnF7EventLog.Enabled)   BtnF7EventLog_Click(null, EventArgs.Empty);      e.Handled = true; break;
                case Keys.F8: if (_btnF8Stats.Enabled)      BtnF8Stats_Click(null, EventArgs.Empty);         e.Handled = true; break;
            }
        }
        else if (_startupPanel.Visible && e.KeyCode == Keys.F4)
        {
            Application.Exit();
            e.Handled = true;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════════════════
    private void SetStatus(string message) => _statusLabel.Text = message;

    private static Label MakeLabel(string text, Point location, int width) => new()
    {
        Text = text, Location = location, Size = new Size(width, 26),
        ForeColor = Color.White, Font = new Font("Segoe UI", 10f),
    };

    private static Button MakeFuncButton(string text, Color back, Point location, int width) => new()
    {
        Text = text, Location = location, Size = new Size(width, 44),
        BackColor = back, ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
    };

    // ═══════════════════════════════════════════════════════════════════════════
    //  BROWSE ROW VIEW MODEL
    // ═══════════════════════════════════════════════════════════════════════════
    private sealed class BoxBrowseRow
    {
        public int RecId { get; set; }
        public string TimeDisplay   { get; set; } = string.Empty;
        public string SorterMessage { get; set; } = string.Empty;
        public string PartOrError   { get; set; } = string.Empty;
        public string StackNum      { get; set; } = string.Empty;
        public bool HasError        { get; set; }
    }
}
