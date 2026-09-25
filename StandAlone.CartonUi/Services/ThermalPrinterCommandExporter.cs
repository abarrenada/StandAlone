using StandAlone.CartonUi.Models;
using StandAlone.Integration.Services;
using System.IO.Ports;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace StandAlone.CartonUi.Services;

/// <summary>
/// Generates thermal-printer command text and writes it to an output file.
/// </summary>
public sealed class ThermalPrinterCommandExporter
{
    private readonly string _outputAddress;
    private readonly string _fallbackDirectory;
    private readonly string _thermalPrinterType;
    private readonly EngineHand _engineHand;
    private readonly int _baudRate;
    private readonly string _printerModel;

    private static readonly Regex ComPortPattern = new(@"^COM\d{1,3}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <param name="printerModel">
    /// This station's printer model (e.g. "M84Pro", "S86NX" — see AppSettings.PrinterModel),
    /// resolved once via PrinterModelCatalog to the physical engine hand that Standard Retail
    /// coordinate selection needs. Empty/unrecognized safely defaults to Right-hand.
    /// </param>
    /// <param name="baudRate">
    /// Baud rate used only when outputAddress is a bare COM port (e.g. "COM4") — the serial
    /// equivalent of Progress dev-detail's STTY field. Ignored for IP/file/UNC targets.
    /// </param>
    public ThermalPrinterCommandExporter(string outputAddress, string fallbackDirectory, string thermalPrinterType, string printerModel = "", int baudRate = 9600)
    {
        _outputAddress = outputAddress ?? string.Empty;
        _fallbackDirectory = fallbackDirectory;
        _thermalPrinterType = string.IsNullOrWhiteSpace(thermalPrinterType) ? "SATO" : thermalPrinterType;
        _engineHand = new PrinterModelCatalog(fallbackDirectory).GetHand(printerModel);
        _baudRate = baudRate > 0 ? baudRate : 9600;
        _printerModel = printerModel ?? string.Empty;
    }

    public async Task<ThermalExportResult> ExportAsync(ThermalLabelPayload payload, CancellationToken ct)
    {
        try
        {
            var commandText = ResolveCommandText(payload);
            var archivePath = ResolveArchivePath();

            var dir = Path.GetDirectoryName(archivePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // Always keep a local archive copy for traceability and troubleshooting.
            await File.WriteAllTextAsync(archivePath, commandText, Encoding.ASCII, ct);

            bool dispatched = false;
            string? target = null;
            string? dispatchError = null;

            if (!string.IsNullOrWhiteSpace(_outputAddress))
            {
                target = _outputAddress.Trim();
                try
                {
                    target = await DispatchToTargetAsync(target, commandText, ct);
                    dispatched = true;
                }
                catch (Exception ex)
                {
                    dispatchError = ex.Message;
                }
            }

            var success = string.IsNullOrWhiteSpace(_outputAddress) || dispatched;
            return new ThermalExportResult(success, archivePath, target, dispatched, commandText, dispatchError);
        }
        catch (Exception ex)
        {
            return new ThermalExportResult(false, string.Empty, null, false, string.Empty, ex.Message);
        }
    }

    private string ResolveCommandText(ThermalLabelPayload payload)
    {
        var type = NormalizePrinterType(_thermalPrinterType);
        if (type == "SATO")
        {
            var templateName = SatoTemplateResolver.ResolveTemplateFileName(payload);
            if (!string.IsNullOrWhiteSpace(templateName))
            {
                // A left-hand engine on a size whose legacy source only ever coded a right-hand
                // branch (2x7.25/2x6/1.75x8.38) is exactly the combination Progress itself
                // refuses to print a real label for — match that before even opening the template.
                if (templateName.StartsWith("lt_retail_", StringComparison.OrdinalIgnoreCase))
                {
                    var earlySizeToken = templateName["lt_retail_".Length..^".sato".Length];
                    if (ThermalPrinterCommandBuilder.RightHandOnlyRetailSizes.Contains(earlySizeToken) &&
                        _engineHand != EngineHand.Right)
                    {
                        return ThermalPrinterCommandBuilder.BuildUnsupportedPrinterLabel(payload);
                    }
                }

                var templatesDirectory = SatoTemplateResolver.FindTemplateDirectory();
                var templatePath = Path.Combine(templatesDirectory, templateName);
                if (File.Exists(templatePath))
                {
                    var template = StripCommentLines(File.ReadAllText(templatePath));
                    // Reuse an already-known barcode (e.g. a reprint's stored value) instead of
                    // recomputing from CreatedAtUtc, which would drift on a different calendar day.
                    if (string.IsNullOrWhiteSpace(payload.CartonBarcodeSerial))
                        payload.CartonBarcodeSerial = ThermalPrinterCommandBuilder.ComputeCartonBarcodeSerial(payload);
                    payload.CartonBarcodeCommand = ThermalPrinterCommandBuilder.ComputeCartonBarcodeCommand(payload.CartonBarcodeSerial);
                    payload.MfgDateCode = ThermalPrinterCommandBuilder.ComputeMfgDateCode(payload.CreatedAtUtc);
                    payload.ItemNumberMasked = ThermalPrinterCommandBuilder.MaskItemNumber(payload.ItemNumber);
                    payload.PartDescriptionShort = ThermalPrinterCommandBuilder.ClipField(payload.PartDescription, 36);
                    payload.ShadeLotCode = ThermalPrinterCommandBuilder.ComputeShadeLotCode(payload);
                    payload.InspectorDisplay = ThermalPrinterCommandBuilder.ComputeInspectorDisplay(payload);
                    payload.MachineLineTerminalCode = ThermalPrinterCommandBuilder.ComputeMachineLineTerminalCode(payload);
                    payload.MfgDateCodeShort = ThermalPrinterCommandBuilder.ComputeMfgDateCodeShort(payload.CreatedAtUtc);
                    (payload.ItemNumberPart1, payload.ItemNumberPart2) = ThermalPrinterCommandBuilder.SplitItemNumber(payload.ItemNumber);
                    payload.SalesQtyFormatted = ThermalPrinterCommandBuilder.FormatQty(payload.SalesQty);
                    payload.PackageWeightFormatted = ThermalPrinterCommandBuilder.FormatQty(payload.PackageWeight);
                    payload.PackageWeightMetricFormatted = ThermalPrinterCommandBuilder.ComputePackageWeightMetricFormatted(payload.PackageWeight);
                    payload.CoverageMetricFormatted = ThermalPrinterCommandBuilder.ComputeCoverageMetricFormatted(payload.SalesQty, payload.SalesUom);
                    if (templateName.StartsWith("lt04_default_", StringComparison.OrdinalIgnoreCase))
                    {
                        payload.FndReferenceMoveOffset = ThermalPrinterCommandBuilder.ComputeFndReferenceMoveOffset(_printerModel);
                        payload.Fnd3x45Footer = ThermalPrinterCommandBuilder.ComputeFnd3x45Footer(_printerModel);
                    }
                    if (string.Equals(templateName, "lt_default_45x3.sato", StringComparison.OrdinalIgnoreCase))
                    {
                        payload.EngineReferenceMove = ThermalPrinterCommandBuilder.ComputeMfg45x3ReferenceMove(_engineHand);
                        payload.GradeHighlightBlock = ThermalPrinterCommandBuilder.ComputeMfg45x3GradeHighlightBlock(payload.GradeHighlight, _engineHand);
                    }
                    if (templateName.StartsWith("lt_retail_", StringComparison.OrdinalIgnoreCase))
                    {
                        var sizeToken = templateName["lt_retail_".Length..^".sato".Length];
                        ThermalPrinterCommandBuilder.ApplyStandardRetailFields(payload, sizeToken, _engineHand);
                    }
                    if (templateName.StartsWith("lt_mexico_", StringComparison.OrdinalIgnoreCase))
                    {
                        var sizeToken = templateName["lt_mexico_".Length..^".sato".Length];
                        ThermalPrinterCommandBuilder.ApplyMexicoFields(payload, sizeToken);
                    }
                    var rendered = SatoTemplateRenderer.RenderTemplate(template, payload);
                    return DecodeSatoControlMarkers(rendered);
                }
            }
        }

        return ThermalPrinterCommandBuilder.Build(_thermalPrinterType, payload);
    }

    /// <summary>
    /// Removes '#'-prefixed documentation/comment lines from a .sato template before it's
    /// rendered and dispatched — these are for maintainers reading the source file, not the
    /// printer, and were previously being sent as literal plain-text bytes ahead of the real
    /// SBPL command stream (no STX/ESC prefix), which a printer's command parser has no defined
    /// way to handle safely.
    /// </summary>
    private static string StripCommentLines(string template)
    {
        if (string.IsNullOrEmpty(template))
            return template;

        var lines = template.Split('\n');
        var kept = lines.Where(line => !line.TrimStart().StartsWith('#'));
        return string.Join('\n', kept);
    }

    private static string NormalizePrinterType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "SATO";

        return value.Trim().ToLowerInvariant() switch
        {
            "sato" => "SATO",
            "ipl" => "IPL",
            "zpl" => "ZPL",
            "fingerprint" => "Fingerprint",
            _ => "SATO",
        };
    }

    private static string DecodeSatoControlMarkers(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Convert symbolic control markers used in repository templates to
        // raw SBPL control bytes expected by SATO printers.
        var decoded = text
            .Replace("<STX>", ((char)0x02).ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("<ETX>", ((char)0x03).ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("<ESC>", ((char)0x1B).ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("\\x1b", ((char)0x1B).ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("\\x1B", ((char)0x1B).ToString(), StringComparison.Ordinal);

        return Regex.Replace(
            decoded,
            "\\\\x(?<hex>[0-9A-Fa-f]{2})",
            match => ((char)Convert.ToByte(match.Groups["hex"].Value, 16)).ToString());
    }

    private async Task<string> DispatchToTargetAsync(string target, string commandText, CancellationToken ct)
    {
        var bytes = Encoding.ASCII.GetBytes(commandText);

        // A bare COM port (e.g. "COM4") must open as a real serial port — checked before the TCP
        // heuristic below, which would otherwise misread it as a literal hostname called "COM4"
        // (TryParseTcpTarget's letter-based fallback matches any alphabetic string).
        var trimmedTarget = target.Trim();
        if (ComPortPattern.IsMatch(trimmedTarget))
        {
            await SendSerialAsync(trimmedTarget, _baudRate, bytes, ct);
            return $"serial://{trimmedTarget} ({_baudRate} baud)";
        }

        if (TryParseTcpTarget(target, out var tcpHost, out var tcpPort))
        {
            await SendRawTcpAsync(tcpHost, tcpPort, bytes, ct);
            return $"tcp://{tcpHost}:{tcpPort}";
        }

        if (TryParseUncPrinterTarget(target, out var uncHost, out _))
        {
            try
            {
                using var uncStream = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.Read);
                await uncStream.WriteAsync(bytes, 0, bytes.Length, ct);
                await uncStream.FlushAsync(ct);
                return target;
            }
            catch
            {
                // Common scenario in restricted environments: UNC write blocked.
                // Fall back to raw TCP on default thermal port 9100.
                await SendRawTcpAsync(uncHost, 9100, bytes, ct);
                return $"{target} (fallback tcp://{uncHost}:9100)";
            }
        }

        // For explicit file paths, write text directly.
        if (LooksLikeWritableFilePath(target))
        {
            var targetDir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(targetDir))
                Directory.CreateDirectory(targetDir);

            await File.WriteAllTextAsync(target, commandText, Encoding.ASCII, ct);
            return target;
        }

        using var stream = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.Read);
        await stream.WriteAsync(bytes, 0, bytes.Length, ct);
        await stream.FlushAsync(ct);
        return target;
    }

    private static async Task SendRawTcpAsync(string host, int port, byte[] payload, CancellationToken ct)
    {
        using var client = new TcpClient();
        using var reg = ct.Register(() =>
        {
            try { client.Dispose(); } catch { }
        });

        await client.ConnectAsync(host, port);
        using var network = client.GetStream();
        await network.WriteAsync(payload, 0, payload.Length, ct);
        await network.FlushAsync(ct);
    }

    private static async Task SendSerialAsync(string portName, int baudRate, byte[] payload, CancellationToken ct)
    {
        using var port = new SerialPort(portName, baudRate) { WriteTimeout = 5000 };
        port.Open();
        try
        {
            await port.BaseStream.WriteAsync(payload, ct);
            await port.BaseStream.FlushAsync(ct);
        }
        finally
        {
            port.Close();
        }
    }

    private string ResolveArchivePath()
    {
        var fileName = $"thermal_{NormalizeToken(_thermalPrinterType)}_last.txt";
        return Path.Combine(_fallbackDirectory, fileName);
    }

    private static bool LooksLikeWritableFilePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (trimmed.EndsWith("\\") || trimmed.EndsWith("/"))
            return false;

        // A printer share like \\server\printer is not a file path.
        if (trimmed.StartsWith("\\\\", StringComparison.Ordinal) &&
            string.IsNullOrEmpty(Path.GetExtension(trimmed)))
            return false;

        return Path.IsPathRooted(trimmed) || trimmed.Contains(Path.DirectorySeparatorChar) || trimmed.Contains(Path.AltDirectorySeparatorChar);
    }

    private static bool TryParseUncPrinterTarget(string target, out string host, out string printer)
    {
        host = string.Empty;
        printer = string.Empty;

        if (string.IsNullOrWhiteSpace(target) || !target.StartsWith("\\\\", StringComparison.Ordinal))
            return false;

        var parts = target.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return false;

        host = parts[0].Trim();
        printer = parts[1].Trim();
        return host.Length > 0 && printer.Length > 0;
    }

    private static bool TryParseTcpTarget(string target, out string host, out int port)
    {
        host = string.Empty;
        port = 9100;

        if (string.IsNullOrWhiteSpace(target))
            return false;

        var value = target.Trim();

        if (value.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
            value = value[6..];

        if (value.StartsWith("\\\\", StringComparison.Ordinal))
            return false;

        var colonIndex = value.LastIndexOf(':');
        if (colonIndex > 0 && colonIndex < value.Length - 1)
        {
            host = value[..colonIndex].Trim();
            if (!int.TryParse(value[(colonIndex + 1)..].Trim(), out port) || port < 1 || port > 65535)
                return false;

            return host.Length > 0;
        }

        // Host or IP without explicit port.
        if (value.Contains('.') || value.Contains('-') || value.Any(char.IsLetter))
        {
            host = value;
            port = 9100;
            return true;
        }

        return false;
    }

    private static string NormalizeToken(string value)
    {
        var chars = value.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray();
        return chars.Length == 0 ? "SATO" : new string(chars);
    }
}

public sealed record ThermalExportResult(
    bool Success,
    string ArchivePath,
    string? DispatchTarget,
    bool Dispatched,
    string CommandText,
    string? ErrorMessage);

internal static class ThermalPrinterCommandBuilder
{
    public static string Build(string thermalPrinterType, ThermalLabelPayload payload)
    {
        var type = NormalizeType(thermalPrinterType);
        return type switch
        {
            "SATO" => BuildSato(payload),
            "IPL" => BuildIpl(payload),
            "ZPL" => BuildZpl(payload),
            "Fingerprint" => BuildFingerprint(payload),
            _ => BuildSato(payload),
        };
    }

    private static string BuildSato(ThermalLabelPayload p)
    {
        if (string.Equals(p.LabelFormat, "PALLET_LABEL", StringComparison.OrdinalIgnoreCase))
            return BuildSatoPallet(p);

        return BuildSatoCarton(p);
    }

    private static string BuildSatoCarton(ThermalLabelPayload p)
    {
        // Progress-style framing used in legacy SATO flows: STX + ESC A ... ESC Z + ETX.
        const char stx = (char)0x02;
        const char etx = (char)0x03;
        const char esc = (char)0x1B;

        var localTime = p.CreatedAtUtc.ToLocalTime();
        var hhmm = localTime.ToString("HHmm");
        // Reuse an already-known barcode (e.g. a reprint's stored value) instead of
        // recomputing from CreatedAtUtc, which would drift on a different calendar day.
        var bc128 = string.IsNullOrWhiteSpace(p.CartonBarcodeSerial) ? ComputeCartonBarcodeSerial(p) : p.CartonBarcodeSerial;
        var descLine = BuildDescriptionLine(p);

        var businessTypeName = ResolveBusinessLabelTypeName(p.LabelTypeCode);
        var title = string.Equals(p.LabelFormat, "SLAB_LABEL", StringComparison.OrdinalIgnoreCase)
            ? "SLAB LABEL"
            : $"{businessTypeName} CARTON LABEL";

        var barcodeCaption = string.Equals(p.LabelFormat, "SLAB_LABEL", StringComparison.OrdinalIgnoreCase)
            ? "SLAB 128:"
            : "128:";

        var sb = new StringBuilder();
        sb.Append(stx);
        sb.Append(esc).Append("A").AppendLine();
        sb.Append(esc).Append("CS2").AppendLine();
        sb.Append(esc).Append("Q").Append(Math.Clamp(p.Quantity, 1, 999)).AppendLine();
        sb.Append(esc).Append("H0180").Append(esc).Append("V0080").Append(esc)
            .Append("L0202").Append(esc).Append("XM").Append(title).AppendLine();

        sb.Append(esc).Append("H0180").Append(esc).Append("V0145").Append(esc)
            .Append("XM").Append(ClipAscii($"ITEM: {p.ItemNumber}", 40)).AppendLine();
        sb.Append(esc).Append("H0180").Append(esc).Append("V0190").Append(esc)
            .Append("XM").Append(ClipAscii($"SHADE: {ComputeShadeLotCode(p)}   SIZE: {p.Size}", 40)).AppendLine();
        sb.Append(esc).Append("H0180").Append(esc).Append("V0235").Append(esc)
            .Append("XM").Append(ClipAscii(descLine, 48)).AppendLine();

        sb.Append(esc).Append("H0180").Append(esc).Append("V0280").Append(esc)
            .Append("XM").Append(ClipAscii($"QTY/CTN: {p.LisQty:00000}   SALES: {p.SalesQty:0.##} {p.SalesUom}", 48)).AppendLine();
        sb.Append(esc).Append("H0180").Append(esc).Append("V0325").Append(esc)
            .Append("XM").Append(ClipAscii($"WT: {p.PackageWeight:0.#} LB   LINE: {p.LineNumber:00} SHIFT: {p.Shift}", 48)).AppendLine();
        sb.Append(esc).Append("H0180").Append(esc).Append("V0370").Append(esc)
            .Append("XM").Append(ClipAscii($"INSP: {ComputeInspectorDisplay(p)}  STACK: {p.StackNumber}  SIZECD: {p.LabelSize}", 48)).AppendLine();
        sb.Append(esc).Append("H0180").Append(esc).Append("V0415").Append(esc)
            .Append("XM").Append(ClipAscii($"DATE: {localTime.Year:0000}{localTime.DayOfYear:000}:{hhmm}  IREF: {p.IRef:000000}  PLANT: {p.Plant:000}", 48)).AppendLine();
        sb.Append(esc).Append("H0180").Append(esc).Append("V0450").Append(esc)
            .Append("XM").Append(ClipAscii($"LTYPE: {p.LabelTypeCode:00} {businessTypeName}  FORMAT: {p.LabelFormat}", 48)).AppendLine();

        var upcBarcode = string.IsNullOrWhiteSpace(p.CartonUpc) ? p.ItemNumber : p.CartonUpc;
        sb.Append(esc).Append("H0180").Append(esc).Append("V0470").Append(esc)
            .Append("XM").Append("UPC:").AppendLine();
        sb.Append(esc).Append("H0180").Append(esc).Append("V0510").Append(esc)
            .Append("B103100").Append(esc).Append("D").Append(ClipAscii(upcBarcode, 20)).AppendLine();

        sb.Append(esc).Append("H0680").Append(esc).Append("V0470").Append(esc)
            .Append("XM").Append(barcodeCaption).AppendLine();
        sb.Append(esc).Append("H0680").Append(esc).Append("V0510").Append(esc)
            .Append("B103080").Append(esc).Append("D").Append(ClipAscii(bc128, 32)).AppendLine();

        sb.Append(esc).Append("Z");
        sb.Append(etx);
        return sb.ToString();
    }

    private static string BuildSatoPallet(ThermalLabelPayload p)
    {
        // Faithful port of Progress dtlbl101b.i (WMS 4x6 pallet label), M8400RV/84Pro
        // printer-model branch — field-for-field, coordinate-for-coordinate.
        const char stx = (char)0x02;
        const char etx = (char)0x03;
        const char esc = (char)0x1B;
        const string tSpeed = "CS2";
        const string tHeat = "#E2";

        var localTime = p.CreatedAtUtc.ToLocalTime();

        // prt-pal-tag-bc = PPP + SSSSSSSSS (12 chars, no dash) — used in barcode
        // prt-tag-nbr    = PPP-SSSSSSSSS (13 chars with dash) — used in text
        var tagDisplay = string.IsNullOrWhiteSpace(p.PalletId) ? string.Empty : p.PalletId.Trim();
        var tagBarcode = tagDisplay.Replace("-", string.Empty, StringComparison.Ordinal);

        // Shade: string(prt-shade,"9999") + prt-size  (4-digit shade + 1-char size code)
        var shadeInt = int.TryParse(DigitsOnly(p.Shade, 10, "0"), out var sv) ? sv : 0;
        var shade4   = shadeInt.ToString("0000");
        var sizeCode = !string.IsNullOrWhiteSpace(p.Caliber)
            ? p.Caliber.Trim()[..Math.Min(1, p.Caliber.Trim().Length)]
            : (!string.IsNullOrWhiteSpace(p.Size) ? p.Size.Trim()[..Math.Min(1, p.Size.Trim().Length)] : "0");

        // itemhdr.ih-wms-uom = "CT" overrides pkgconfig/total-pieces to use boxes-per-pallet
        // directly instead of LisQty * BoxesPerPallet (Progress dtscn011.p ~line 661/1031).
        var isCtUom = string.Equals(p.WmsUom, "CT", StringComparison.OrdinalIgnoreCase);

        // prt-nbr-pc: LisQty × BoxesPerPallet, unless CT-uom (then just BoxesPerPallet).
        var totalPcs = isCtUom
            ? (long)Math.Max(0, p.BoxesPerPallet)
            : (long)p.LisQty * Math.Max(0, p.BoxesPerPallet);

        // prt-yyyyjjj = YYYY + JJJ (7 chars)
        var mfgDate = $"{localTime.Year:0000}{localTime.DayOfYear:000}";

        // prt-pkgconfig = trim(LisQty) + "P" + trim(BoxesPerPallet) + "C" (no zero-padding,
        // no separating space), or "XXX" + BoxesPerPallet + "C" for CT-uom items.
        var pkgConfig = isCtUom
            ? $"XXX{Math.Max(0, p.BoxesPerPallet)}C"
            : $"{p.LisQty}P{Math.Max(0, p.BoxesPerPallet)}C";

        // Plant name capped at 15 chars (substr(prt-plant-name,1,15))
        var plantName = string.IsNullOrWhiteSpace(p.PlantName)
            ? string.Empty
            : p.PlantName[..Math.Min(15, p.PlantName.Length)];

        // SKU field format "XXXX XXXXXXXXXXX": first 4 chars + literal space + next 11 chars.
        var skuField = FormatPalletSkuField(p.ItemNumber);

        var sb = new StringBuilder();
        sb.Append(stx);
        sb.Append(esc).Append("A");
        sb.Append(esc).Append(tSpeed);
        sb.Append(esc).Append(tHeat);
        sb.Append(esc).Append("%3");

        // ── Tag barcode: overline / barcode / underline ───────────────────────
        sb.Append(esc).Append("H0750").Append(esc).Append("V0175").Append(esc).Append("FW03H540");
        sb.Append(esc).Append("H0750").Append(esc).Append("V0187").Append(esc).Append("BG05197>I").Append(tagBarcode);
        sb.Append(esc).Append("H0553").Append(esc).Append("V0175").Append(esc).Append("FW03H540");

        // ── QC-hold audit flag: no qc-holds equivalent in this system, always blank ──
        sb.Append(esc).Append("H0730").Append(esc).Append("V0950").Append(esc).Append("L0404").Append(esc).Append("WL0");

        // ── Machine/line/term, date/time, and user footer ─────────────────────
        sb.Append(esc).Append("H0750").Append(esc).Append("V1040").Append(esc).Append("L0101").Append(esc).Append("S")
            .Append(ClipAscii(Environment.MachineName, 4)).Append('-')
            .Append(p.LineNumber.ToString("00")).Append('-')
            .Append(ClipAscii(p.PrinterTermId, 5));
        sb.Append(esc).Append("H0730").Append(esc).Append("V1040").Append(esc).Append("L0101").Append(esc).Append("S")
            .Append(localTime.ToString("MM/dd/yy")).Append(':').Append(localTime.ToString("HHmm"));
        sb.Append(esc).Append("H0710").Append(esc).Append("V1040").Append(esc).Append("L0101").Append(esc).Append("S")
            .Append(ClipAscii(p.UserId, 8));

        // ── Tag: ──────────────────────────────────────────────────────────────
        sb.Append(esc).Append("H0540").Append(esc).Append("V0025").Append(esc).Append("L0101").Append(esc).Append("XB0").Append("Tag:");
        sb.Append(esc).Append("H0540").Append(esc).Append("V0125").Append(esc).Append("L0201").Append(esc).Append("WL0").Append(tagDisplay);

        // ── SKU: ──────────────────────────────────────────────────────────────
        sb.Append(esc).Append("H0430").Append(esc).Append("V0025").Append(esc).Append("L0101").Append(esc).Append("XB0").Append("SKU:");
        sb.Append(esc).Append("H0470").Append(esc).Append("V0125").Append(esc).Append("L0202").Append(esc).Append("WL0").Append(skuField);

        // ── Shd: ──────────────────────────────────────────────────────────────
        sb.Append(esc).Append("H0350").Append(esc).Append("V0025").Append(esc).Append("L0101").Append(esc).Append("XB0").Append("Shd:");
        sb.Append(esc).Append("H0350").Append(esc).Append("V0125").Append(esc).Append("L0201").Append(esc).Append("WL0").Append(shade4).Append(sizeCode);
        sb.Append(esc).Append("H0350").Append(esc).Append("V0450").Append(esc).Append("L0101").Append(esc).Append("XB0")
            .Append("Plant: ").Append(p.Plant.ToString("000")).Append(" - ").Append(plantName);

        // ── Qty: | Grade: | Shift: | Line: ────────────────────────────────────
        sb.Append(esc).Append("H0290").Append(esc).Append("V0025").Append(esc).Append("L0101").Append(esc).Append("XB0")
            .Append("Qty: ").Append(totalPcs.ToString("#,##0"));
        sb.Append(esc).Append("H0290").Append(esc).Append("V0450").Append(esc).Append("L0101").Append(esc).Append("XB0")
            .Append("Grade: ").Append(p.Grade);
        sb.Append(esc).Append("H0290").Append(esc).Append("V0740").Append(esc).Append("L0101").Append(esc).Append("XB0")
            .Append("Shift: ").Append(p.Shift);
        sb.Append(esc).Append("H0290").Append(esc).Append("V0940").Append(esc).Append("L0101").Append(esc).Append("XB0")
            .Append("Line: ").Append(p.LineNumber.ToString("00"));

        // ── pkgconfig | Loc: | MfgDate: ────────────────────────────────────────
        sb.Append(esc).Append("H0220").Append(esc).Append("V0025").Append(esc).Append("L0101").Append(esc).Append("XB0").Append(pkgConfig);
        sb.Append(esc).Append("H0220").Append(esc).Append("V0450").Append(esc).Append("L0101").Append(esc).Append("XB0")
            .Append("Loc: ").Append(p.Location);
        sb.Append(esc).Append("H0220").Append(esc).Append("V0740").Append(esc).Append("L0101").Append(esc).Append("XB0")
            .Append("MfgDate: ").Append(mfgDate);

        // ── Carton reference barcode (last scanned carton, if this pallet came from a scan) ──
        if (!string.IsNullOrWhiteSpace(p.CartonReferenceBarcode))
        {
            var cartonBc = p.CartonReferenceBarcode;
            sb.Append(esc).Append("H0160").Append(esc).Append("V0097").Append(esc).Append("FW03H680");
            sb.Append(esc).Append("H0160").Append(esc).Append("V0100").Append(esc).Append("BG03085>H")
                .Append(cartonBc.Length >= 2 ? cartonBc[..2] : cartonBc).Append(">C")
                .Append(cartonBc.Length > 2 ? cartonBc[2..] : string.Empty);
            sb.Append(esc).Append("H0080").Append(esc).Append("V0097").Append(esc).Append("FW03H680");
            sb.Append(esc).Append("H0070").Append(esc).Append("V0120").Append(esc).Append("L0101").Append(esc).Append("M").Append(cartonBc);
        }

        // ── Pallet-ID barcode (verbatim from dtlbl101b.i — literal "WMS" content) ─
        sb.Append(esc).Append("H0160").Append(esc).Append("V0892").Append(esc).Append("FW03H210");
        sb.Append(esc).Append("H0160").Append(esc).Append("V0900").Append(esc).Append("BC0310003WMS");
        sb.Append(esc).Append("H0060").Append(esc).Append("V0892").Append(esc).Append("FW03H210");

        // Progress prt-qty is never assigned in this flow, so ESC Q is always sent with no
        // trailing digit — matching production behavior (the printer defaults to 1 label).
        sb.Append(esc).Append("Q");
        sb.Append(esc).Append("Z");
        sb.Append(etx);
        return sb.ToString();
    }

    /// <summary>
    /// Progress dtlbl101b.i SKU field format picture "XXXX XXXXXXXXXXX": first 4 source
    /// characters, a literal space, then the next 11 source characters (16 chars total).
    /// </summary>
    private static string FormatPalletSkuField(string? itemNumber)
    {
        var padded = (itemNumber ?? string.Empty).PadRight(15);
        var value = padded.Length > 15 ? padded[..15] : padded;
        return $"{value[..4]} {value[4..15]}";
    }

    private static string BuildIpl(ThermalLabelPayload p)
    {
        var businessTypeName = ResolveBusinessLabelTypeName(p.LabelTypeCode);
        var sb = new StringBuilder();
        sb.AppendLine("<STX><ESC>P");
        sb.AppendLine("q400");
        sb.AppendLine("Q120,24");
        sb.AppendLine($"A20,2,0,3,1,1,N,\"LTYPE {p.LabelTypeCode:00} {ClipAscii(businessTypeName, 18)}\"");
        sb.AppendLine($"A20,20,0,4,1,1,N,\"ITEM {ClipAscii(p.ItemNumber, 24)}\"");
        sb.AppendLine($"A20,50,0,3,1,1,N,\"{ClipAscii(p.PartDescription, 40)}\"");
        sb.AppendLine($"A20,80,0,3,1,1,N,\"SHADE {ClipAscii(ComputeShadeLotCode(p), 8)} SIZE {ClipAscii(p.Size, 8)}\"");
        sb.AppendLine($"A20,110,0,3,1,1,N,\"SHIFT {p.Shift} INSPECTOR {ClipAscii(ComputeInspectorDisplay(p), 16)}\"");
        sb.AppendLine($"A20,140,0,3,1,1,N,\"STACK {ClipAscii(p.StackNumber, 8)} QTY {Math.Clamp(p.Quantity, 1, 999)}\"");
        var barcode = string.IsNullOrWhiteSpace(p.CartonUpc) ? p.ItemNumber : p.CartonUpc;
        sb.AppendLine($"B20,170,0,1,3,6,70,N,\"{ClipAscii(barcode, 20)}\"");
        sb.AppendLine("P1");
        sb.AppendLine("<ETX>");
        return sb.ToString();
    }

    private static string BuildZpl(ThermalLabelPayload p)
    {
        var barcode = string.IsNullOrWhiteSpace(p.CartonUpc) ? p.ItemNumber : p.CartonUpc;
        var businessTypeName = ResolveBusinessLabelTypeName(p.LabelTypeCode);

        var sb = new StringBuilder();
        sb.AppendLine("^XA");
        sb.AppendLine("^PW812");
        sb.AppendLine("^LL406");
        sb.AppendLine("^CF0,36");
        sb.AppendLine($"^FO40,5^A0N,20,20^FDLTYPE {p.LabelTypeCode:00} {ClipAscii(businessTypeName, 18)}^FS");
        sb.AppendLine($"^FO40,30^FDITEM {ClipAscii(p.ItemNumber, 24)}^FS");
        sb.AppendLine($"^FO40,80^A0N,28,28^FD{ClipAscii(p.PartDescription, 46)}^FS");
        sb.AppendLine($"^FO40,120^A0N,28,28^FDSHADE {ClipAscii(ComputeShadeLotCode(p), 8)}  SIZE {ClipAscii(p.Size, 8)}^FS");
        sb.AppendLine($"^FO40,160^A0N,28,28^FDSHIFT {p.Shift}  INSPECTOR {ClipAscii(ComputeInspectorDisplay(p), 16)}^FS");
        sb.AppendLine($"^FO40,200^A0N,28,28^FDSTACK {ClipAscii(p.StackNumber, 8)}  QTY {Math.Clamp(p.Quantity, 1, 999)}^FS");
        sb.AppendLine($"^FO40,245^BY2,3,70^BCN,70,N,N,N^FD{ClipAscii(barcode, 20)}^FS");
        sb.AppendLine("^XZ");
        return sb.ToString();
    }

    private static string BuildFingerprint(ThermalLabelPayload p)
    {
        var barcode = string.IsNullOrWhiteSpace(p.CartonUpc) ? p.ItemNumber : p.CartonUpc;

        var sb = new StringBuilder();
        sb.AppendLine("NEW");
        sb.AppendLine("SETSTDIO " + '"' + "USB1:" + '"');
        sb.AppendLine("PRPOS 20,20");
        sb.AppendLine($"PP 20,20:{Quote(ClipAscii($"ITEM {p.ItemNumber}", 24))}");
        sb.AppendLine($"PP 20,50:{Quote(ClipAscii(p.PartDescription, 40))}");
        sb.AppendLine($"PP 20,80:{Quote(ClipAscii($"SHADE {ComputeShadeLotCode(p)} SIZE {p.Size}", 30))}");
        sb.AppendLine($"PP 20,110:{Quote(ClipAscii($"SHIFT {p.Shift} INSPECTOR {ComputeInspectorDisplay(p)}", 36))}");
        sb.AppendLine($"PP 20,140:{Quote(ClipAscii($"STACK {p.StackNumber} QTY {Math.Clamp(p.Quantity, 1, 999)}", 24))}");
        sb.AppendLine($"BARSET " + '"' + "CODE128" + '"' + $",2,2,80");
        sb.AppendLine($"BAR 20,180:{Quote(ClipAscii(barcode, 20))}");
        sb.AppendLine("PRINTFEED 1");
        return sb.ToString();
    }

    /// <summary>
    /// Builds the 30-character carton barcode serial — identical to the Progress dtlbl060b.i formula.
    /// Format: % + year(4) + julday(3) + iref(6) + shade2(4) + sizeCode(1) + shift(1) + lineId(2) + plant(3) + lisQty(5)
    /// shade2: if shade &lt; 1000 (3-digit shade), multiply by 10; otherwise use as-is.
    /// </summary>
    internal static string ComputeCartonBarcodeSerial(
        DateTime localTime, int iRef, int shade, string sizeCode, int shift, int lineId, int plant, int lisQty, char prefix = '%')
    {
        var year4  = localTime.Year.ToString("0000");
        var julday = localTime.DayOfYear.ToString("000");
        var shade2 = shade < 1000 ? shade * 10 : shade;
        var szCode = string.IsNullOrWhiteSpace(sizeCode) ? "0" : sizeCode.Trim()[..1];
        return $"{prefix}{year4}{julday}{Math.Clamp(iRef, 0, 999999):000000}{shade2:0000}{szCode}{Math.Clamp(shift, 0, 9)}{Math.Clamp(lineId, 0, 99):00}{Math.Clamp(plant, 0, 999):000}{Math.Clamp(lisQty, 0, 99999):00000}";
    }

    internal static string ComputeCartonBarcodeSerial(ThermalLabelPayload p)
    {
        var localTime = p.CreatedAtUtc.ToLocalTime();
        var shadeInt = int.TryParse(DigitsOnly(p.Shade, 10, "0"), out var sv) ? sv : 0;
        var sizeCode = string.IsNullOrWhiteSpace(p.Caliber) ? p.Size : p.Caliber;
        return ComputeCartonBarcodeSerial(localTime, p.IRef, shadeInt, sizeCode, p.Shift, p.LineNumber, p.Plant, p.LisQty);
    }

    /// <summary>
    /// Builds the "BG" symbology barcode-encoding directive (dtlbl060b.i/dtlbl065d.i):
    /// "&gt;H" + first 2 chars + "&gt;C" + the rest — a real Code128 mode switch (Set C packs the
    /// remaining all-numeric digits two-per-symbol), not a cosmetic split. See
    /// ThermalLabelPayload.CartonBarcodeCommand.
    /// </summary>
    internal static string ComputeCartonBarcodeCommand(string cartonBarcodeSerial)
    {
        if (string.IsNullOrEmpty(cartonBarcodeSerial) || cartonBarcodeSerial.Length < 3)
            return $">H{cartonBarcodeSerial}";

        return $">H{cartonBarcodeSerial[..2]}>C{cartonBarcodeSerial[2..]}";
    }

    /// <summary>
    /// Builds the combined 5-digit shade/lot code for label display — Progress dtlbl060b.i's
    /// prt-shade2 (4-digit shade*10) + prt-size (trailing lot digit), printed together under
    /// "Shade/Teinte". Reuses the exact same shade/size resolution as ComputeCartonBarcodeSerial
    /// so the visible code always matches what's encoded in the barcode.
    /// </summary>
    internal static string ComputeShadeLotCode(ThermalLabelPayload p)
    {
        var shadeInt = int.TryParse(DigitsOnly(p.Shade, 10, "0"), out var sv) ? sv : 0;
        var shade2 = shadeInt < 1000 ? shadeInt * 10 : shadeInt;
        var sizeCode = string.IsNullOrWhiteSpace(p.Caliber) ? p.Size : p.Caliber;
        var szChar = string.IsNullOrWhiteSpace(sizeCode) ? "0" : sizeCode.Trim()[..1];
        return $"{shade2:0000}{szChar}";
    }

    /// <summary>
    /// Builds "{Inspector} {PhysicalStackNumber:00}" for carton-label display — Progress
    /// dtplc067.p's prt-inspector + " " + string(stacker.st-stacknum,"99"), falling back to
    /// "00" when no stacker applies (dtplc067_prep.p: "else LabelVarB = LabelVarB + '00'").
    /// </summary>
    internal static string ComputeInspectorDisplay(ThermalLabelPayload p)
    {
        var stackDigits = int.TryParse(DigitsOnly(p.PhysicalStackNumber, 2, "0"), out var sn) ? sn : 0;
        var stackCode = string.IsNullOrWhiteSpace(p.PhysicalStackNumber) ? "00" : stackDigits.ToString("00");
        return $"{p.Inspector} {stackCode}";
    }

    /// <summary>
    /// Builds the machine-line-terminal traceability stamp printed on every Standard Retail size
    /// (Progress: substr(t-machine,4,4) + "-" + string(dd-line-nbr,"99") + "-" + w-trk-term).
    /// This port has no analog of Progress's t-machine hostname variable, so the 4-char "machine"
    /// segment is approximated from the local workstation name — the line/terminal segments are
    /// exact (LineNumber, and PrinterTermId which is already our w-trk-term equivalent).
    /// </summary>
    internal static string ComputeMachineLineTerminalCode(ThermalLabelPayload p)
    {
        var machineName = (Environment.MachineName ?? string.Empty).PadRight(7);
        var machineCode = machineName.Substring(3, 4);
        return $"{machineCode}-{Math.Clamp(p.LineNumber, 0, 99):00}-{p.PrinterTermId}";
    }

    /// <summary>
    /// Builds the interleaved-2-of-5 case/shipping barcode payload (Progress prt-scs / s-i2of5,
    /// dtlbl006.i), derived entirely from this item's own carton UPC/pkg-indicator fields:
    /// pkgIndicator(1) + "0" + cartonUpcNumSys(1) + cartonUpcMfg(5,"99999") + cartonUpcProd(5,"99999"),
    /// followed by a check digit (odd positions * 3 + even positions, then (10 - total) mod 10).
    /// Returns empty when there's no carton UPC to derive it from, matching legacy's "prt-scs gt ''" gate.
    /// </summary>
    internal static string ComputeCaseShippingCode(ThermalLabelPayload p)
    {
        if (string.IsNullOrWhiteSpace(p.CartonUpcMfg) || string.IsNullOrWhiteSpace(p.CartonUpcProd))
            return string.Empty;

        var numSys = int.TryParse(p.CartonUpcNumSys, out var ns) ? ns : 0;
        var mfg = int.TryParse(p.CartonUpcMfg, out var mv) ? mv : 0;
        var prd = int.TryParse(p.CartonUpcProd, out var pv) ? pv : 0;

        var body = $"{Math.Clamp(p.PkgIndicator, 0, 9)}0{numSys}{mfg:00000}{prd:00000}";

        var odd = 0;
        var even = 0;
        for (var i = 0; i < body.Length; i++)
        {
            var digit = body[i] - '0';
            if (i % 2 == 0) odd += digit; else even += digit;
        }
        var check = (10 - ((odd * 3 + even) % 10)) % 10;

        return body + check;
    }

    /// <summary>
    /// Populates every Standard Retail computed field (side panel, ColorDesc swap, grade highlight,
    /// case barcode, qty/weight block) for the given size token, following that size's exact legacy
    /// source file. Called from ResolveCommandText once the target lt_retail_{size} template is
    /// already known. sizeToken matches SatoTemplateResolver's normalized tokens.
    /// </summary>
    internal static void ApplyStandardRetailFields(ThermalLabelPayload p, string sizeToken, EngineHand engineHand)
    {
        p.MachineLineTerminalCode = ComputeMachineLineTerminalCode(p);
        p.CaseShippingCode = ComputeCaseShippingCode(p);
        p.ColorDescDisplay = p.ColorDesc;
        p.CustomerHighlightBlock = string.Empty;

        switch (sizeToken)
        {
            case "45x3": ApplyRetail45x3(p, engineHand); break;
            case "4x3": ApplyRetail4x3(p, engineHand); break;
            case "2x725": ApplyRetail2x725(p); break;
            case "2x6": ApplyRetail2x6(p); break;
            case "3x45": ApplyRetail3x45(p); break;
            case "175x838": ApplyRetail175x838(p); break;
        }
    }

    /// <summary>
    /// The 2x7.25/2x6/1.75x8.38 sizes only ever ported a right-hand engine branch (their legacy
    /// source has no left-hand case at all) — a left-hand model on these sizes is exactly the
    /// combination Progress itself refuses to print a real label for.
    /// </summary>
    internal static readonly HashSet<string> RightHandOnlyRetailSizes = new(StringComparer.Ordinal)
    {
        "2x725", "2x6", "175x838",
    };

    /// <summary>
    /// Literal Progress "NON SUPPORTED PRINTER STYLE" fallback (dtlbl060d.i/060r.i/060o.i's
    /// unsupported-model else-branch), used verbatim instead of the normal label when the
    /// station's engine hand isn't one this size's legacy source ever supported.
    /// </summary>
    internal static string BuildUnsupportedPrinterLabel(ThermalLabelPayload p) =>
        "\x02\x1BA\x1BCS4\x1B#E2\x1BN\x1BA3H000V000\x1BH200\x1BV0280\x1BL0102\x1BMNON SUPPORTED PRINTER STYLE\x1BZ\x03";

    /// <summary>
    /// PEI/WA/COF/Tone/grade icon stack (PanelType=3 side panel), shared shape across every
    /// Standard Retail size — each size supplies its own H/V coordinates and rotate wrap.
    /// COF is compared as a category (legacy prt-cof eq 1/2/3) derived from the raw COF decimal
    /// using the thresholds documented in dtlbl060b.i's own comments (&lt;= .42 / .42-.60 / &gt;= .60).
    /// </summary>
    private static string BuildIconStack(ThermalLabelPayload p, string peiPos, string waPos, string cofPos,
        string tonePos, string gradePos, string wrapPrefix, string wrapSuffix = "")
    {
        var tmp = new StringBuilder();

        var peiIcon = p.Pei switch { 1 => "GR007", 2 => "GR008", 3 => "GR009", 4 => "GR010", _ => null };
        if (peiIcon != null) tmp.Append(peiPos).Append("\x1B").Append(peiIcon);

        var waIcon = p.Wa switch { 0.5m => "GR018", 3.0m => "GR011", 3.5m => "GR012", 5.0m => "GR013", 7.0m => "GR015", 16.0m => "GR014", _ => null };
        if (waIcon != null) tmp.Append(waPos).Append("\x1B").Append(waIcon);

        string? cofIcon = p.Cof <= 0 ? null : p.Cof <= 0.42m ? "GR004" : p.Cof >= 0.60m ? "GR006" : "GR005";
        if (cofIcon != null) tmp.Append(cofPos).Append("\x1B").Append(cofIcon);

        if (string.Equals(p.Tone, "Y", StringComparison.OrdinalIgnoreCase))
            tmp.Append(tonePos).Append("\x1BGR003");

        if (tmp.Length == 0)
            return string.Empty;

        tmp.Append(gradePos).Append(p.Grade == 2 ? "\x1BGR002" : "\x1BGR001");
        return wrapPrefix + tmp + wrapSuffix;
    }

    internal static string FormatQty(decimal value) => value.ToString("0.00");

    /// <summary>
    /// The ws-dev-model-driven reference-point move dtlbl060b.i chooses before drawing anything
    /// — the only per-model coordinate difference in the whole file; every other H/V value that
    /// follows is positioned relative to this one point, same for all three hands.
    /// </summary>
    private static string ComputeEngineReferenceMove(EngineHand hand) => hand switch
    {
        EngineHand.Left85L => "\x1BA3H100V0005",
        EngineHand.LeftS86LD => "\x1BA3H430V0005",
        _ => "\x1BA3H-17V0005",
    };

    // ── 4.5x3 (dtlbl060b.i) ──────────────────────────────────────────────────
    private static void ApplyRetail45x3(ThermalLabelPayload p, EngineHand engineHand)
    {
        p.EngineReferenceMove = ComputeEngineReferenceMove(engineHand);

        var tmp = p.PanelType switch
        {
            3 => BuildIconStack(p, "\x1BH0032\x1BV0010", "\x1BH0032\x1BV0130", "\x1BH0032\x1BV0240",
                                 "\x1BH0032\x1BV0350", "\x1BH0032\x1BV0460", "\x1BCC1\x1B%0"),
            2 when !string.IsNullOrWhiteSpace(p.CustomerPartNumber) =>
                $"\x1B%3\x1BH0120\x1BV0020\x1BL0408\x1BS{p.CustomerPartNumber}\x1BH0129\x1BV0020\x1B(595,128",
            1 when !string.IsNullOrWhiteSpace(p.BrandName) =>
                $"\x1B%3\x1BH0115\x1BV0030\x1BL0203\x1BWB1{p.BrandName}\x1B%3\x1BH0120\x1BV0010\x1B(595,095",
            _ => string.Empty,
        };
        p.SidePanelBlock = tmp.Length > 0 ? tmp + "\x1B%2" : string.Empty;

        p.GradeHighlightBlock = p.GradeHighlight ? "\x1BH215\x1BV0343\x1B(070,028" : string.Empty;

        p.CaseBarcodeBlock = (!string.IsNullOrWhiteSpace(p.CaseShippingCode) && p.PanelType != 3)
            ? $"\x1BH890\x1BV0133\x1BFW03H487\x1BH888\x1BV0133\x1BBD202075{p.CaseShippingCode}\x1BH890\x1BV0060\x1BFW03H487\x1BH815\x1BV0048\x1BL0102\x1BS{p.CaseShippingCode}"
            : string.Empty;

        p.QtyWeightBlock = p.Quantity == p.LisQty
            ? $"\x1BH395\x1BV0273\x1BL0102\x1BSCoverage\x1BH305\x1BV0268\x1BL0101\x1BM{FormatQty(p.SalesQty)} {p.SalesUom}\x1BH305\x1BV0243\x1BL0101\x1BM{FormatQty(p.PackageWeight)} LBS\x1BH305\x1BV0218\x1BL0101\x1BM{p.LisQty} {p.LisDesc}"
            : $"\x1BH290\x1BV0218\x1BL0101\x1BM{p.Quantity} {p.LisDesc}";
    }

    // ── 4x3 (dtlbl060b.i, LblSz="4x3" — side panel always suppressed) ───────
    private static void ApplyRetail4x3(ThermalLabelPayload p, EngineHand engineHand)
    {
        ApplyRetail45x3(p, engineHand);
        p.SidePanelBlock = string.Empty;
    }

    // ── 2x7.25 (dtlbl060d.i) ─────────────────────────────────────────────────
    private static void ApplyRetail2x725(ThermalLabelPayload p)
    {
        p.SidePanelBlock = p.PanelType switch
        {
            3 => BuildIconStack(p, "\x1BH0150\x1BV1230", "\x1BH0150\x1BV1335", "\x1BH0045\x1BV1335",
                                 "\x1BH0260\x1BV1335", "\x1BH0260\x1BV1230", "\x1BCC1\x1B%1\x1BA3H000V0100"),
            2 when !string.IsNullOrWhiteSpace(p.CustomerPartNumber) =>
                $"\x1B%0\x1BH0025\x1BV1260{(p.CustomerPartNumber.Length > 14 ? "\x1BP2" : "\x1BP6")}\x1BL0102\x1BWB1{p.CustomerPartNumber}\x1BH0015\x1BV1245\x1B(390,085",
            1 when !string.IsNullOrWhiteSpace(p.BrandName) =>
                $"\x1B%0\x1BH0025\x1BV1260{(p.BrandName.Length > 14 ? "\x1BP2" : "\x1BP6")}\x1BL0102\x1BWB1{p.BrandName}\x1BH0015\x1BV1245\x1B(390,085",
            _ => string.Empty,
        };

        p.GradeHighlightBlock = string.Empty; // dtlbl060d.i has no gr-highlight box

        p.CaseBarcodeBlock = (!string.IsNullOrWhiteSpace(p.CaseShippingCode) && p.PanelType != 3)
            ? $"\x1B%0\x1BH041\x1BV0175\x1BFW03H246\x1BH043\x1BV0175\x1BBD201100{p.CaseShippingCode}\x1BH041\x1BV0275\x1BFW03H246\x1BH053\x1BV0282\x1BL0102\x1BS{p.CaseShippingCode}"
            : "\x1B%0";

        p.QtyWeightBlock = p.Quantity == p.LisQty
            ? $"\x1BH215\x1BV0680\x1BL0101\x1BM{FormatQty(p.SalesQty)} {p.SalesUom}\x1BH220\x1BV0845\x1BL0101\x1BSCoverage\x1BH190\x1BV0680\x1BL0101\x1BM{FormatQty(p.PackageWeight)} LBS\x1BH165\x1BV0680\x1BL0101\x1BM{p.LisQty} {p.LisDesc}"
            : $"\x1BH165\x1BV0680\x1BL0101\x1BM{p.Quantity} {p.LisDesc}";
    }

    // ── 2x6 (dtlbl060r.i) — icon stack always shown except CustomerChar="L", which gets its own
    // CPN box instead; ColorDesc/highlight-box swap for CustomerChar B/F; no PanelType 1/2 panels. ──
    private static void ApplyRetail2x6(ThermalLabelPayload p)
    {
        if ((p.CustomerChar == "B" || p.CustomerChar == "F") && !string.IsNullOrWhiteSpace(p.CustomerPartNumber))
        {
            p.CustomerHighlightBlock = "\x1BH285\x1BV0280\x1B(125,030";
            p.ColorDescDisplay = p.CustomerPartNumber + ClipAscii(p.ColorDesc, 20);
        }

        var tmp = new StringBuilder();
        if (p.Pei is >= 1 and <= 4)
            tmp.Append("\x1BH0150\x1BV1010\x1BCC1\x1BGR").Append(p.Pei switch { 1 => "007", 2 => "008", 3 => "009", _ => "010" });
        var waIcon = p.Wa switch { 0.5m => "018", 3.0m => "011", 3.5m => "012", 5.0m => "013", 16.0m => "014", _ => null };
        if (waIcon != null) tmp.Append("\x1BH0150\x1BV1115\x1BCC1\x1BGR").Append(waIcon);
        if (string.Equals(p.TypeOfTile, "flt", StringComparison.OrdinalIgnoreCase) &&
            (p.CustomerChar == "H" || p.CustomerChar == "L"))
        {
            var cofIcon = p.Cof <= 0 ? null : p.Cof <= 0.42m ? "004" : p.Cof >= 0.60m ? "006" : "005";
            if (cofIcon != null) tmp.Append("\x1BH0045\x1BV1115\x1BCC1\x1BGR").Append(cofIcon);
        }
        if (string.Equals(p.Tone, "Y", StringComparison.OrdinalIgnoreCase))
            tmp.Append("\x1BH0260\x1BV1115\x1BCC1\x1BGR003");
        if (tmp.Length > 0)
            tmp.Append("\x1BH0260\x1BV1010\x1BCC1\x1BGR").Append(p.Grade == 2 ? "002" : "001");

        p.SidePanelBlock = p.CustomerChar == "L"
            ? string.Empty
            : $"\x1B%1\x1BA3H000V0090{tmp}\x1B%0\x1BA3H000V0000";

        p.GradeHighlightBlock = string.Empty; // dtlbl060r.i has no gr-highlight box

        p.CaseBarcodeBlock = (!string.IsNullOrWhiteSpace(p.CaseShippingCode) && p.PanelType != 3)
            ? $"\x1B%0\x1BH041\x1BV0145\x1BFW03H246\x1BH043\x1BV0145\x1BBD201085{p.CaseShippingCode}\x1BH053\x1BV0233\x1BL0102\x1BS{p.CaseShippingCode}"
            : string.Empty;

        var qtyBlock = p.Quantity == p.LisQty
            ? $"\x1BH215\x1BV0680\x1BL0101\x1BM{FormatQty(p.SalesQty)} {p.SalesUom}\x1BH220\x1BV0845\x1BL0101\x1BSCoverage\x1BH190\x1BV0680\x1BL0101\x1BM{FormatQty(p.PackageWeight)} LBS\x1BH165\x1BV0680\x1BL0101\x1BM{p.LisQty} {p.LisDesc}"
            : $"\x1BH165\x1BV0680\x1BL0101\x1BM{p.Quantity} {p.LisDesc}";
        if (p.CustomerChar == "L" && !string.IsNullOrWhiteSpace(p.CustomerPartNumber))
            qtyBlock += $"\x1B%0\x1BH020\x1BV1040\x1BL0102\x1BWL1{p.CustomerPartNumber}\x1BH010\x1BV1020\x1B(365,120";
        p.QtyWeightBlock = qtyBlock;
    }

    // ── 3x4.5 (dtlbl065d.i, 90°-rotated port of 060b) ───────────────────────
    private static void ApplyRetail3x45(ThermalLabelPayload p)
    {
        if ((p.CustomerChar == "L" || p.CustomerChar == "F" || p.CustomerChar == "B") &&
            !string.IsNullOrWhiteSpace(p.CustomerPartNumber) && p.PanelType == 1)
        {
            p.CustomerHighlightBlock = "\x1BH239\x1BV892\x1B(170,045";
            p.ColorDescDisplay = p.CustomerPartNumber + ClipAscii(p.ColorDesc, 20);
        }

        var tmp = p.PanelType switch
        {
            3 => BuildIconStack(p, "\x1BH610\x1BV015", "\x1BH490\x1BV015", "\x1BH380\x1BV015",
                                 "\x1BH270\x1BV015", "\x1BH160\x1BV015", "\x1BCC1\x1B%3"),
            2 when !string.IsNullOrWhiteSpace(p.CustomerPartNumber) =>
                $"\x1B%2\x1BH555\x1BV103\x1BL0408\x1BS{p.CustomerPartNumber}\x1BH605\x1BV112\x1B(595,128",
            1 when !string.IsNullOrWhiteSpace(p.BrandName) =>
                $"\x1B%2\x1BH595\x1BV098\x1BL0203\x1BWB1{p.BrandName}\x1B%2\x1BH615\x1BV103\x1B(595,095",
            _ => string.Empty,
        };
        p.SidePanelBlock = tmp.Length > 0 ? tmp + "\x1B%1" : string.Empty;

        p.GradeHighlightBlock = p.GradeHighlight ? "\x1BH282\x1BV0198\x1B(070,028" : string.Empty;

        p.CaseBarcodeBlock = (!string.IsNullOrWhiteSpace(p.CaseShippingCode) && p.PanelType != 3)
            ? $"\x1BH497\x1BV873\x1BFW03H487\x1BH497\x1BV871\x1BBD202075{p.CaseShippingCode}\x1BH570\x1BV873\x1BFW03H487\x1BH582\x1BV798\x1BL0102\x1BS{p.CaseShippingCode}"
            : string.Empty;

        p.QtyWeightBlock = p.Quantity == p.LisQty
            ? $"\x1BH352\x1BV378\x1BL0102\x1BSCoverage\x1BH357\x1BV288\x1BL0101\x1BM{FormatQty(p.SalesQty)} {p.SalesUom}\x1BH382\x1BV288\x1BL0101\x1BM{FormatQty(p.PackageWeight)} LBS\x1BH407\x1BV288\x1BL0101\x1BM{p.LisQty} {p.LisDesc}"
            : $"\x1BH407\x1BV273\x1BL0101\x1BM{p.Quantity} {p.LisDesc}";
    }

    // ── 1.75x8.38 (dtlbl060o.i) ──────────────────────────────────────────────
    private static void ApplyRetail175x838(ThermalLabelPayload p)
    {
        p.SidePanelBlock = p.PanelType switch
        {
            3 => BuildIconStack(p, "\x1BH0123\x1BV1463", "\x1BH0123\x1BV1570", "\x1BH0020\x1BV1570",
                                 "\x1BH0227\x1BV1570", "\x1BH0227\x1BV1463", "\x1BCC1\x1B%1\x1BA3H000V0100", "\x1BA3H000V0000"),
            1 when !string.IsNullOrWhiteSpace(p.BrandName) => BuildSidePanelSizedText(p.BrandName),
            2 when !string.IsNullOrWhiteSpace(p.CustomerPartNumber) => BuildSidePanelSizedText(p.CustomerPartNumber),
            _ => string.Empty,
        };

        p.GradeHighlightBlock = string.Empty; // dtlbl060o.i has no gr-highlight box

        p.CaseBarcodeBlock = (!string.IsNullOrWhiteSpace(p.CaseShippingCode) && p.PanelType != 3)
            ? $"\x1B%0\x1BH042\x1BV0015\x1BFW03H246\x1BH045\x1BV0015\x1BBD201100{p.CaseShippingCode}\x1BH042\x1BV0115\x1BFW03H246\x1BH045\x1BV0120\x1BL0102\x1BS{p.CaseShippingCode}"
            : string.Empty;

        p.QtyWeightBlock = p.Quantity == p.LisQty
            ? $"\x1BH115\x1BV1185\x1BL0102\x1BXSCoverage\x1BH110\x1BV1275\x1BL0102\x1BXS{FormatQty(p.SalesQty)} {p.SalesUom}\x1BH075\x1BV1275\x1BL0102\x1BXS{FormatQty(p.PackageWeight)} LBS\x1BH040\x1BV1275\x1BL0102\x1BXS{p.LisQty} {p.LisDesc}"
            : $"\x1BH075\x1BV1275\x1BL0102\x1BXS{p.Quantity} {p.LisDesc}";

        // dtlbl060o.i's 3-tier length-based sizing (P2/P3/P6, 11/14-char breakpoints) for the
        // PanelType 1/2 side text, used only by this size.
        static string BuildSidePanelSizedText(string text)
        {
            var sizeCmd = text.Length <= 11 ? "\x1BP2\x1BL0101\x1BWL1"
                        : text.Length <= 14 ? "\x1BP3\x1BL0206\x1BS"
                        : "\x1BP2\x1BL0102\x1BWB1";
            return $"\x1B%0\x1BH010\x1BV1450{sizeCmd}{text}\x1BH001\x1BV1440\x1B(350,100";
        }
    }

    /// <summary>
    /// Decodes the item reference (IRef) encoded in a 30-char carton barcode — the inverse of
    /// ComputeCartonBarcodeSerial's iref field (chars 9-14). Mirrors Progress dtscn011.p's
    /// <c>pip-iref = int(substr(pip-data-stream,9,6))</c> decode.
    ///
    /// This barcode has no per-carton uniqueness guarantee in either the legacy system or this
    /// port — it's a scannable item/shade/size/date descriptor, not a unique identifier (legacy
    /// never matches a scanned barcode back to a specific production record; it decodes the
    /// item directly, the same way this method is used). Returns null if the string isn't a
    /// well-formed 30-char carton barcode.
    /// </summary>
    internal static int? TryDecodeCartonBarcodeIRef(string? barcode)
    {
        if (string.IsNullOrEmpty(barcode) || barcode.Length != 30 || barcode[0] != '%')
            return null;

        return int.TryParse(barcode.Substring(8, 6), out var iref) ? iref : null;
    }

    /// <summary>
    /// Builds the "yjjj:hhmm" manufacture date/time code used on the Progress dtlbl060b.i label
    /// (prt-yjjj = last digit of 2-digit year + 3-digit julian day, followed by 24h HHmm).
    /// </summary>
    internal static string ComputeMfgDateCode(DateTime createdAtUtc)
    {
        var localTime = createdAtUtc.ToLocalTime();
        var yLastDigit = localTime.Year % 10;
        var julian = localTime.DayOfYear.ToString("000");
        return $"{yLastDigit}{julian}:{localTime:HHmm}";
    }

    /// <summary>
    /// Builds the "yjjj" mfg-date code with no time suffix (Progress prt-yjjj as used standalone,
    /// e.g. dtlbl060d.i's CrossOver date field: "~033M" + prt-yjjj + ":") — same year/julian
    /// formula as ComputeMfgDateCode, just without ":HHmm" appended.
    /// </summary>
    internal static string ComputeMfgDateCodeShort(DateTime createdAtUtc)
    {
        var localTime = createdAtUtc.ToLocalTime();
        var yLastDigit = localTime.Year % 10;
        var julian = localTime.DayOfYear.ToString("000");
        return $"{yLastDigit}{julian}";
    }

    /// <summary>
    /// Splits ItemNumber into a 4-char and 11-char run (Progress substr(prt-item-nbr,1,4) /
    /// substr(prt-item-nbr,5,11)) for templates that print the item number across two
    /// separately-positioned fields instead of one contiguous run — same 15-char pad/clip as
    /// FormatPalletSkuField, just returned as two pieces instead of one space-joined string.
    /// </summary>
    internal static (string Part1, string Part2) SplitItemNumber(string? itemNumber)
    {
        var padded = (itemNumber ?? string.Empty).PadRight(15);
        var value = padded.Length > 15 ? padded[..15] : padded;
        return (value[..4], value[4..15]);
    }

    /// <summary>
    /// Maps a single-char CustomerChar to the customer-number constant bc_cpn.csv's CustNbr column
    /// is keyed on (Progress dtplc067.p's GetCpn procedure, lines ~1297-1325). "H" is deliberately
    /// excluded — the real source's own outer guard skips the CPN lookup for it entirely (the "H"
    /// branch inside is dead/unreachable code, kept there only for documentation). Any char outside
    /// L/B/D/F returns empty, meaning "don't attempt a CPN lookup at all" (matches Progress's own
    /// else-branch: prt-cust-nbr = "").
    /// </summary>
    internal static string ResolveCpnCustomerNumber(string? customerChar) => customerChar?.Trim().ToUpperInvariant() switch
    {
        "L" => "74035",     // ws-lowes-cn
        "B" => "DALT1001",  // falls through to ws-menards-cn (the else branch)
        "D" or "F" => "FD001", // ws-f&d-cn
        _ => string.Empty,
    };

    /// <summary>
    /// PackageWeight (lb) converted to kg, formatted " (X.XXkg)" with a leading space (Progress
    /// dtlbl007n.i:349 <c>prt-pkg-wgt-met = itemdet.id-pkg-wgt * 0.453592</c>, inline-formatted the
    /// same way in every F&amp;D dtplbXXX.i file).
    /// </summary>
    internal static string ComputePackageWeightMetricFormatted(decimal packageWeight) =>
        $" ({FormatQty(packageWeight * 0.453592m)}kg)";

    /// <summary>
    /// SalesQty (sqft) converted to sqm and formatted "(X.XXsqm)" when SalesUom is "SF" — else a
    /// single space (Progress dtlbl007n.i:351-360, prt-met-coverage: <c>id-sales-qty / 10.764</c>).
    /// No leading space, unlike ComputePackageWeightMetricFormatted -- legacy is inconsistent about
    /// this between the two fields and this reproduces both exactly as each one really is.
    /// </summary>
    internal static string ComputeCoverageMetricFormatted(decimal salesQty, string? salesUom) =>
        string.Equals(salesUom?.Trim(), "SF", StringComparison.OrdinalIgnoreCase)
            ? $"({FormatQty(salesQty / 10.764m)}sqm)"
            : " ";

    /// <summary>
    /// F&D 4.5x3's per-model base reference-move offset (dtplb003.i's 3-way ws-dev-model branch,
    /// the only F&D size with real per-hand coordinate variance — everything below it shares one
    /// field layout via "ESC %2", just shifted by this one H-offset). Matches Progress's exact
    /// string rules (raw device-model codes like "M-8400RV"/"S86LD"/"85XL"), not this port's
    /// PrinterModelCatalog marketing-name→EngineHand mapping, which classifies differently and
    /// has no entries matching these raw codes at all. None of today's cataloged models (M84Pro,
    /// M8485SE, S86NX, S86EX) match the S86L/85L or S86R/85X prefixes, so this defaults to the
    /// "M84-family" branch — a real, legitimately-coded branch in dtplb003.i, not a fabrication;
    /// still checks the real rules first in case a station's PrinterModel is ever set to a raw
    /// device-model code that does match.
    /// </summary>
    internal static string ComputeFndReferenceMoveOffset(string? printerModel)
    {
        var model = (printerModel ?? string.Empty).Trim().ToUpperInvariant();

        if (model.Length >= 4 && (model[..4] == "S86L" || model == "85L" || model[..Math.Min(4, model.Length)] == "85XL"))
            return "H0215V0000";

        if (model.Length >= 4 && (model[..4] == "S86R" || model == "85R" || model[..Math.Min(3, model.Length)] == "85X"))
            return "H0000V0000";

        return "H0420V0000"; // "M-8400RV" / "84VL" branch, and the default for unmatched models
    }

    /// <summary>
    /// F&D 3x4.5's per-model LabelVarB footer positioning command (dtplb008.i/dtplb008I.i's
    /// 4-way <c>index(ws-dev-model, "...")</c> substring branch — same caveat as
    /// ComputeFndReferenceMoveOffset about raw device-model codes vs this port's PrinterModel
    /// catalog; defaults to the "84-family" branch, matching Progress's own else-default.
    /// </summary>
    internal static string ComputeFnd3x45Footer(string? printerModel)
    {
        var model = (printerModel ?? string.Empty).Trim().ToUpperInvariant();

        if (model.Contains("S86", StringComparison.Ordinal))
            return "\x02\x1BA\x1BA114241340\x1BZ\x03";
        if (model.Contains("S84", StringComparison.Ordinal))
            return "\x02\x1BA\x1BA114240832\x1BZ\x03";
        if (model.Contains("85", StringComparison.Ordinal))
            return "\x02\x1BA\x1BA114241024\x1BZ\x03";

        return "\x02\x1BA\x1BA114240832\x1BZ\x03"; // 84-family default (84VL, M84Pro, 8400RVe, ...)
    }

    /// <summary>
    /// Manufacturing 4.5x3's per-model base reference-move offset (dtmlb001.i's 2-way
    /// ws-dev-model branch — the only Manufacturing size with real per-hand coordinate
    /// variance, everything below it shares one field layout via "ESC %2"). Unlike F&D's
    /// raw-device-model checks, this one collapses cleanly onto the existing EngineHand
    /// classification: dtmlb001.i's "right" group (S86RD/85R/85xrbd) is exactly EngineHand.Right,
    /// and its "left" group (85L/85xlbd/S86LD) is exactly "not Right" — it doesn't distinguish
    /// Left85L from LeftS86LD the way Standard Retail does, both get the same offset here.
    /// </summary>
    internal static string ComputeMfg45x3ReferenceMove(EngineHand hand) =>
        hand == EngineHand.Right ? "H-17V0005" : "H115V0015";

    /// <summary>
    /// Manufacturing 4.5x3's grade-highlight box position (dtmlb001.i's gr-highlight branch) —
    /// only this size has one; the other 5 Manufacturing sizes never reference gr-highlight at
    /// all. Source's exact grouping is a little inconsistent (S86LD — a "left" hand for the
    /// reference-move offset above — shares the "right" box position here, only 85L/85xlbd get
    /// the second position), which doesn't cleanly collapse onto EngineHand.Right/not-Right;
    /// simplified to follow the same Right/not-Right split as the reference move, since none of
    /// today's cataloged models match any of these raw device-model strings anyway.
    /// </summary>
    internal static string ComputeMfg45x3GradeHighlightBlock(bool gradeHighlight, EngineHand hand)
    {
        if (!gradeHighlight)
            return string.Empty;

        return hand == EngineHand.Right ? "\x1BH170\x1BV0294\x1B(070,028" : "\x1BH142\x1BV0312\x1B(070,028";
    }

    // ── Mexico Store (MEXICO-ONLY flag, mitemdet/mitemhdr) ───────────────────
    // Only dtlbl066n.i's S86-engine 4.5x3 layout is ported; dtlbl066m.i (3x4.5) and
    // dtlbl060p.i (1.75x8.38) are not yet built. See project memory
    // feature_mexico_label_research_20260923 for the full field-by-field source.

    /// <summary>
    /// Dispatches to the per-size Mexico field-computation method. Only "45x3" is implemented
    /// today — other sizes fall through with no Mexico-specific fields populated.
    /// </summary>
    internal static void ApplyMexicoFields(ThermalLabelPayload p, string sizeToken)
    {
        switch (sizeToken)
        {
            case "45x3": ApplyMexico45x3(p); break;
        }
    }

    // ── Mexico 4.5x3 (dtlbl066n.i, S86 engine branch only) ───────────────────
    private static void ApplyMexico45x3(ThermalLabelPayload p)
    {
        p.MexicoBarcodeSerial = ComputeMexicoBarcodeSerial(p);
        p.MexicoBarcodeCommand = ComputeCartonBarcodeCommand(p.MexicoBarcodeSerial);
        p.MexicoLotCode = ComputeMexicoLotCode(p);
        p.MexicoQtyString = ComputeMexicoQtyString(p);
        p.MexicoToneCalbrBlock = ComputeMexicoToneCalbrBlock(p);
        p.MexicoIconBlock = ComputeMexicoIconBlock(p);
    }

    /// <summary>
    /// The Mexico 30-byte barcode (dtlbl066n.i/066m.i/060p.i, all three identical): same field
    /// layout as ComputeCartonBarcodeSerial but with an "M" prefix instead of "%".
    /// </summary>
    internal static string ComputeMexicoBarcodeSerial(ThermalLabelPayload p)
    {
        var localTime = p.CreatedAtUtc.ToLocalTime();
        var shadeInt = int.TryParse(DigitsOnly(p.Shade, 10, "0"), out var sv) ? sv : 0;
        var sizeCode = string.IsNullOrWhiteSpace(p.Caliber) ? p.Size : p.Caliber;
        return ComputeCartonBarcodeSerial(localTime, p.IRef, shadeInt, sizeCode, p.Shift, p.LineNumber, p.Plant, p.LisQty, prefix: 'M');
    }

    /// <summary>
    /// The Mexico "Lot:" field — not a real lot number. Progress
    /// substr(w-shade-x,2,2) + string(prt-size,"9"): the middle 2 digits of the 4-digit shade2
    /// code, plus a single size/caliber digit (dtlbl066n.i:326-327).
    /// </summary>
    internal static string ComputeMexicoLotCode(ThermalLabelPayload p)
    {
        var shadeInt = int.TryParse(DigitsOnly(p.Shade, 10, "0"), out var sv) ? sv : 0;
        var shade2 = shadeInt < 1000 ? shadeInt * 10 : shadeInt;
        var shade2Str = shade2.ToString("0000");
        var middleTwo = shade2Str.Substring(1, 2);
        var sizeCode = string.IsNullOrWhiteSpace(p.Caliber) ? p.Size : p.Caliber;
        var sizeDigit = int.TryParse(DigitsOnly(sizeCode, 1, "0"), out var szv) ? szv : 0;
        return $"{middleTwo}{sizeDigit}";
    }

    /// <summary>
    /// Spanish "Contenido ..." quantity string (dtlbl066n.i prt-qty-str, lines 114-125): full
    /// sales-qty/weight/LIS breakdown when the carton quantity matches LisQty, or a short
    /// carton-qty-only form when it was overridden ("prt-qty-chg").
    /// </summary>
    internal static string ComputeMexicoQtyString(ThermalLabelPayload p)
    {
        if (p.Quantity == p.LisQty)
            return $"Contenido {FormatQty(p.SalesQty)} {p.SalesUom}/ {FormatQty(p.PackageWeight)} KG/ {p.LisQty} {p.LisDesc}";
        return $"Contenido {p.Quantity} {p.LisDesc}";
    }

    /// <summary>
    /// SBPL "Tono/Calbr:" line (dtlbl066n.i:297-303) — empty when shade2 is 0, matching the
    /// source's "if prt-shade2 gt 0" guard.
    /// </summary>
    internal static string ComputeMexicoToneCalbrBlock(ThermalLabelPayload p)
    {
        var shadeInt = int.TryParse(DigitsOnly(p.Shade, 10, "0"), out var sv) ? sv : 0;
        var shade2 = shadeInt < 1000 ? shadeInt * 10 : shadeInt;
        if (shade2 <= 0)
            return string.Empty;

        var sizeCode = string.IsNullOrWhiteSpace(p.Caliber) ? p.Size : p.Caliber;
        var szChar = string.IsNullOrWhiteSpace(sizeCode) ? "0" : sizeCode.Trim()[..1];
        return $"\x1BH0310\x1BV0402\x1BL0101\x1BSTono/Calbr:\x1BH0310\x1BV0387\x1BL0101\x1BWB0{shade2:0000} {szChar}";
    }

    /// <summary>
    /// PEI/WA/COF/Tone/grade icon stack for the Mexico 4.5x3 label (dtlbl066n.i:154-206) — same
    /// icon graphics/coordinates as Standard Retail's PanelType=3 stack (see BuildIconStack), but
    /// the COF icon is additionally gated on CustomerChar H/L (mid-cust-char = "H" or "L", lines
    /// 178-185) and compares exact COF values (.10/.42) rather than BuildIconStack's threshold
    /// bands — real behavioral differences from the shared Standard Retail helper, not
    /// coincidental overlap, so this is its own implementation rather than a BuildIconStack call.
    /// </summary>
    internal static string ComputeMexicoIconBlock(ThermalLabelPayload p)
    {
        var tmp = new StringBuilder();

        var peiIcon = p.Pei switch { 1 => "GR007", 2 => "GR008", 3 => "GR009", 4 => "GR010", _ => null };
        if (peiIcon != null) tmp.Append("\x1BH0032\x1BV0010\x1B").Append(peiIcon);

        var waIcon = p.Wa switch { 0.5m => "GR018", 3.0m => "GR011", 3.5m => "GR012", 5.0m => "GR013", 16.0m => "GR014", _ => null };
        if (waIcon != null) tmp.Append("\x1BH0032\x1BV0130\x1B").Append(waIcon);

        if (p.CustomerChar is "H" or "L")
        {
            if (p.Cof == 0.10m) tmp.Append("\x1BH0032\x1BV0240\x1BCC1\x1BGR004");
            else if (p.Cof == 0.42m) tmp.Append("\x1BH0032\x1BV0240\x1BCC1\x1BGR005");
        }

        if (string.Equals(p.Tone, "Y", StringComparison.OrdinalIgnoreCase))
            tmp.Append("\x1BH0032\x1BV0350\x1BGR003");

        if (tmp.Length == 0)
            return string.Empty;

        tmp.Append(p.Grade == 2 ? "\x1BH0032\x1BV0460\x1BGR002\x1BH0032\x1BV0460\x1BGR002" : "\x1BH0032\x1BV0460\x1BGR001");
        return "\x1BCC1\x1B%0" + tmp;
    }

    /// <summary>
    /// Applies the Progress dtplc067_prep.p item-number display mask, format picture
    /// "xxxx  xxxxxxxxxxx" (first 4 characters, two literal spaces, next 11 characters -
    /// space-padded/truncated to fit), e.g. seen at dtplc067_prep.p:6714 and :8384.
    /// </summary>
    internal static string MaskItemNumber(string? itemNumber)
    {
        var padded = (itemNumber ?? string.Empty).PadRight(15);
        var value = padded.Length > 15 ? padded[..15] : padded;
        return $"{value[..4]}  {value[4..]}";
    }

    /// <summary>Clips a field to a max length so it doesn't run into the label's second column.</summary>
    internal static string ClipField(string? value, int maxLength) => ClipAscii(value ?? string.Empty, maxLength);

    private static string NormalizeType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "SATO";

        return value.Trim().ToLowerInvariant() switch
        {
            "sato" => "SATO",
            "ipl" => "IPL",
            "zpl" => "ZPL",
            "fingerprint" => "Fingerprint",
            _ => "SATO",
        };
    }

    private static string ClipAscii(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var asciiChars = value.Select(c => c <= 127 ? c : '?').ToArray();
        var ascii = new string(asciiChars).Trim();
        return ascii.Length <= maxLength ? ascii : ascii[..maxLength];
    }

    private static string Quote(string value) => '"' + value.Replace("\"", "") + '"';

    private static string BuildDescriptionLine(ThermalLabelPayload p)
    {
        var color = string.IsNullOrWhiteSpace(p.ColorDesc) ? string.Empty : p.ColorDesc;
        var shape = string.IsNullOrWhiteSpace(p.ShapeDesc) ? string.Empty : p.ShapeDesc;
        var series = string.IsNullOrWhiteSpace(p.SeriesDesc) ? string.Empty : p.SeriesDesc;
        var joined = string.Join(" | ", new[] { color, shape, series }.Where(v => !string.IsNullOrWhiteSpace(v)));

        if (!string.IsNullOrWhiteSpace(joined))
            return joined;

        return p.PartDescription;
    }

    private static string DigitsOnly(string value, int maxLen, string fallback)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
            return fallback;

        return digits.Length <= maxLen ? digits.PadLeft(maxLen, '0') : digits[^maxLen..];
    }

    private static string ResolveBusinessLabelTypeName(int labelTypeCode)
    {
        return labelTypeCode switch
        {
            0 => "DALTILE",
            1 => "TYPE1_PENDING",
            2 => "LOWES",
            3 => "HOME DEPOT",
            4 => "TYPE4_PENDING",
            5 => "TYPE5_PENDING",
            6 => "TYPE6_PENDING",
            _ => $"TYPE{labelTypeCode:00}",
        };
    }
}
