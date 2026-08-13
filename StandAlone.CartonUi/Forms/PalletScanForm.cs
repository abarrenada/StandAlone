using StandAlone.CartonUi.Models;
using StandAlone.CartonUi.Services;
using StandAlone.Integration.Services;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;

namespace StandAlone.CartonUi.Forms;

/// <summary>
/// Full-screen pallet-label printing screen.
/// Input sources (determined by Settings → PlcConnectionType):
///   SerialPort — listens on the configured COM port for 30-char carton barcodes.
///   IP         — listens on a TCP port (Settings → PlcPort, default 9000) for the same barcodes.
/// In both modes the operator can also type or paste a barcode into the manual-entry bar and press Enter.
/// </summary>
public class PalletScanForm : Form
{
    private readonly AppSettings     _settings;
    private readonly CartonAppConfig _config;
    private readonly IBoxRepository  _boxRepo;
    private readonly int             _shift;
    private readonly string          _inspector;

    // UI
    private Label   _statusLabel  = null!;
    private Label   _barcodeLabel = null!;
    private Label   _itemLabel    = null!;
    private Label   _descLabel    = null!;
    private Label   _infoLabel    = null!;
    private ListBox _historyList  = null!;
    private TextBox _manualEntry  = null!;

    // Serial
    private SerialPort?               _port;
    // TCP
    private TcpListener?              _tcpListener;
    private CancellationTokenSource?  _cts;
    private volatile bool             _processing;

    public PalletScanForm(AppSettings settings, CartonAppConfig config, IBoxRepository boxRepo,
        int shift, string inspector)
    {
        _settings  = settings;
        _config    = config;
        _boxRepo   = boxRepo;
        _shift     = shift;
        _inspector = inspector;

        Text             = $"Pallet Label — Line {config.LineNumber:00}";
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
            Text      = $"PALLET LABEL   Line {_config.LineNumber:00}   Inspector: {_inspector}   Shift: {_shift}",
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
            Text      = "Carton barcode:",
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

        var btnPrint = new Button
        {
            Text      = "Print (Enter)",
            Size      = new Size(120, 28),
            Location  = new Point(460, 11),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.DarkSlateBlue,
            ForeColor = Color.White,
            Font      = new Font("Segoe UI", 9f),
        };
        btnPrint.Click += async (_, _) => await SubmitManualEntry();

        manualBar.Controls.Add(entryLabel);
        manualBar.Controls.Add(_manualEntry);
        manualBar.Controls.Add(btnPrint);
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
        _barcodeLabel = FieldValue(string.Empty, "Courier New", 13f, Color.LightGreen);
        body.Controls.Add(_barcodeLabel, 1, row);
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

        var histHeader = FieldLabel("Recent prints:");
        body.Controls.Add(histHeader, 0, row);
        body.SetColumnSpan(histHeader, 2);
        row++;

        _historyList = new ListBox
        {
            Dock        = DockStyle.Fill,
            Font        = new Font("Courier New", 10f),
            BackColor   = Color.DarkSlateGray,
            ForeColor   = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
        };
        body.Controls.Add(_historyList, 0, row);
        body.SetColumnSpan(_historyList, 2);
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
        var barcode = _manualEntry.Text.Trim();
        if (string.IsNullOrEmpty(barcode)) return;
        _manualEntry.Clear();
        await HandleScanAsync(barcode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Lifecycle
    // ─────────────────────────────────────────────────────────────────────────
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
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

        SetStatus($"Ready — scan a carton barcode on {portName} ({_settings.PlcBaudRate} baud)  or type it above.", Color.LightCyan);

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

        SetStatus($"Ready — listening for scanner on TCP port {port}  or type a barcode above.", Color.LightCyan);

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
        if (scanned.Length != 30 || scanned[0] != '%')
        {
            SetStatus($"Ignored: '{(scanned.Length > 30 ? scanned[..30] + "…" : scanned)}' (not a 30-char carton barcode).", Color.Gray);
            return;
        }

        SetStatus("Looking up carton...", Color.LightCyan);
        _barcodeLabel.Text = scanned;
        _itemLabel.Text    = string.Empty;
        _descLabel.Text    = string.Empty;
        _infoLabel.Text    = string.Empty;

        var box = await _boxRepo.GetBoxByBarcodeSerialAsync(_config.LineNumber, scanned, CancellationToken.None);
        if (box is null)
        {
            SetStatus($"⚠ Barcode not found in boxes{_config.LineNumber:00}.csv.", Color.Salmon);
            return;
        }
        if (box.PrintNum == 0)
        {
            SetStatus($"⚠ Carton found (Rec {box.RecId}) but was never printed.", Color.Salmon);
            return;
        }

        ItemDetail? item = null;
        var stackNum = box.StackNum.Trim();
        if (!string.IsNullOrWhiteSpace(stackNum))
        {
            var stacker = await _boxRepo.GetStackerAsync(_config.LineNumber, stackNum, CancellationToken.None);
            if (stacker is not null)
                item = await _boxRepo.GetItemDetailByIRefAsync(stacker.IRef, stacker.IsMexicoItem, CancellationToken.None);
        }

        if (item is null)
        {
            SetStatus($"⚠ Item detail not found for carton (Rec {box.RecId}, stack '{stackNum}').", Color.Salmon);
            return;
        }

        var serial   = await _boxRepo.AllocatePalletSerialAsync(item.Plant, CancellationToken.None);
        var palletId = $"{item.Plant:000}-{serial:000000000}";

        _itemLabel.Text = item.ItemNumber;
        _descLabel.Text = item.GetPrimaryItemDescription();
        _infoLabel.Text = $"Grade: {item.Grade}   Line: {_config.LineNumber:00}   Shift: {_shift}   Pallet Tag: {palletId}";

        SetStatus("Printing...", Color.Yellow);
        var success = await PrintPalletAsync(item, palletId);

        if (success)
        {
            var histLine = $"{DateTime.Now:HH:mm:ss}  {palletId}  {item.ItemNumber,-20}  {item.LisQty}pc × {item.BoxesPerPallet}ctn";
            _historyList.Items.Insert(0, histLine);
            if (_historyList.Items.Count > 100)
                _historyList.Items.RemoveAt(_historyList.Items.Count - 1);

            SetStatus($"✓ Printed: {palletId}   {item.ItemNumber}", Color.LightGreen);
        }
        else
        {
            SetStatus("⚠ Failed to send pallet label — check printer settings.", Color.Salmon);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Label printing
    // ─────────────────────────────────────────────────────────────────────────
    private async Task<bool> PrintPalletAsync(ItemDetail item, string palletId)
    {
        if (string.IsNullOrWhiteSpace(_settings.LabelOutputAddress))
            return false;

        var payload = new ThermalLabelPayload
        {
            LabelFormat      = "PALLET_LABEL",
            LabelTypeCode    = item.LabelTypeCode,
            PalletId         = palletId,
            ItemNumber       = item.ItemNumber,
            IRef             = item.IRef,
            Plant            = item.Plant,
            PartDescription  = item.GetPrimaryItemDescription(),
            ColorDesc        = item.ColorDesc,
            ShapeDesc        = item.ShapeDesc,
            SeriesDesc       = item.SeriesDesc,
            Shade            = item.Shade.ToString("0000"),
            Size             = item.SizeShape,
            BoxesPerPallet   = item.BoxesPerPallet,
            SalesQty         = item.SalesQty,
            SalesUom         = item.SalesUOM,
            PackageWeight    = item.PkgWeight,
            LisQty           = item.LisQty,
            Grade            = item.Grade,
            Location         = _settings.PalletLocation,
            PlantName        = _settings.PlantName,
            Inspector        = _inspector,
            Shift            = _shift,
            LineNumber       = _config.LineNumber,
            Quantity         = 1,
            UccBarcode       = item.GetUCC(),
            CartonUpc        = item.GetCartonUPC(),
            CartonUpcNumSys  = item.CartonUPC_NumSys.ToString("0"),
            CartonUpcMfg     = item.CartonUPC_Mfg.ToString("00000"),
            CartonUpcProd    = item.CartonUPC_Prod.ToString("00000"),
            CartonUpcChkdgt  = item.CartonUPC_Chkdgt.ToString("0"),
            CreatedAtUtc     = DateTime.UtcNow,
        };

        var exporter = new ThermalPrinterCommandExporter(
            _settings.LabelOutputAddress,
            _config.DataDirectory,
            _settings.ThermalPrinterType);

        var result = await exporter.ExportAsync(payload, CancellationToken.None);
        return result.Success;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────
    private void SetStatus(string text, Color color)
    {
        if (_statusLabel.IsDisposed) return;
        _statusLabel.Text      = text;
        _statusLabel.ForeColor = color;
    }
}
