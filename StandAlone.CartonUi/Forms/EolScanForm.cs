using StandAlone.CartonUi.Models;
using StandAlone.CartonUi.Services;
using StandAlone.Integration.Services;
using System.ComponentModel;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;

namespace StandAlone.CartonUi.Forms;

/// <summary>
/// End-of-line scanning screen: operator scans (or types) a pallet serial as it leaves
/// the line. The pallet's info is looked up from the local registry (written when the
/// pallet label was printed — see <see cref="IBoxRepository.SavePalletRecordAsync"/>),
/// logged as a production-confirmation transaction, and sent to SAP for backflush.
/// Input sources mirror <see cref="PalletScanForm"/> (determined by Settings → PlcConnectionType):
///   SerialPort — listens on the configured COM port for a pallet serial.
///   IP         — listens on a TCP port (Settings → PlcPort) for the same.
/// In both modes the operator can also type or paste a pallet serial and press Enter.
/// </summary>
public class EolScanForm : Form
{
    private readonly AppSettings     _settings;
    private readonly CartonAppConfig _config;
    private readonly IBoxRepository  _boxRepo;
    private readonly int             _shift;
    private readonly string          _inspector;

    private readonly BindingList<EolScanRecord> _history = new();

    // UI
    private Label         _statusLabel  = null!;
    private Label         _serialLabel  = null!;
    private Label         _itemLabel    = null!;
    private Label         _descLabel    = null!;
    private Label         _infoLabel    = null!;
    private DataGridView  _historyGrid  = null!;
    private TextBox       _manualEntry  = null!;

    // Serial
    private SerialPort?               _port;
    // TCP
    private TcpListener?              _tcpListener;
    private CancellationTokenSource?  _cts;
    private volatile bool             _processing;

    public EolScanForm(AppSettings settings, CartonAppConfig config, IBoxRepository boxRepo,
        int shift, string inspector)
    {
        _settings  = settings;
        _config    = config;
        _boxRepo   = boxRepo;
        _shift     = shift;
        _inspector = inspector;

        Text             = $"EOL Scan — Line {config.LineNumber:00}";
        WindowState      = FormWindowState.Maximized;
        FormBorderStyle  = FormBorderStyle.Sizable;
        BackColor        = Color.MidnightBlue;
        ForeColor        = Color.White;
        StartPosition    = FormStartPosition.CenterScreen;
        KeyPreview       = true;
        KeyDown         += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        BuildUI();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  UI
    // ─────────────────────────────────────────────────────────────────────────
    private void BuildUI()
    {
        // ── Header bar ───────────────────────────────────────────────────────
        var header = new Panel
        {
            Dock = DockStyle.Top, Height = 52, BackColor = Color.DarkSlateBlue,
        };
        header.Controls.Add(new Label
        {
            Text      = $"EOL SCAN   Line {_config.LineNumber:00}   Inspector: {_inspector}   Shift: {_shift}",
            Dock      = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font      = new Font("Segoe UI", 15f, FontStyle.Bold),
            ForeColor = Color.LightYellow,
        });
        var btnBack = new Button
        {
            Text      = "← Back  (Esc)",
            Size      = new Size(140, 38),
            Dock      = DockStyle.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.DarkRed,
            ForeColor = Color.White,
            Font      = new Font("Segoe UI", 10f),
        };
        btnBack.Click += (_, _) => Close();
        header.Controls.Add(btnBack);
        Controls.Add(header);

        // ── Manual-entry bar ─────────────────────────────────────────────────
        var manualBar = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 50,
            BackColor = Color.FromArgb(30, 30, 70),
            Padding   = new Padding(12, 8, 12, 8),
        };
        var entryLabel = new Label
        {
            Text      = "Pallet Serial:",
            Font      = new Font("Segoe UI", 10f),
            ForeColor = Color.LightGray,
            AutoSize  = true,
            Location  = new Point(12, 14),
        };
        _manualEntry = new TextBox
        {
            Font      = new Font("Courier New", 11f),
            Width     = 320,
            Location  = new Point(130, 11),
            MaxLength = 50,
            BackColor = Color.DarkSlateGray,
            ForeColor = Color.LightGreen,
        };
        _manualEntry.KeyDown += ManualEntry_KeyDown;

        var btnConfirm = new Button
        {
            Text      = "Confirm (Enter)",
            Size      = new Size(130, 28),
            Location  = new Point(460, 11),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.DarkSlateBlue,
            ForeColor = Color.White,
            Font      = new Font("Segoe UI", 9f),
        };
        btnConfirm.Click += async (_, _) => await SubmitManualEntry();

        manualBar.Controls.Add(entryLabel);
        manualBar.Controls.Add(_manualEntry);
        manualBar.Controls.Add(btnConfirm);
        Controls.Add(manualBar);

        // ── Content ───────────────────────────────────────────────────────────
        var body = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 2,
            RowCount    = 7,
            BackColor   = Color.MidnightBlue,
            Padding     = new Padding(40, 20, 40, 20),
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;

        _statusLabel = new Label
        {
            Text      = "Initializing...",
            Font      = new Font("Segoe UI", 14f, FontStyle.Bold),
            ForeColor = Color.LightCyan,
            Dock      = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Height    = 44,
        };
        body.Controls.Add(_statusLabel, 0, row);
        body.SetColumnSpan(_statusLabel, 2);
        row++;

        body.Controls.Add(FieldLabel("Last scan:"), 0, row);
        _serialLabel = FieldValue(string.Empty, "Courier New", 13f, Color.LightGreen);
        body.Controls.Add(_serialLabel, 1, row);
        row++;

        body.Controls.Add(FieldLabel("Item:"), 0, row);
        _itemLabel = FieldValue(string.Empty, "Segoe UI", 13f, Color.White, bold: true);
        body.Controls.Add(_itemLabel, 1, row);
        row++;

        body.Controls.Add(FieldLabel("Desc:"), 0, row);
        _descLabel = FieldValue(string.Empty);
        body.Controls.Add(_descLabel, 1, row);
        row++;

        body.Controls.Add(FieldLabel("Info:"), 0, row);
        _infoLabel = FieldValue(string.Empty);
        body.Controls.Add(_infoLabel, 1, row);
        row++;

        var histHeader = FieldLabel("Recent EOL scans:");
        body.Controls.Add(histHeader, 0, row);
        body.SetColumnSpan(histHeader, 2);
        row++;

        _historyGrid = new DataGridView
        {
            Dock                = DockStyle.Fill,
            AllowUserToAddRows  = false,
            ReadOnly            = true,
            SelectionMode       = DataGridViewSelectionMode.FullRowSelect,
            AutoGenerateColumns = false,
            BackgroundColor     = Color.LightSteelBlue,
            BorderStyle         = BorderStyle.FixedSingle,
            ForeColor           = Color.Black,
            MultiSelect         = false,
            Font                = new Font("Courier New", 9f),
            RowHeadersVisible   = false,
            ColumnHeadersHeight = 26,
        };
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Time",     DataPropertyName = "TimeDisplay",     Width = 100 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Pallet",    DataPropertyName = "PalletId",        Width = 140 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Item#",     DataPropertyName = "ItemNumber",      Width = 130 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Description", DataPropertyName = "Description",  Width = 340 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Shop Order",  DataPropertyName = "ShopOrder",    Width = 110 });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Qty",       DataPropertyName = "ConfirmedQty",   Width = 80  });
        _historyGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "SAP",       DataPropertyName = "SapStatusDisplay", Width = 80 });
        _historyGrid.DataSource = _history;

        body.Controls.Add(_historyGrid, 0, row);
        body.SetColumnSpan(_historyGrid, 2);
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Controls.Add(body);
    }

    private static Label FieldLabel(string text) => new()
    {
        Text      = text,
        Font      = new Font("Segoe UI", 10f),
        ForeColor = Color.LightGray,
        Dock      = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private static Label FieldValue(string text, string fontName = "Segoe UI", float size = 10.5f,
        Color? color = null, bool bold = false) => new()
    {
        Text      = text,
        Font      = new Font(fontName, size, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = color ?? Color.LightGray,
        Dock      = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    // ─────────────────────────────────────────────────────────────────────────
    //  Manual entry
    // ─────────────────────────────────────────────────────────────────────────
    private async void ManualEntry_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            await SubmitManualEntry();
        }
    }

    private async Task SubmitManualEntry()
    {
        var serial = _manualEntry.Text.Trim();
        if (string.IsNullOrEmpty(serial)) return;
        _manualEntry.Clear();
        await HandleScanAsync(serial);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Lifecycle
    // ─────────────────────────────────────────────────────────────────────────
    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        var recent = await _boxRepo.GetLastEolScansAsync(15, CancellationToken.None);
        foreach (var record in recent)
            _history.Add(record);

        _cts = new CancellationTokenSource();

        if (_settings.PlcConnectionType == "IP")
            StartTcpListener(_cts.Token);
        else
            StartSerialReader(_cts.Token);

        _manualEntry.Focus();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cts?.Cancel();
        try { _port?.Close(); } catch { }
        _port?.Dispose();
        try { _tcpListener?.Stop(); } catch { }
        base.OnFormClosing(e);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Serial reader
    // ─────────────────────────────────────────────────────────────────────────
    private void StartSerialReader(CancellationToken ct)
    {
        var portName = _settings.PlcAddress?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(portName))
        {
            SetStatus("⚠ No COM port configured — go to Settings → Address.", Color.Salmon);
            return;
        }

        try
        {
            _port = new SerialPort(portName, _settings.PlcBaudRate)
            {
                ReadTimeout = 2000,
                NewLine     = "\r",
            };
            _port.Open();
        }
        catch (Exception ex)
        {
            SetStatus($"⚠ Cannot open {portName}: {ex.Message}", Color.Salmon);
            return;
        }

        SetStatus($"Ready — scan a pallet serial on {portName} ({_settings.PlcBaudRate} baud)  or type it above.", Color.LightCyan);

        Task.Run(() =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var line = _port.ReadLine().Trim();
                    if (!string.IsNullOrWhiteSpace(line))
                        BeginInvoke(async () => await HandleScanAsync(line));
                }
                catch (TimeoutException) { /* normal idle */ }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        BeginInvoke(() => SetStatus($"⚠ Serial error: {ex.Message}", Color.Salmon));
                }
            }
        }, ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  TCP listener
    // ─────────────────────────────────────────────────────────────────────────
    private void StartTcpListener(CancellationToken ct)
    {
        var port = _settings.PlcPort;
        try
        {
            _tcpListener = new TcpListener(IPAddress.Any, port);
            _tcpListener.Start();
        }
        catch (Exception ex)
        {
            SetStatus($"⚠ Cannot start TCP listener on port {port}: {ex.Message}", Color.Salmon);
            return;
        }

        SetStatus($"Ready — listening for scanner on TCP port {port}  or type a pallet serial above.", Color.LightCyan);

        Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var client = await _tcpListener.AcceptTcpClientAsync(ct);
                    _ = Task.Run(() => HandleTcpClientAsync(client, ct), ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        BeginInvoke(() => SetStatus($"⚠ TCP error: {ex.Message}", Color.Salmon));
                }
            }
        }, ct);
    }

    private async Task HandleTcpClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        using (var reader = new StreamReader(client.GetStream()))
        {
            try
            {
                string? line;
                while (!ct.IsCancellationRequested &&
                       (line = await reader.ReadLineAsync(ct)) is not null)
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                        BeginInvoke(async () => await HandleScanAsync(trimmed));
                }
            }
            catch (OperationCanceledException) { }
            catch { /* client disconnected */ }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Scan processing  (always runs on the UI thread via BeginInvoke)
    // ─────────────────────────────────────────────────────────────────────────
    private async Task HandleScanAsync(string scanned)
    {
        if (_processing) return;
        _processing = true;
        try
        {
            await ProcessScanAsync(scanned);
        }
        finally
        {
            _processing = false;
        }
    }

    private async Task ProcessScanAsync(string scanned)
    {
        var palletId = NormalizePalletSerial(scanned);
        if (string.IsNullOrWhiteSpace(palletId))
        {
            SetStatus($"Ignored: '{scanned}' (not a recognizable pallet serial).", Color.Gray);
            return;
        }

        SetStatus("Looking up pallet...", Color.LightCyan);
        _serialLabel.Text = palletId;
        _itemLabel.Text   = string.Empty;
        _descLabel.Text   = string.Empty;
        _infoLabel.Text   = string.Empty;

        var pallet = await _boxRepo.GetPalletBySerialAsync(palletId, CancellationToken.None);
        if (pallet is null)
        {
            SetStatus($"⚠ Pallet '{palletId}' not found — was it printed on this system?", Color.Salmon);
            return;
        }

        _itemLabel.Text = pallet.ItemNumber;
        _descLabel.Text = pallet.Description;
        _infoLabel.Text = $"Qty: {pallet.TotalPieces}   Shop Order: {pallet.ShopOrder}   Plant: {pallet.Plant:000}   Printed: {pallet.TimeDisplay}";

        // Duplicate-scan guard: only block on a PRIOR scan that actually reached SAP — if the
        // last attempt failed to send, treat this scan as a retry rather than a duplicate.
        var previousScan = await _boxRepo.FindEolScanByPalletIdAsync(pallet.PalletId, CancellationToken.None);
        if (previousScan != null && previousScan.SapSuccess)
        {
            SetStatus($"⚠ Pallet '{pallet.PalletId}' was already scanned at {previousScan.TimeDisplay} by {previousScan.Inspector}.", Color.Salmon);
            return;
        }

        SetStatus("Confirming...", Color.Yellow);

        var scanRecord = new EolScanRecord
        {
            PalletId       = pallet.PalletId,
            Plant          = pallet.Plant,
            ItemNumber     = pallet.ItemNumber,
            Description    = pallet.Description,
            ShopOrder      = pallet.ShopOrder,
            LisQty         = pallet.LisQty,
            BoxesPerPallet = pallet.BoxesPerPallet,
            ConfirmedQty   = pallet.TotalPieces,
            Shift          = _shift,
            LineNumber     = _config.LineNumber,
            Inspector      = _inspector,
            ScanTimeUtc    = DateTime.UtcNow,
        };

        var sapResult = await SendToSapAsync(pallet, scanRecord.ScanTimeUtc, CancellationToken.None);
        scanRecord.SapSuccess = sapResult.Success;
        scanRecord.SapDetail  = sapResult.Success ? "OK" : (sapResult.ErrorMessage ?? "Unknown error");

        await _boxRepo.AppendEolScanAsync(scanRecord, CancellationToken.None);

        _history.Insert(0, scanRecord);
        while (_history.Count > 15)
            _history.RemoveAt(_history.Count - 1);

        SetStatus(sapResult.Success
                ? $"✓ Confirmed: {pallet.PalletId}   {pallet.ItemNumber}   sent to SAP"
                : $"⚠ Confirmed and logged, but SAP send failed: {sapResult.ErrorMessage}",
            sapResult.Success ? Color.LightGreen : Color.Salmon);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  SAP backflush integration
    // ─────────────────────────────────────────────────────────────────────────
    private async Task<SapIntegrationResult> SendToSapAsync(PalletRecord pallet, DateTime scanTimeUtc, CancellationToken ct)
    {
        var sapIntegration = new FileSapIntegrationService(Path.Combine(_config.DataDirectory, "sap-output"));

        var payload = new PalletIntegrationPayload
        {
            SerialNumber = pallet.PalletId,
            ItemNumber   = pallet.ItemNumber,
            Plant        = pallet.Plant,
            Source       = "EOL",
            ReceivedAt   = scanTimeUtc,
            ShopOrder    = pallet.ShopOrder,
            LineNumber   = _config.LineNumber,
            Shift        = _shift,
            ConfirmedQty = pallet.TotalPieces,
            Inspector    = _inspector,
            ConfirmedAt  = scanTimeUtc,
        };

        return await sapIntegration.SendPalletIntegrationAsync(payload, ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Accepts either the printed "PPP-SSSSSSSSS" tag text or the 12-digit no-dash
    /// barcode value actually encoded on the label (prt-pal-tag-bc), normalizing both
    /// to the dashed form used as the key in the pallet registry.
    /// </summary>
    private static string NormalizePalletSerial(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Contains('-'))
            return trimmed.ToUpperInvariant();

        return trimmed.Length == 12 && trimmed.All(char.IsDigit)
            ? $"{trimmed[..3]}-{trimmed[3..]}"
            : trimmed;
    }

    private void SetStatus(string text, Color color)
    {
        if (_statusLabel.IsDisposed) return;
        _statusLabel.Text      = text;
        _statusLabel.ForeColor = color;
    }
}
