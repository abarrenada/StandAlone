using StandAlone.CartonUi.Forms;
using StandAlone.CartonUi.Models;
using StandAlone.CartonUi.Services;
using StandAlone.Integration.Services;
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
    private CancellationTokenSource? _plcMonitorCts;
    private Task? _plcMonitorTask;
    // Per-stack-number debounce/in-flight guard — scoped per stacker so two DIFFERENT
    // stackers firing close together don't block each other, only rapid repeats of the SAME one.
    private readonly Dictionary<int, DateTime> _lastStackerFireUtc = new();
    private readonly HashSet<int> _stackerHandlingInProgress = new();

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
    private ItemDetail? _primaryItemDetail;
    private TextBox? _secondaryItemInput;
    private NumericUpDown? _manualQtyInput;
    private TextBox? _shopOrderInput;
    private TextBox? _shadeManualInput;
    private TextBox? _caliberInput;
    private Label? _manualStatusLabel;
    private Button _btnBegin = null!;
    private Button? _btnPrintCarton;
    private Button? _btnPrintPallet;
    // Read-only display TextBoxes for Manual Qty mode
    private TextBox? _lisQtyDisplay;
    private TextBox? _cartonQtyDisplay;
    private TextBox? _palletQtyDisplay;
    private TextBox? _lineDisplay;

    // ── Pallet query panel controls ───────────────────────────────────────────
    private Panel? _palletQueryPanel;
    private TextBox? _serialNoInput;
    private TextBox? _palletItemNoDisplay;
    private Label? _palletDescLabel;
    private TextBox? _palletShopOrderDisplay;
    private TextBox? _palletLisQtyDisplay;
    private TextBox? _palletCartonQtyDisplay;
    private TextBox? _palletPalletQtyDisplay;
    private TextBox? _palletShadeDisplay;
    private TextBox? _palletCaliberDisplay;
    private TextBox? _palletShiftDisplay;
    private TextBox? _palletLineDisplay;
    private Label? _palletStatusLabel;
    private Button? _btnPalletPrint;
    private ItemDetail? _palletQueryItemDetail;
    private string _palletQueryCartonBarcode = string.Empty;

    // ── Browse panel controls ─────────────────────────────────────────────────
    private Panel _browsePanel = null!;
    private DataGridView _browseGrid = null!;
    private Label _browseTitleLabel = null!;
    private Label _stoppedLabel = null!;
    private Label _statusLabel = null!;
    private ListBox _plcInputList = null!;
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
        _settings = SettingsManager.Load();
        _boxRepo = new FileBoxRepository(_config.DataDirectory, _settings.PalletsCsvPath);
        _plcPipe = new FilePlcPipeService();

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

        // Pre-fill item description, shade, and shop order from saved item number
        Load += async (_, _) => await RefreshPrimaryItemDescriptionAsync();

        StartPlcIpMonitorIfConfigured();
        FormClosing += (_, _) => StopPlcIpMonitor();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  STARTUP PANEL — equivalent to Progress startup-frm frame
    // ═══════════════════════════════════════════════════════════════════════════
    private void BuildStartupPanel()
    {
        _startupPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.MidnightBlue };

        const int labelX = 40;
        const int inputX = 250;
        const int inputW = 240;

        bool isManualQtyMode = string.Equals(_settings.CartonPrintMode, "Manual Qty", StringComparison.OrdinalIgnoreCase);
        if (isManualQtyMode)
            ClientSize = new Size(1200, 720);

        // ── Corner info labels ────────────────────────────────────────────────
        _startupPanel.Controls.Add(new Label
        {
            Text = $"User: {Environment.UserName}",
            Location = new Point(8, 8), AutoSize = true,
            Font = new Font("Segoe UI", 8f), ForeColor = Color.LightCyan,
        });
        var printerInfo = string.IsNullOrWhiteSpace(_settings.LabelOutputAddress)
            ? "(printer not configured)"
            : $"{_settings.ThermalPrinterType}  {_settings.LabelOutputAddress}";
        _startupPanel.Controls.Add(new Label
        {
            Text = printerInfo,
            Location = new Point(580, 8), Size = new Size(532, 18),
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 8f), ForeColor = Color.LightCyan,
        });

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

        // ── Right-aligned label helper — colon lands at x=245, field starts at x=250 ──
        Label MakeRL(string text, int y, int w = 90) => new Label
        {
            Text = text, Location = new Point(inputX - 5 - w, y), Size = new Size(w, 26),
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.White, Font = new Font("Segoe UI", 10f),
        };

        // ── Top fields — flow layout starting at y=120 ────────────────────────
        // Shift is shown here only in PLC mode; Manual mode places it below with the job fields.
        int topY = 120;

        if (!isManualQtyMode)
        {
            _startupPanel.Controls.Add(MakeRL("Shift:", topY + 5));
            _shiftInput = new NumericUpDown
            {
                Location = new Point(inputX, topY), Size = new Size(80, 30),
                Minimum = 1, Maximum = 9, Value = 1,
                Font = new Font("Segoe UI", 11f),
                BackColor = Color.White, ForeColor = Color.Black,
            };
            _startupPanel.Controls.Add(_shiftInput);
            topY += 60;
        }

        _startupPanel.Controls.Add(MakeRL("Inspector:", topY + 5));
        _inspectorInput = new TextBox
        {
            Location = new Point(inputX, topY), Size = new Size(inputW, 30),
            MaxLength = 20, Font = new Font("Segoe UI", 11f),
            BackColor = Color.White, ForeColor = Color.Black,
        };
        _startupPanel.Controls.Add(_inspectorInput);
        topY += 60;

        _startupPanel.Controls.Add(MakeRL("Label Size:", topY + 5));
        _labelSizeCombo = new ComboBox
        {
            Location = new Point(inputX, topY), Size = new Size(inputW, 30),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 11f),
        };
        PopulateLabelSizes();
        _startupPanel.Controls.Add(_labelSizeCombo);
        topY += 40;

        var sizeListText = BuildLabelSizeListText();
        if (!string.IsNullOrEmpty(sizeListText))
        {
            _startupPanel.Controls.Add(new Label
            {
                Location = new Point(inputX, topY), Size = new Size(600, 60),
                ForeColor = Color.LightCyan, Font = new Font("Courier New", 8f),
                Text = sizeListText,
            });
            topY += 65;
        }

        // ── Dynamic fields ─────────────────────────────────────────────────────
        int nextY = topY + 20;

        // Item Number — always visible in Manual Qty mode; conditional on DoesManStk in PLC mode
        if (isManualQtyMode || (_config.DoesManStk && !_config.DoesMexico))
        {
            _startupPanel.Controls.Add(MakeRL("Primary Item:", nextY + 5, 105));
            _primaryItemInput = new TextBox
            {
                Location = new Point(inputX, nextY), Size = new Size(inputW, 30),
                MaxLength = 15, Font = new Font("Segoe UI", 11f),
                Text = _settings.CurrentPrimaryItem,
                BackColor = Color.White, ForeColor = Color.Black,
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

        // 2nd Primary Item — shown in either print mode when the line is set up for two
        // primary items (DoesTwoPrims describes the line's setup, not the print mode).
        if (_config.DoesTwoPrims && _config.DoesManStk && !_config.DoesMexico)
        {
            _startupPanel.Controls.Add(MakeRL("2nd Primary Item:", nextY + 5, 130));
            _secondaryItemInput = new TextBox
            {
                Location = new Point(inputX, nextY), Size = new Size(inputW, 30),
                MaxLength = 15, Font = new Font("Segoe UI", 11f),
                Text = _settings.CurrentSecondaryItem,
                BackColor = Color.White, ForeColor = Color.Black,
            };
            // Save on change so it persists even in Manual Qty mode, where "Begin" (the other
            // save path, below) never runs.
            _secondaryItemInput.Leave += (_, _) =>
            {
                var secondaryItem = _secondaryItemInput.Text.Trim();
                _settings.CurrentSecondaryItem = secondaryItem;
                SettingsManager.SaveField(s => s.CurrentSecondaryItem = secondaryItem);
            };
            _startupPanel.Controls.Add(_secondaryItemInput);
            nextY += 50;
        }

        if (isManualQtyMode)
        {
            TextBox MakeRO(int x, int y, int w) => new TextBox
            {
                Location = new Point(x, y), Size = new Size(w, 28),
                ReadOnly = true, BackColor = Color.White,
                ForeColor = Color.Black, Font = new Font("Segoe UI", 10f),
            };

            // Shop Order
            _startupPanel.Controls.Add(MakeRL("Shop Order:", nextY + 5));
            _shopOrderInput = new TextBox
            {
                Location = new Point(inputX, nextY), Size = new Size(inputW, 30),
                MaxLength = 30, Font = new Font("Segoe UI", 11f),
                BackColor = Color.White, ForeColor = Color.Black,
            };
            _startupPanel.Controls.Add(_shopOrderInput);
            nextY += 48;

            // LIS Qty | Carton Qty | Pallet Qty — read-only info on one row
            _startupPanel.Controls.Add(MakeLabel("LIS Qty:", new Point(labelX, nextY + 5), 75));
            _lisQtyDisplay = MakeRO(labelX + 80, nextY, 65);
            _startupPanel.Controls.Add(_lisQtyDisplay);

            _startupPanel.Controls.Add(MakeLabel("Carton Qty:", new Point(labelX + 158, nextY + 5), 90));
            _cartonQtyDisplay = MakeRO(labelX + 253, nextY, 85);
            _startupPanel.Controls.Add(_cartonQtyDisplay);

            _startupPanel.Controls.Add(MakeLabel("Pallet Qty:", new Point(labelX + 352, nextY + 5), 85));
            _palletQtyDisplay = MakeRO(labelX + 442, nextY, 60);
            _startupPanel.Controls.Add(_palletQtyDisplay);
            nextY += 44;

            // Shade + Caliber — Shade colon aligns with first-column labels (x=245)
            _startupPanel.Controls.Add(MakeRL("Shade:", nextY + 5));
            _shadeManualInput = new TextBox
            {
                Location = new Point(inputX, nextY), Size = new Size(100, 30),
                MaxLength = 10, Font = new Font("Segoe UI", 11f),
                BackColor = Color.White, ForeColor = Color.Black,
            };
            _startupPanel.Controls.Add(_shadeManualInput);

            _startupPanel.Controls.Add(MakeLabel("Caliber:", new Point(inputX + 116, nextY + 4), 80));
            _caliberInput = new TextBox
            {
                Location = new Point(inputX + 200, nextY), Size = new Size(80, 30),
                MaxLength = 10, Font = new Font("Segoe UI", 11f),
                BackColor = Color.White, ForeColor = Color.Black,
            };
            _startupPanel.Controls.Add(_caliberInput);
            nextY += 48;

            // Shift (editable, replaces top shift field in manual mode) + Line
            _startupPanel.Controls.Add(MakeRL("Shift:", nextY + 5));
            _shiftInput = new NumericUpDown
            {
                Location = new Point(inputX, nextY), Size = new Size(80, 30),
                Minimum = 1, Maximum = 9, Value = 1,
                Font = new Font("Segoe UI", 11f),
                BackColor = Color.White, ForeColor = Color.Black,
            };
            _startupPanel.Controls.Add(_shiftInput);

            _startupPanel.Controls.Add(MakeLabel("Line:", new Point(inputX + 96, nextY + 5), 50));
            _lineDisplay = MakeRO(inputX + 150, nextY, 50);
            _lineDisplay.Text = _config.LineNumber.ToString("00");
            _startupPanel.Controls.Add(_lineDisplay);
            nextY += 44;

            // Labels to Print
            _startupPanel.Controls.Add(MakeRL("Labels to Print:", nextY + 5, 125));
            _manualQtyInput = new NumericUpDown
            {
                Location = new Point(inputX, nextY), Size = new Size(120, 30),
                Minimum = 1, Maximum = 999, Value = 1,
                Font = new Font("Segoe UI", 11f),
                BackColor = Color.White, ForeColor = Color.Black,
            };
            _startupPanel.Controls.Add(_manualQtyInput);
            nextY += 46;

            // Status feedback label
            _manualStatusLabel = new Label
            {
                Location = new Point(inputX, nextY), Size = new Size(inputW + 200, 24),
                ForeColor = Color.LightGreen, Font = new Font("Segoe UI", 9f),
                AutoSize = false,
            };
            _startupPanel.Controls.Add(_manualStatusLabel);
            nextY += 28;
        }

        // ── Buttons ───────────────────────────────────────────────────────────
        int btnY = nextY + 10;

        _btnBegin = new Button
        {
            Text = "Begin  [Enter]",
            Size = new Size(150, 44), Location = new Point(inputX, btnY),
            BackColor = isManualQtyMode ? Color.DimGray : Color.DarkGreen,
            ForeColor = isManualQtyMode ? Color.DarkGray : Color.White,
            FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Enabled = !isManualQtyMode,
            // In Manual Qty mode, Print Carton sits at this exact same spot (freeing up
            // horizontal room for the rest of the button row) — Enabled=false alone left Begin
            // fully opaque and on top, hiding Print Carton underneath it entirely.
            Visible = !isManualQtyMode,
        };
        if (!isManualQtyMode)
            _btnBegin.Click += BtnBegin_Click;
        _startupPanel.Controls.Add(_btnBegin);

        int nextBtnX = inputX ;
       
        if (isManualQtyMode)
        {
            _btnPrintCarton = new Button
            {
                Text = "Print Carton",
                Size = new Size(150, 44), Location = new Point(nextBtnX, btnY),
                BackColor = Color.DarkGreen, ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            };
            _btnPrintCarton.Click += (_, _) => _ = PrintManualAsync(isPallet: false);
            _startupPanel.Controls.Add(_btnPrintCarton);
            nextBtnX += 156;

            _btnPrintPallet = new Button
            {
                Text = "Print Pallet",
                Size = new Size(150, 44), Location = new Point(nextBtnX, btnY),
                BackColor = Color.DarkSlateBlue, ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            };
            _btnPrintPallet.Click += (_, _) => ShowPalletQuery();
            _startupPanel.Controls.Add(_btnPrintPallet);
            nextBtnX += 156;

            var btnPalletScan = new Button
            {
                Text = "Pallet Scan",
                Size = new Size(150, 44), Location = new Point(nextBtnX, btnY),
                BackColor = Color.DarkGreen, ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            };
            btnPalletScan.Click += (_, _) => OpenPalletScanForm();
            _startupPanel.Controls.Add(btnPalletScan);
            nextBtnX += 156;

            var btnEolScan = new Button
            {
                Text = "EOL Scan",
                Size = new Size(150, 44), Location = new Point(nextBtnX, btnY),
                BackColor = Color.DarkOrange, ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            };
            btnEolScan.Click += (_, _) => OpenEolScanForm();
            _startupPanel.Controls.Add(btnEolScan);
            nextBtnX += 156;
        }

        // Visible in both Carton Print Modes — stacker→item setup is a maintenance task
        // independent of how the line prints, and (per the person who does this work) it
        // must NOT sit behind the Settings password gate.
        var btnStackerMaint = new Button
        {
            Text = "Stacker Maint.",
            Size = new Size(150, 44), Location = new Point(nextBtnX, btnY),
            BackColor = Color.SaddleBrown, ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 11f, FontStyle.Bold),
        };
        btnStackerMaint.Click += (_, _) => OpenStackerMaintenanceForm();
        _startupPanel.Controls.Add(btnStackerMaint);
        nextBtnX += 156;

        var btnExit = new Button
        {
            Text = "Exit  [F4]",
            Size = new Size(130, 44), Location = new Point(nextBtnX, btnY),
            BackColor = Color.DarkRed, ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 11f),
        };
        btnExit.Click += (_, _) => Application.Exit();
        _startupPanel.Controls.Add(btnExit);

        AcceptButton = isManualQtyMode ? null : _btnBegin;
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
            _labelSizeCombo.Items.Add(new LabelSizeOption { SizeCode = "4x3",   Prompt = "4x3 Standard",    IncludeUs = true });
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
    private async void BtnBegin_Click(object? sender, EventArgs e)
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

        // Validate primary item: must have open qty and a current/future schedule date
        if (_config.DoesManStk && !_config.DoesMexico && _primaryItemInput != null)
        {
            var itemNumber = _primaryItemInput.Text.Trim();
            if (!string.IsNullOrEmpty(itemNumber))
            {
                var item = (_primaryItemDetail?.ItemNumber.Trim().Equals(itemNumber, StringComparison.OrdinalIgnoreCase) == true)
                    ? _primaryItemDetail
                    : await _boxRepo.GetItemDetailByNumberAsync(itemNumber, searchMexicoAlso: true, CancellationToken.None);

                if (item == null)
                {
                    MessageBox.Show($"Item '{itemNumber}' not found in item master.", "Cannot Proceed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _primaryItemInput.Focus();
                    return;
                }

                if (item.OpenQty <= 0)
                {
                    MessageBox.Show(
                        $"Item '{itemNumber}' has no open quantity on schedule.\nCannot proceed until open quantity is available.",
                        "Cannot Proceed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _primaryItemInput.Focus();
                    return;
                }

                if (!DateTime.TryParse(item.ScheduleDate, out var schedDate) || schedDate.Date < DateTime.Today)
                {
                    var dateDisplay = string.IsNullOrEmpty(item.ScheduleDate) ? "(not set)" : item.ScheduleDate;
                    MessageBox.Show(
                        $"Item '{itemNumber}' schedule date ({dateDisplay}) must be today or a future date.\nCannot proceed until schedule date criteria is met.",
                        "Cannot Proceed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _primaryItemInput.Focus();
                    return;
                }
            }
        }

        // Save primary and secondary items for next session (read-modify-write so this
        // doesn't clobber other fields changed by a separate Settings session meanwhile)
        var primaryItemToSave = _primaryItemInput?.Text.Trim();
        var secondaryItemToSave = _secondaryItemInput?.Text.Trim();
        if (primaryItemToSave is not null) _settings.CurrentPrimaryItem = primaryItemToSave;
        if (secondaryItemToSave is not null) _settings.CurrentSecondaryItem = secondaryItemToSave;
        SettingsManager.SaveField(s =>
        {
            if (primaryItemToSave is not null) s.CurrentPrimaryItem = primaryItemToSave;
            if (secondaryItemToSave is not null) s.CurrentSecondaryItem = secondaryItemToSave;
        });

        if (string.Equals(_settings.CartonPrintMode, "Manual Qty", StringComparison.OrdinalIgnoreCase))
        {
            // Do not auto-print on Begin. Enter run mode and wait for PLC/read-triggered
            // workflow or explicit user print action.
            ShowBrowse();
            SetStatus("Manual Qty mode ready. Waiting for PLC/read-triggered or explicit print action.");
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
                // Cycle through stackers 1-3 so repeated clicks are visible in the browse
                // panel. Delegates to the exact same handler a real PLC signal uses (stacker
                // lookup, item resolution, barcode, box record, print) so this test button can
                // never drift out of sync with production behavior the way a separate,
                // hand-duplicated simulation used to.
                var existingBoxes = await _boxRepo.GetLastBoxesAsync(_config.LineNumber, 999, CancellationToken.None);
                var testStack = (existingBoxes.Count % 3) + 1;

                await HandleStackerCartonEventAsync(testStack, DateTime.Now);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error simulating carton read:\r\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        });
    }

    private async Task PrintManualAsync(bool isPallet)
    {
        var itemNumber = _primaryItemInput?.Text.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(itemNumber))
        {
            MessageBox.Show("Enter an item number before printing.", "Input Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _primaryItemInput?.Focus();
            return;
        }

        var shift     = (int)_shiftInput.Value;
        var inspector = _inspectorInput.Text.Trim();
        if (inspector.Length == 0)
        {
            MessageBox.Show("Inspector must be entered.", "Input Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _inspectorInput.Focus();
            return;
        }
        while (inspector.Length < 2) inspector += " ";

        if (_labelSizeCombo.SelectedItem is not LabelSizeOption selectedSize)
        {
            MessageBox.Show("Please select a valid label size.", "Input Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _labelSizeCombo.Focus();
            return;
        }

        var quantity = _manualQtyInput is not null ? (int)_manualQtyInput.Value : 1;

        var item = (_primaryItemDetail?.ItemNumber.Trim().Equals(itemNumber, StringComparison.OrdinalIgnoreCase) == true)
            ? _primaryItemDetail
            : await _boxRepo.GetItemDetailByNumberAsync(itemNumber, searchMexicoAlso: true, CancellationToken.None);

        if (item is null)
        {
            MessageBox.Show($"Item '{itemNumber}' not found in item master.", "Cannot Print", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _primaryItemInput?.Focus();
            return;
        }

        if (item.OpenQty <= 0)
        {
            MessageBox.Show(
                $"Item '{itemNumber}' has no open quantity on schedule.\nCannot proceed until open quantity is available.",
                "Cannot Print", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _primaryItemInput?.Focus();
            return;
        }

        if (!DateTime.TryParse(item.ScheduleDate, out var schedDate) || schedDate.Date < DateTime.Today)
        {
            var dateDisplay = string.IsNullOrEmpty(item.ScheduleDate) ? "(not set)" : item.ScheduleDate;
            MessageBox.Show(
                $"Item '{itemNumber}' schedule date ({dateDisplay}) must be today or a future date.",
                "Cannot Print", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _primaryItemInput?.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.LabelOutputAddress))
        {
            MessageBox.Show("Label Output Address is not configured. Go to Settings.", "Cannot Print", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var shopOrder     = _shopOrderInput?.Text.Trim() ?? string.Empty;
        var shadeOverride = _shadeManualInput?.Text.Trim() ?? string.Empty;
        var caliber       = _caliberInput?.Text.Trim() ?? string.Empty;
        var labelFormat   = isPallet ? "PALLET_LABEL" : "CARTON_LABEL";

        // Snapshot session fields so the print job uses current UI values
        _shift     = shift;
        _inspector = inspector;
        _labelSize = selectedSize.SizeCode;

        var job = new ManualCartonPrintJob(
            item.ItemNumber,
            quantity,
            inspector,
            shift,
            selectedSize.SizeCode,
            item.GetPrimaryItemDescription(),
            Environment.UserName,
            DateTime.Now,
            shopOrder,
            shadeOverride,
            caliber,
            labelFormat);

        _settings.CurrentPrimaryItem = itemNumber;
        SettingsManager.SaveField(s => s.CurrentPrimaryItem = itemNumber);

        var labelKind = isPallet ? "Pallet" : "Carton";
        var (success, barcodeSerial) = await ExportManualLabelAsync(item, job, CancellationToken.None);

        if (success)
        {
            if (!isPallet && !string.IsNullOrEmpty(barcodeSerial))
            {
                // Register a box record so this carton can later be found by barcode serial
                // (pallet-scan lookup) - manual prints otherwise never appear in boxes*.csv.
                var boxFile = Path.Combine(_config.DataDirectory, $"boxes{_config.LineNumber:00}.csv");
                var existingCount = File.Exists(boxFile)
                    ? File.ReadAllLines(boxFile).Count(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#'))
                    : 0;
                var stackNumber = (existingCount % 99) + 1;
                AppendCartonBoxRecord(item, stackNumber, barcodeSerial, DateTime.Now);
            }

            var msg = $"{labelKind} label sent: {item.ItemNumber} ×{quantity}" +
                      (string.IsNullOrEmpty(shopOrder) ? string.Empty : $"  |  Order: {shopOrder}");
            if (_manualStatusLabel is not null)
            {
                _manualStatusLabel.Text = msg;
                _manualStatusLabel.ForeColor = Color.LightGreen;
            }
        }
        else
        {
            var errMsg = $"Failed to send {labelKind.ToLower()} label.";
            if (_manualStatusLabel is not null)
            {
                _manualStatusLabel.Text = errMsg;
                _manualStatusLabel.ForeColor = Color.Salmon;
            }
            MessageBox.Show(errMsg, "Print Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

    /// <summary>
    /// Appends a box record so a printed carton can later be found by barcode serial
    /// (pallet-scan lookup) or stack number. barcodeSerial should match what was actually printed
    /// on the label (see ThermalPrinterCommandBuilder.ComputeCartonBarcodeSerial).
    ///
    /// Does NOT touch stackers{NN}.csv — that table is operator-maintained configuration
    /// (Stacker Maintenance screen: which item/shade/size each physical stacker is assigned),
    /// read here to resolve what to print, never written as a side effect of a carton event.
    /// (It previously was written here on every single carton — auto-appending a stacker
    /// row per event, unbounded — which is why stackers01.csv had accumulated dozens of
    /// duplicate/garbage rows.)
    /// </summary>
    private void AppendCartonBoxRecord(ItemDetail itemDetail, int stackNumber, string barcodeSerial, DateTime timestamp)
    {
        var boxFile = Path.Combine(_config.DataDirectory, $"boxes{_config.LineNumber:00}.csv");

        int nextRecId = 1;
        if (File.Exists(boxFile))
        {
            foreach (var line in File.ReadAllLines(boxFile))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith("RecId", StringComparison.OrdinalIgnoreCase))
                    continue;

                var parts = line.Split(',');
                if (parts.Length >= 1 && int.TryParse(parts[0].Trim().Trim('"'), out var recId))
                    nextRecId = Math.Max(nextRecId, recId + 1);
            }
        }

        var stackNum = $" {stackNumber}";
        var plcMsg = stackNum + new string(' ', 31) + itemDetail.ItemNumber.PadRight(30);
        if (plcMsg.Length < 65) plcMsg = plcMsg.PadRight(65);

        var boxRecord = $"{nextRecId},{_config.LineNumber},{timestamp:yyyy-MM-dd HH:mm:ss},{stackNum},{plcMsg},,1,{barcodeSerial}";

        AppendToFile(boxFile, boxRecord);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  PALLET QUERY PANEL
    // ═══════════════════════════════════════════════════════════════════════════
    private void BuildPalletQueryPanel()
    {
        const int inputX = 250;
        const int inputW = 240;
        const int labelX = 40;

        _palletQueryPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.MidnightBlue, Visible = false };

        Label MakeRL(string text, int y, int w = 90) => new Label
        {
            Text = text, Location = new Point(inputX - 5 - w, y), Size = new Size(w, 26),
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.White, Font = new Font("Segoe UI", 10f),
        };
        TextBox MakeRO(int x, int y, int w) => new TextBox
        {
            Location = new Point(x, y), Size = new Size(w, 28),
            ReadOnly = true, BackColor = Color.White,
            ForeColor = Color.Black, Font = new Font("Segoe UI", 10f),
        };

        _palletQueryPanel.Controls.Add(new Label
        {
            Text = $"User: {Environment.UserName}",
            Location = new Point(8, 8), AutoSize = true,
            Font = new Font("Segoe UI", 8f), ForeColor = Color.LightCyan,
        });
        var printerInfo = string.IsNullOrWhiteSpace(_settings.LabelOutputAddress)
            ? "(printer not configured)"
            : $"{_settings.ThermalPrinterType}  {_settings.LabelOutputAddress}";
        _palletQueryPanel.Controls.Add(new Label
        {
            Text = printerInfo,
            Location = new Point(580, 8), Size = new Size(532, 18),
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 8f), ForeColor = Color.LightCyan,
        });

        _palletQueryPanel.Controls.Add(new Label
        {
            Text = $" Print Pallet Label — Line {_config.LineNumber:00} ",
            Location = new Point(40, 40), AutoSize = true,
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = Color.LightYellow, BackColor = Color.DarkSlateBlue,
            Padding = new Padding(8, 4, 8, 4),
        });

        int nextY = 100;

        _palletQueryPanel.Controls.Add(MakeRL("Carton Label Serial No:", nextY + 6, 160));
        _serialNoInput = new TextBox
        {
            Location = new Point(inputX, nextY), Size = new Size(320, 34),
            Font = new Font("Segoe UI", 13f), MaxLength = 50,
            BackColor = Color.White, ForeColor = Color.Black,
        };
        _serialNoInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Return)
            {
                e.SuppressKeyPress = true;
                _ = LookupCartonSerialAsync();
            }
        };
        _serialNoInput.Leave += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_serialNoInput.Text))
                _ = LookupCartonSerialAsync();
        };
        _palletQueryPanel.Controls.Add(_serialNoInput);
        nextY += 58;

        _palletQueryPanel.Controls.Add(MakeRL("Item Number:", nextY + 5, 105));
        _palletItemNoDisplay = MakeRO(inputX, nextY, 200);
        _palletQueryPanel.Controls.Add(_palletItemNoDisplay);
        nextY += 46;

        _palletQueryPanel.Controls.Add(MakeRL("Description:", nextY + 5, 105));
        _palletDescLabel = new Label
        {
            Location = new Point(inputX, nextY + 4), Size = new Size(550, 22),
            ForeColor = Color.LightCyan, Font = new Font("Segoe UI", 9f),
            AutoSize = false, TextAlign = ContentAlignment.MiddleLeft,
        };
        _palletQueryPanel.Controls.Add(_palletDescLabel);
        nextY += 46;

        _palletQueryPanel.Controls.Add(MakeRL("Shop Order:", nextY + 5));
        _palletShopOrderDisplay = MakeRO(inputX, nextY, inputW);
        _palletQueryPanel.Controls.Add(_palletShopOrderDisplay);
        nextY += 46;

        _palletQueryPanel.Controls.Add(MakeLabel("LIS Qty:", new Point(labelX, nextY + 5), 75));
        _palletLisQtyDisplay = MakeRO(labelX + 80, nextY, 65);
        _palletQueryPanel.Controls.Add(_palletLisQtyDisplay);
        _palletQueryPanel.Controls.Add(MakeLabel("Carton Qty:", new Point(labelX + 158, nextY + 5), 90));
        _palletCartonQtyDisplay = MakeRO(labelX + 253, nextY, 85);
        _palletQueryPanel.Controls.Add(_palletCartonQtyDisplay);
        _palletQueryPanel.Controls.Add(MakeLabel("Pallet Qty:", new Point(labelX + 352, nextY + 5), 85));
        _palletPalletQtyDisplay = MakeRO(labelX + 442, nextY, 60);
        _palletQueryPanel.Controls.Add(_palletPalletQtyDisplay);
        nextY += 44;

        _palletQueryPanel.Controls.Add(MakeRL("Shade:", nextY + 5));
        _palletShadeDisplay = MakeRO(inputX, nextY, 100);
        _palletQueryPanel.Controls.Add(_palletShadeDisplay);
        _palletQueryPanel.Controls.Add(MakeLabel("Caliber:", new Point(inputX + 116, nextY + 4), 80));
        _palletCaliberDisplay = MakeRO(inputX + 200, nextY, 80);
        _palletQueryPanel.Controls.Add(_palletCaliberDisplay);
        nextY += 44;

        _palletQueryPanel.Controls.Add(MakeRL("Shift:", nextY + 5));
        _palletShiftDisplay = MakeRO(inputX, nextY, 48);
        _palletQueryPanel.Controls.Add(_palletShiftDisplay);
        _palletQueryPanel.Controls.Add(MakeLabel("Line:", new Point(inputX + 64, nextY + 4), 50));
        _palletLineDisplay = MakeRO(inputX + 118, nextY, 50);
        _palletLineDisplay.Text = _config.LineNumber.ToString("00");
        _palletQueryPanel.Controls.Add(_palletLineDisplay);
        nextY += 50;

        _palletStatusLabel = new Label
        {
            Location = new Point(inputX, nextY), Size = new Size(640, 22),
            ForeColor = Color.LightGreen, Font = new Font("Segoe UI", 9f), AutoSize = false,
        };
        _palletQueryPanel.Controls.Add(_palletStatusLabel);
        nextY += 38;

        _btnPalletPrint = new Button
        {
            Text = "Print Pallet Label",
            Size = new Size(200, 44), Location = new Point(inputX, nextY),
            BackColor = Color.DarkSlateBlue, ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            Enabled = false,
        };
        _btnPalletPrint.Click += (_, _) => _ = PrintPalletLabelAsync();
        _palletQueryPanel.Controls.Add(_btnPalletPrint);

        var btnBack = new Button
        {
            Text = "Back",
            Size = new Size(120, 44), Location = new Point(inputX + 210, nextY),
            BackColor = Color.DarkRed, ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 11f),
        };
        btnBack.Click += (_, _) =>
        {
            _palletQueryPanel!.Visible = false;
            _startupPanel.Visible = true;
            ClientSize = new Size(1120, 720);
        };
        _palletQueryPanel.Controls.Add(btnBack);

        Controls.Add(_palletQueryPanel);
    }

    private void OpenPalletScanForm()
    {
        var form = new PalletScanForm(_settings, _config, _boxRepo, _shift, _inspector);
        form.Show(this);
    }

    private void OpenEolScanForm()
    {
        var form = new EolScanForm(_settings, _config, _boxRepo, _shift, _inspector);
        form.Show(this);
    }

    private void OpenStackerMaintenanceForm()
    {
        var form = new StackerMaintenanceForm(_boxRepo, _config.LineNumber, _config.DoesMexico, _settings, _config.DoesTwoPrims);
        form.Show(this);
    }

    private void ShowPalletQuery()
    {
        var inspector = _inspectorInput.Text.Trim();
        if (inspector.Length == 0)
        {
            MessageBox.Show("Inspector must be entered before printing.", "Input Error",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _inspectorInput.Focus();
            return;
        }
        if (_labelSizeCombo.SelectedItem is not LabelSizeOption)
        {
            MessageBox.Show("Please select a valid label size.", "Input Error",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _labelSizeCombo.Focus();
            return;
        }

        if (_palletQueryPanel == null)
            BuildPalletQueryPanel();

        _palletQueryItemDetail = null;
        _palletQueryCartonBarcode = string.Empty;
        _serialNoInput!.Clear();
        _palletItemNoDisplay!.Text = string.Empty;
        if (_palletDescLabel != null) _palletDescLabel.Text = string.Empty;
        _palletShopOrderDisplay!.Text = string.Empty;
        _palletLisQtyDisplay!.Text = string.Empty;
        _palletCartonQtyDisplay!.Text = string.Empty;
        _palletPalletQtyDisplay!.Text = string.Empty;
        _palletShadeDisplay!.Text = string.Empty;
        _palletCaliberDisplay!.Text = string.Empty;
        _palletShiftDisplay!.Text = _shiftInput!.Value.ToString();
        if (_palletStatusLabel != null) _palletStatusLabel.Text = string.Empty;
        _btnPalletPrint!.Enabled = false;

        _startupPanel.Visible = false;
        _palletQueryPanel!.Visible = true;
        ClientSize = new Size(1120, 560);
        _serialNoInput.Focus();
    }

    private async Task LookupCartonSerialAsync()
    {
        var serialNo = _serialNoInput?.Text.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(serialNo)) return;

        if (_palletStatusLabel != null)
        {
            _palletStatusLabel.Text = "Searching...";
            _palletStatusLabel.ForeColor = Color.LightCyan;
        }
        _btnPalletPrint!.Enabled = false;

        ItemDetail? itemDetail = null;
        string? shadeText = null;
        string statusFound = string.Empty;
        _palletQueryCartonBarcode = string.Empty;

        // 1. Try as a carton barcode — decode its encoded item reference directly, the same
        //    way Progress dtscn011.p does. Legacy never matches a scanned barcode back to a
        //    specific box log row (it has no per-carton uniqueness guarantee, by design —
        //    it's a scannable item/shade/size/date descriptor, not a unique identifier); it
        //    decodes the item straight from the barcode's own content. Matching that intent
        //    here means we never need "the one" boxes.csv row to exist or be unambiguous.
        var decodedIRef = ThermalPrinterCommandBuilder.TryDecodeCartonBarcodeIRef(serialNo);
        if (decodedIRef is int iref && iref > 0)
        {
            itemDetail = await _boxRepo.GetItemDetailByIRefAsync(iref, isMexicoItem: false, CancellationToken.None);
            if (itemDetail is null && _config.DoesMexico)
                itemDetail = await _boxRepo.GetItemDetailByIRefAsync(iref, isMexicoItem: true, CancellationToken.None);

            if (itemDetail != null)
            {
                statusFound = $"Carton barcode decoded — item {itemDetail.ItemNumber}";
                _palletQueryCartonBarcode = serialNo;
            }
        }

        // 2. Try as an item number (fallback: user typed item# directly).
        if (itemDetail == null)
        {
            itemDetail = await _boxRepo.GetItemDetailByNumberAsync(serialNo, searchMexicoAlso: true, CancellationToken.None);
            if (itemDetail != null)
                statusFound = $"Item found: {itemDetail.ItemNumber}";
        }

        // 3. Try as a stack number (fallback: user typed stack# directly).
        if (itemDetail == null)
        {
            var stacker = await _boxRepo.GetStackerAsync(_config.LineNumber, serialNo, CancellationToken.None);
            if (stacker != null && !string.IsNullOrWhiteSpace(stacker.ItemNumber))
            {
                itemDetail = await _boxRepo.GetItemDetailByNumberAsync(stacker.ItemNumber, _config.DoesMexico, CancellationToken.None);
                if (itemDetail != null)
                {
                    if (stacker.Shade > 0) shadeText = stacker.Shade.ToString();
                    statusFound = $"Stack found: {serialNo} → {itemDetail.ItemNumber}";
                }
            }
        }

        if (itemDetail != null)
        {
            _palletQueryItemDetail = itemDetail;
            if (_palletItemNoDisplay != null)    _palletItemNoDisplay.Text    = itemDetail.ItemNumber;
            if (_palletDescLabel != null)        _palletDescLabel.Text        = itemDetail.GetPrimaryItemDescription();
            if (_palletShopOrderDisplay != null) _palletShopOrderDisplay.Text = itemDetail.LastScheduleOrder;
            if (_palletLisQtyDisplay != null)    _palletLisQtyDisplay.Text    = itemDetail.LisQty.ToString();
            if (_palletCartonQtyDisplay != null)
                _palletCartonQtyDisplay.Text = itemDetail.SalesQty == 0
                    ? string.Empty
                    : $"{itemDetail.SalesQty:G} {itemDetail.SalesUOM}".Trim();
            if (_palletPalletQtyDisplay != null) _palletPalletQtyDisplay.Text = itemDetail.BoxesPerPallet.ToString();
            if (_palletShadeDisplay != null)     _palletShadeDisplay.Text     = shadeText ?? itemDetail.Shade.ToString();
            if (_palletCaliberDisplay != null)   _palletCaliberDisplay.Text   = string.Empty;
            if (_palletShiftDisplay != null)     _palletShiftDisplay.Text     = _shiftInput!.Value.ToString();

            if (_palletStatusLabel != null)
            {
                _palletStatusLabel.Text = statusFound;
                _palletStatusLabel.ForeColor = Color.LightGreen;
            }
            _btnPalletPrint!.Enabled = true;
        }
        else
        {
            _palletQueryItemDetail = null;
            if (_palletItemNoDisplay != null)    _palletItemNoDisplay.Text    = string.Empty;
            if (_palletDescLabel != null)        _palletDescLabel.Text        = string.Empty;
            if (_palletShopOrderDisplay != null) _palletShopOrderDisplay.Text = string.Empty;
            if (_palletLisQtyDisplay != null)    _palletLisQtyDisplay.Text    = string.Empty;
            if (_palletCartonQtyDisplay != null) _palletCartonQtyDisplay.Text = string.Empty;
            if (_palletPalletQtyDisplay != null) _palletPalletQtyDisplay.Text = string.Empty;
            if (_palletShadeDisplay != null)     _palletShadeDisplay.Text     = string.Empty;
            if (_palletCaliberDisplay != null)   _palletCaliberDisplay.Text   = string.Empty;

            if (_palletStatusLabel != null)
            {
                _palletStatusLabel.Text = $"'{serialNo}' not found as barcode serial, item number, or stack number";
                _palletStatusLabel.ForeColor = Color.Salmon;
            }
        }
    }

    private async Task PrintPalletLabelAsync()
    {
        var itemDetail = _palletQueryItemDetail;
        if (itemDetail == null) return;

        if (string.IsNullOrWhiteSpace(_settings.LabelOutputAddress))
        {
            MessageBox.Show("Label Output Address is not configured. Go to Settings.",
                "Cannot Print", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var inspector = _inspectorInput.Text.Trim();
        var labelSize = (_labelSizeCombo.SelectedItem as LabelSizeOption)?.SizeCode ?? string.Empty;
        var shade = _palletShadeDisplay?.Text ?? itemDetail.Shade.ToString();

        // Allocate the next pallet serial for this plant — format "PPP-SSSSSSSSS" matching Progress prt-tag-nbr
        var palletSerial = await _boxRepo.AllocatePalletSerialAsync(itemDetail.Plant, CancellationToken.None);
        var palletId = $"{itemDetail.Plant:000}-{palletSerial:000000000}";

        var job = new ManualCartonPrintJob(
            itemDetail.ItemNumber,
            Quantity: 1,
            inspector,
            Shift: (int)_shiftInput!.Value,
            labelSize,
            itemDetail.GetPrimaryItemDescription(),
            Environment.UserName,
            DateTime.Now,
            ShopOrder: itemDetail.LastScheduleOrder,
            ShadeOverride: shade,
            Caliber: string.Empty,
            LabelFormat: "PALLET_LABEL");

        _btnPalletPrint!.Enabled = false;
        var (success, _) = await ExportManualLabelAsync(itemDetail, job, CancellationToken.None,
            palletId: palletId, cartonReferenceBarcode: _palletQueryCartonBarcode);
        _btnPalletPrint.Enabled = true;

        if (success)
        {
            await _boxRepo.SavePalletRecordAsync(new PalletRecord
            {
                PalletId       = palletId,
                Plant          = itemDetail.Plant,
                ItemNumber     = itemDetail.ItemNumber,
                ColorDesc      = itemDetail.ColorDesc,
                ShapeDesc      = itemDetail.ShapeDesc,
                SeriesDesc     = itemDetail.SeriesDesc,
                LisQty         = itemDetail.LisQty,
                BoxesPerPallet = itemDetail.BoxesPerPallet,
                Shade          = shade,
                Size           = itemDetail.SizeShape,
                ShopOrder      = itemDetail.LastScheduleOrder,
                Grade          = itemDetail.Grade,
                LineNumber     = _config.LineNumber,
                Shift          = (int)_shiftInput!.Value,
                Inspector      = inspector,
                PrintedAtUtc   = DateTime.UtcNow,
            }, CancellationToken.None);
        }

        if (_palletStatusLabel != null)
        {
            _palletStatusLabel.Text = success
                ? $"Pallet label sent for {itemDetail.ItemNumber}  ·  {DateTime.Now:HH:mm:ss}"
                : "Failed to send pallet label.";
            _palletStatusLabel.ForeColor = success ? Color.LightGreen : Color.Salmon;
        }
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

        var plcLabel = new Label
        {
            Text = "PLC Input (Live)",
            Location = new Point(820, 540), Size = new Size(160, 20),
            ForeColor = Color.LightGreen, Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _browsePanel.Controls.Add(plcLabel);

        _plcInputList = new ListBox
        {
            Location = new Point(820, 560),
            Size = new Size(160, 80),
            Font = new Font("Consolas", 8.5f),
            BackColor = Color.Black,
            ForeColor = Color.LightGreen,
            BorderStyle = BorderStyle.FixedSingle,
            DrawMode = DrawMode.OwnerDrawFixed,
            SelectionMode = SelectionMode.None,
        };
        _plcInputList.DrawItem += PlcInputList_DrawItem;
        _browsePanel.Controls.Add(_plcInputList);
        plcLabel.BringToFront();
        _plcInputList.BringToFront();

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
                    if (stacker is not null && !string.IsNullOrWhiteSpace(stacker.ItemNumber))
                    {
                        var itemDetail = await _boxRepo.GetItemDetailByNumberAsync(
                            stacker.ItemNumber, _config.DoesMexico, CancellationToken.None);
                        if (itemDetail is not null)
                        {
                            // Progress: w-4digitshade = shade as 4 digits; else shade * 10 as 4 digits
                            var shadeStr = _config.W4DigitShade
                                ? stacker.Shade.ToString("0000")
                                : (stacker.Shade * 10).ToString("0000");
                            row.PartOrError = $"{itemDetail.ItemNumber.TrimEnd()}-{itemDetail.LisQty}-{shadeStr}-{stacker.Size}";
                        }
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
            _primaryItemDetail = null;
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
                // Validate right away — as soon as the item resolves — rather than waiting
                // until Print is pressed, so the operator finds out immediately instead of
                // after filling out the rest of the form.
                if (itemDetail.OpenQty <= 0)
                {
                    _primaryItemDetail = null;
                    _primaryItemDescLabel.Text = $"⚠ Item '{itemNumber}' has no open quantity on schedule.";
                    _primaryItemDescLabel.ForeColor = Color.Salmon;
                    MessageBox.Show(
                        $"Item '{itemNumber}' has no open quantity on schedule.\nCannot proceed until open quantity is available.",
                        "Cannot Proceed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!DateTime.TryParse(itemDetail.ScheduleDate, out var schedDate) || schedDate.Date < DateTime.Today)
                {
                    var dateDisplay = string.IsNullOrEmpty(itemDetail.ScheduleDate) ? "(not set)" : itemDetail.ScheduleDate;
                    _primaryItemDetail = null;
                    _primaryItemDescLabel.Text = $"⚠ Item '{itemNumber}' schedule date ({dateDisplay}) must be today or a future date.";
                    _primaryItemDescLabel.ForeColor = Color.Salmon;
                    MessageBox.Show(
                        $"Item '{itemNumber}' schedule date ({dateDisplay}) must be today or a future date.\nCannot proceed until schedule date criteria is met.",
                        "Cannot Proceed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _primaryItemDetail = itemDetail;
                // Display format: "ColorDesc | ShapeDesc | SeriesDesc"
                var description = itemDetail.GetPrimaryItemDescription();
                _primaryItemDescLabel.Text = description;
                _primaryItemDescLabel.ForeColor = Color.LightGreen;

                // Update read-only item info displays (always refresh on lookup)
                if (_lisQtyDisplay != null)
                    _lisQtyDisplay.Text = itemDetail.LisQty.ToString();
                if (_cartonQtyDisplay != null)
                    _cartonQtyDisplay.Text = itemDetail.SalesQty == 0
                        ? string.Empty
                        : $"{itemDetail.SalesQty:G} {itemDetail.SalesUOM}".Trim();
                if (_palletQtyDisplay != null)
                    _palletQtyDisplay.Text = itemDetail.BoxesPerPallet.ToString();

                // Pre-fill editable fields from item master if empty
                if (_shadeManualInput != null && string.IsNullOrEmpty(_shadeManualInput.Text))
                    _shadeManualInput.Text = itemDetail.Shade.ToString();
                if (_shopOrderInput != null && string.IsNullOrEmpty(_shopOrderInput.Text))
                    _shopOrderInput.Text = itemDetail.LastScheduleOrder;
            }
            else
            {
                _primaryItemDetail = null;
                _primaryItemDescLabel.Text = $"⚠ Item '{itemNumber}' not found in item master";
                _primaryItemDescLabel.ForeColor = Color.Salmon;
                if (_lisQtyDisplay != null) _lisQtyDisplay.Text = string.Empty;
                if (_cartonQtyDisplay != null) _cartonQtyDisplay.Text = string.Empty;
                if (_palletQtyDisplay != null) _palletQtyDisplay.Text = string.Empty;
            }
        }
        catch (Exception ex)
        {
            _primaryItemDetail = null;
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
                    ItemDetail? itemDetail = null;
                    if (stacker is not null && !string.IsNullOrWhiteSpace(stacker.ItemNumber))
                    {
                        itemDetail = await _boxRepo.GetItemDetailByNumberAsync(
                            stacker.ItemNumber, _config.DoesMexico, CancellationToken.None);
                    }

                    // ItemDisplay is the "PartOrError" field shown in the browse grid.
                    await ExportLabelAsync(box, stacker, itemDetail, row.PartOrError, CancellationToken.None);
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
        // Progress: find stacker where st-errmsg begins "*OK" or st-errmsg eq "" — that error-flag
        // concept doesn't exist in this format; just confirm the stacker is configured at all.
        var stacker = await _boxRepo.GetStackerAsync(_config.LineNumber, stackNum, CancellationToken.None);
        if (stacker is null || string.IsNullOrWhiteSpace(stacker.ItemNumber))
        {
            MessageBox.Show(
                $"Stack {stackNum} SETUP: not configured in Stacker Maintenance.",
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
    /// Exports label data using the selected output type.
    /// </summary>
    private async Task ExportLabelAsync(BoxRecord box, StackerRecord? stacker, ItemDetail? itemDetail, string itemDisplay, CancellationToken ct)
    {
        try
        {
            var outputType = _settings.LabelOutputType?.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);

            if (string.Equals(outputType, "NiceLabelXml", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(_settings.LabelOutputAddress))
                {
                    SetStatus("Warning: NiceLabel Xml selected but Output Address is empty.");
                    return;
                }

                var xmlExporter = new NiceLabelXmlExporter(_settings.LabelOutputAddress);
                var xmlSuccess = await xmlExporter.ExportAsync(box, stacker, itemDisplay, ct);

                if (xmlSuccess)
                    SetStatus($"Label exported to: {_settings.LabelOutputAddress}");
                else
                    SetStatus($"Warning: Failed to export label to {_settings.LabelOutputAddress}");
                return;
            }

            if (string.Equals(outputType, "NetworkPrinter", StringComparison.OrdinalIgnoreCase))
            {
                var payload = BuildThermalPayload(
                    labelFormat: ResolveThermalLabelFormat(itemDetail, stacker?.Size ?? _labelSize),
                    labelTypeCode: itemDetail?.LabelTypeCode ?? 0,
                    palletId: string.Empty,
                    itemNumber: itemDetail?.ItemNumber ?? itemDisplay,
                    iRef: itemDetail?.IRef ?? 0,
                    plant: itemDetail?.Plant ?? 0,
                    partDescription: itemDetail?.GetPrimaryItemDescription() ?? itemDisplay,
                    colorDesc: itemDetail?.ColorDesc ?? string.Empty,
                    shapeDesc: itemDetail?.ShapeDesc ?? string.Empty,
                    seriesDesc: itemDetail?.SeriesDesc ?? string.Empty,
                    labelSize: _labelSize,
                    stackNumber: box.StackNum.Trim(),
                    shade: stacker?.Shade.ToString() ?? string.Empty,
                    size: stacker?.Size ?? _labelSize,
                    boxesPerPallet: itemDetail?.BoxesPerPallet ?? 0,
                    salesQty: itemDetail?.SalesQty ?? 0m,
                    salesUom: itemDetail?.SalesUOM ?? string.Empty,
                    packageWeight: itemDetail?.PkgWeight ?? 0m,
                    quantity: Math.Max(1, box.PrintNum),
                    uccBarcode: itemDetail?.GetUCC() ?? string.Empty,
                    cartonUpc: itemDetail?.GetCartonUPC() ?? string.Empty,
                    cartonUpcNumSys: itemDetail?.CartonUPC_NumSys.ToString("0") ?? string.Empty,
                    cartonUpcMfg: itemDetail?.CartonUPC_Mfg.ToString("00000") ?? string.Empty,
                    cartonUpcProd: itemDetail?.CartonUPC_Prod.ToString("00000") ?? string.Empty,
                    cartonUpcChkdgt: itemDetail?.CartonUPC_Chkdgt.ToString("0") ?? string.Empty,
                    lisQty: itemDetail?.LisQty ?? 0,
                    grade: itemDetail?.Grade ?? 0,
                    location: _settings.PalletLocation,
                    plantName: _settings.PlantName,
                    cartonBarcodeSerialOverride: box.BarcodeSerial,
                    physicalStackNumber: box.StackNum.Trim());

                var thermalExporter = new ThermalPrinterCommandExporter(
                    _settings.LabelOutputAddress,
                    _config.DataDirectory,
                    _settings.ThermalPrinterType);

                var thermalResult = await thermalExporter.ExportAsync(payload, ct);
                if (thermalResult.Success)
                    SetStatus($"{_settings.ThermalPrinterType} sent to '{thermalResult.DispatchTarget ?? "(no target)"}' and archived at {thermalResult.ArchivePath}");
                else
                    SetStatus($"Thermal export error: {thermalResult.ErrorMessage} (archive: {thermalResult.ArchivePath})");
                return;
            }

            SetStatus($"Output type '{_settings.LabelOutputType}' is not implemented for automatic export.");
        }
        catch (Exception ex)
        {
            SetStatus($"Export error: {ex.Message}");
        }
    }

    private async Task<(bool Success, string BarcodeSerial)> ExportManualLabelAsync(ItemDetail itemDetail, ManualCartonPrintJob job, CancellationToken ct, string palletId = "", string cartonReferenceBarcode = "")
    {
        var outputType = _settings.LabelOutputType?.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);

        if (string.Equals(outputType, "NiceLabelXml", StringComparison.OrdinalIgnoreCase))
        {
            var xmlExporter = new NiceLabelXmlExporter(_settings.LabelOutputAddress);
            return (await xmlExporter.ExportManualCartonAsync(job, ct), string.Empty);
        }

        if (string.Equals(outputType, "NetworkPrinter", StringComparison.OrdinalIgnoreCase))
        {
            var resolvedFormat = string.IsNullOrEmpty(job.LabelFormat)
                ? ResolveThermalLabelFormat(itemDetail, job.LabelSize)
                : job.LabelFormat;
            var resolvedShade = string.IsNullOrEmpty(job.ShadeOverride)
                ? itemDetail.Shade.ToString()
                : job.ShadeOverride;
            var resolvedStack = string.IsNullOrEmpty(job.ShopOrder) ? "MANUAL" : job.ShopOrder;

            var payload = BuildThermalPayload(
                labelFormat: resolvedFormat,
                labelTypeCode: itemDetail.LabelTypeCode,
                palletId: palletId,
                itemNumber: job.ItemNumber,
                iRef: itemDetail.IRef,
                plant: itemDetail.Plant,
                partDescription: job.PartDescription,
                colorDesc: itemDetail.ColorDesc,
                shapeDesc: itemDetail.ShapeDesc,
                seriesDesc: itemDetail.SeriesDesc,
                labelSize: job.LabelSize,
                stackNumber: resolvedStack,
                shade: resolvedShade,
                size: itemDetail.SizeShape,
                boxesPerPallet: itemDetail.BoxesPerPallet,
                salesQty: itemDetail.SalesQty,
                salesUom: itemDetail.SalesUOM,
                packageWeight: itemDetail.PkgWeight,
                quantity: job.Quantity,
                uccBarcode: itemDetail.GetUCC(),
                cartonUpc: itemDetail.GetCartonUPC(),
                cartonUpcNumSys: itemDetail.CartonUPC_NumSys.ToString("0"),
                cartonUpcMfg: itemDetail.CartonUPC_Mfg.ToString("00000"),
                cartonUpcProd: itemDetail.CartonUPC_Prod.ToString("00000"),
                cartonUpcChkdgt: itemDetail.CartonUPC_Chkdgt.ToString("0"),
                shopOrder: job.ShopOrder,
                caliber: job.Caliber,
                lisQty: itemDetail.LisQty,
                grade: itemDetail.Grade,
                location: _settings.PalletLocation,
                plantName: _settings.PlantName,
                cartonReferenceBarcode: cartonReferenceBarcode,
                userId: job.RequestedBy,
                printerTermId: DerivePrinterTermId(_settings.LabelOutputAddress),
                wmsUom: itemDetail.WmsUOM,
                physicalStackNumber: job.PhysicalStackNumber);

            // Compute up front so the returned serial is populated regardless of which
            // branch inside ExportAsync actually renders the label (e.g. non-SATO types).
            // Skip if a caller already supplied a known value (e.g. a reprint reusing the
            // box's originally-stored barcode) — recomputing from "now" would drift once
            // the reprint happens on a different calendar day than the original print.
            if (string.IsNullOrWhiteSpace(payload.CartonBarcodeSerial))
                payload.CartonBarcodeSerial = ThermalPrinterCommandBuilder.ComputeCartonBarcodeSerial(payload);

            var thermalExporter = new ThermalPrinterCommandExporter(
                _settings.LabelOutputAddress,
                _config.DataDirectory,
                _settings.ThermalPrinterType);

            var thermalResult = await thermalExporter.ExportAsync(payload, ct);
            if (thermalResult.Success)
            {
                SetStatus($"{_settings.ThermalPrinterType} sent to '{thermalResult.DispatchTarget ?? "(no target)"}' and archived at {thermalResult.ArchivePath}");
                return (true, payload.CartonBarcodeSerial);
            }

            SetStatus($"Thermal export error: {thermalResult.ErrorMessage} (archive: {thermalResult.ArchivePath})");
            return (false, payload.CartonBarcodeSerial);
        }

        SetStatus($"Output type '{_settings.LabelOutputType}' is not supported in manual mode.");
        return (false, string.Empty);
    }

    private ThermalLabelPayload BuildThermalPayload(
        string labelFormat,
        int labelTypeCode,
        string palletId,
        string itemNumber,
        int iRef,
        int plant,
        string partDescription,
        string colorDesc,
        string shapeDesc,
        string seriesDesc,
        string labelSize,
        string stackNumber,
        string shade,
        string size,
        int boxesPerPallet,
        decimal salesQty,
        string salesUom,
        decimal packageWeight,
        int quantity,
        string uccBarcode,
        string cartonUpc,
        string cartonUpcNumSys = "",
        string cartonUpcMfg = "",
        string cartonUpcProd = "",
        string cartonUpcChkdgt = "",
        string shopOrder = "",
        string caliber = "",
        int lisQty = 0,
        int grade = 0,
        string location = "",
        string plantName = "",
        string cartonReferenceBarcode = "",
        string userId = "",
        string printerTermId = "",
        string wmsUom = "",
        string cartonBarcodeSerialOverride = "",
        string physicalStackNumber = "")
    {
        return new ThermalLabelPayload
        {
            PhysicalStackNumber = physicalStackNumber,
            // Reuse an already-known barcode (e.g. a reprint's originally-stored value)
            // instead of letting it be recomputed from "now" — see CartonBarcodeSerial
            // guard in ThermalPrinterCommandExporter/ThermalPrinterCommandBuilder.
            CartonBarcodeSerial = cartonBarcodeSerialOverride,
            LabelFormat = labelFormat,
            LabelTypeCode = labelTypeCode,
            PalletId = palletId,
            ItemNumber = itemNumber,
            IRef = iRef,
            Plant = plant,
            PartDescription = partDescription,
            ColorDesc = colorDesc,
            ShapeDesc = shapeDesc,
            SeriesDesc = seriesDesc,
            LabelSize = labelSize,
            StackNumber = stackNumber,
            Shade = shade,
            Size = size,
            BoxesPerPallet = boxesPerPallet,
            SalesQty = salesQty,
            SalesUom = salesUom,
            PackageWeight = packageWeight,
            Inspector = _inspector,
            Shift = _shift,
            LineNumber = _config.LineNumber,
            Quantity = Math.Clamp(quantity, 1, 999),
            LisQty = lisQty,
            UccBarcode = uccBarcode,
            CartonUpc = cartonUpc,
            CartonUpcNumSys = cartonUpcNumSys,
            CartonUpcMfg = cartonUpcMfg,
            CartonUpcProd = cartonUpcProd,
            CartonUpcChkdgt = cartonUpcChkdgt,
            ShopOrder = shopOrder,
            Caliber = caliber,
            Grade = grade,
            Location = location,
            PlantName = plantName,
            CartonReferenceBarcode = cartonReferenceBarcode,
            UserId = userId,
            PrinterTermId = printerTermId,
            WmsUom = wmsUom,
            CreatedAtUtc = DateTime.UtcNow,
        };
    }

    /// <summary>Short terminal identifier for the pallet label footer (Progress w-trk-term), derived
    /// from the configured output address (analogous to trimming the unix tty/device path).</summary>
    private static string DerivePrinterTermId(string? outputAddress)
    {
        if (string.IsNullOrWhiteSpace(outputAddress))
            return string.Empty;

        var trimmed = outputAddress.Trim();
        return trimmed.Length <= 5 ? trimmed : trimmed[^5..];
    }

    private static string ResolveThermalLabelFormat(ItemDetail? itemDetail, string? labelSize)
    {
        if (LooksLikeSlabProcess(itemDetail, labelSize))
            return "SLAB_LABEL";

        return IsPalletLabelSize(labelSize)
            ? "PALLET_LABEL"
            : "CARTON_LABEL";
    }

    private static bool IsPalletLabelSize(string? labelSize)
    {
        if (string.IsNullOrWhiteSpace(labelSize))
            return false;

        var normalized = new string(labelSize
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());

        // Business rule provided by operations: pallet labels are 6x4.
        return normalized.Contains("6X4", StringComparison.Ordinal);
    }

    private static bool LooksLikeSlabProcess(ItemDetail? itemDetail, string? labelSize)
    {
        if (!string.IsNullOrWhiteSpace(labelSize) &&
            labelSize.Contains("SLAB", StringComparison.OrdinalIgnoreCase))
            return true;

        if (itemDetail is null)
            return false;

        return ContainsSlab(itemDetail.ProductType) ||
               ContainsSlab(itemDetail.TypeOfTile) ||
               ContainsSlab(itemDetail.SeriesDesc) ||
               ContainsSlab(itemDetail.LISDescription);
    }

    private static bool ContainsSlab(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Contains("SLAB", StringComparison.OrdinalIgnoreCase);
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

    private void StartPlcIpMonitorIfConfigured()
    {
        if (!string.Equals(_settings.PlcConnectionType, "IP", StringComparison.OrdinalIgnoreCase))
        {
            AddPlcInputLine("monitor disabled (SerialPort)");
            return;
        }

        if (!OmronNPortMonitorService.TryParseEndpoint(_settings.PlcAddress, out var host, out var port))
        {
            SetStatus($"PLC monitor skipped: invalid PLC address '{_settings.PlcAddress}'.");
            return;
        }

        var logPath = Path.Combine(_config.DataDirectory, "plc-ip-monitor.log");
        var service = new OmronNPortMonitorService(host, port, logPath);
        service.OnStatus = msg =>
        {
            if (!IsDisposed && IsHandleCreated)
            {
                try
                {
                    BeginInvoke(new Action(() => SetStatus(msg)));
                }
                catch
                {
                    // ignore UI shutdown race
                }
            }
        };
        service.OnFrame = frame =>
        {
            if (!IsDisposed && IsHandleCreated)
            {
                try
                {
                    BeginInvoke(new Action(() => AddPlcInputFrame(frame)));
                }
                catch
                {
                    // ignore UI shutdown race
                }
            }
        };

        _plcMonitorCts = new CancellationTokenSource();
        _plcMonitorTask = Task.Run(() => service.RunAsync(_plcMonitorCts.Token));
        AddPlcInputLine($"monitor start {host}:{port}");
    }

    private void StopPlcIpMonitor()
    {
        try
        {
            _plcMonitorCts?.Cancel();
            AddPlcInputLine("monitor stop");
        }
        catch
        {
            // best-effort shutdown
        }
    }

    private void AddPlcInputFrame(PlcDecodedFrame frame)
    {
        var decoded = frame.DecodedValue;
        var line = $"{frame.Timestamp:HH:mm:ss}  {decoded,-8}  {frame.Signature}";
        AddPlcInputLine(line);

        // Every decoded numeric value is a "this stacker just produced a carton" signal —
        // there is no separate/special print-trigger code. Which item/shade/size to print is
        // resolved from the Stacker Maintenance table for that stack number, not a single
        // global "Current Primary Item" (that setting is only used by the manual print flows).
        if (int.TryParse(decoded, out var stackNumber))
            _ = HandleStackerCartonEventAsync(stackNumber, frame.Timestamp);
    }

    /// <summary>
    /// Handles one "stacker N produced a carton" PLC event end-to-end: resolves the stacker's
    /// assigned item/shade/size (Stacker Maintenance), records the box, and prints the carton
    /// label — sequentially, so the recorded barcode and the printed barcode can never drift
    /// apart the way two independent fire-and-forget handlers could.
    /// </summary>
    private async Task HandleStackerCartonEventAsync(int stackNumber, DateTime frameTimestamp)
    {
        if (!_browsePanel.Visible)
            return;

        var nowUtc = DateTime.UtcNow;
        if (_lastStackerFireUtc.TryGetValue(stackNumber, out var last) && nowUtc - last < TimeSpan.FromMilliseconds(700))
            return;
        if (!_stackerHandlingInProgress.Add(stackNumber))
            return;
        _lastStackerFireUtc[stackNumber] = nowUtc;

        try
        {
            var stackNumStr = stackNumber.ToString();
            var stacker = await _boxRepo.GetStackerAsync(_config.LineNumber, stackNumStr, CancellationToken.None);
            if (stacker is null || string.IsNullOrWhiteSpace(stacker.ItemNumber))
            {
                SetStatus($"Stacker {stackNumStr}: not configured — set it up in Stacker Maintenance.");
                return;
            }

            var itemDetail = await _boxRepo.GetItemDetailByNumberAsync(stacker.ItemNumber, _config.DoesMexico, CancellationToken.None);
            if (itemDetail is null)
            {
                SetStatus($"Stacker {stackNumStr}: item '{stacker.ItemNumber}' not found in item master.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_labelSize))
            {
                SetStatus("PLC signal received before startup completion (label size missing).");
                return;
            }

            // Shade/Size come from the STACKER's assignment, not the item's own defaults —
            // the same item can run in different shades/sizes on different physical stackers.
            var sizeCode = string.IsNullOrWhiteSpace(stacker.Size) ? "0" : stacker.Size.Trim()[..1];
            var barcodeSerial = ThermalPrinterCommandBuilder.ComputeCartonBarcodeSerial(
                frameTimestamp, itemDetail.IRef, stacker.Shade, sizeCode, _shift, _config.LineNumber, itemDetail.Plant, itemDetail.LisQty);
            AppendCartonBoxRecord(itemDetail, stackNumber, barcodeSerial, frameTimestamp);
            await RefreshBrowseAsync();

            var job = new ManualCartonPrintJob(
                itemDetail.ItemNumber,
                1,
                _inspector,
                _shift,
                _labelSize,
                itemDetail.GetPrimaryItemDescription(),
                Environment.UserName,
                DateTime.Now,
                ShadeOverride: stacker.Shade.ToString(),
                Caliber: stacker.Size,
                PhysicalStackNumber: stackNumStr);

            var (success, _) = await ExportManualLabelAsync(itemDetail, job, CancellationToken.None);
            SetStatus(success
                ? $"Stacker {stackNumStr} auto-print sent for {itemDetail.ItemNumber}."
                : $"Stacker {stackNumStr} auto-print failed for {itemDetail.ItemNumber}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Stacker auto-print error: {ex.Message}");
        }
        finally
        {
            _stackerHandlingInProgress.Remove(stackNumber);
        }
    }

    private void AddPlcInputLine(string line)
    {
        if (_plcInputList is null || _plcInputList.IsDisposed)
            return;

        _plcInputList.Items.Insert(0, line);
        while (_plcInputList.Items.Count > 6)
            _plcInputList.Items.RemoveAt(_plcInputList.Items.Count - 1);
    }

    private void PlcInputList_DrawItem(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground();

        if (e.Index < 0 || e.Index >= _plcInputList.Items.Count)
            return;

        var text = _plcInputList.Items[e.Index]?.ToString() ?? string.Empty;
        var color = text.Contains("unknown", StringComparison.OrdinalIgnoreCase)
            ? Color.OrangeRed
            : Color.LightGreen;

        using var brush = new SolidBrush(color);
        e.Graphics.DrawString(text, e.Font, brush, e.Bounds);
        e.DrawFocusRectangle();
    }

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
