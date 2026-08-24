using StandAlone.CartonUi.Models;
using StandAlone.Integration.Services;
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

    public ThermalPrinterCommandExporter(string outputAddress, string fallbackDirectory, string thermalPrinterType)
    {
        _outputAddress = outputAddress ?? string.Empty;
        _fallbackDirectory = fallbackDirectory;
        _thermalPrinterType = string.IsNullOrWhiteSpace(thermalPrinterType) ? "SATO" : thermalPrinterType;
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
                var templatesDirectory = SatoTemplateResolver.FindTemplateDirectory();
                var templatePath = Path.Combine(templatesDirectory, templateName);
                if (File.Exists(templatePath))
                {
                    var template = File.ReadAllText(templatePath);
                    // Reuse an already-known barcode (e.g. a reprint's stored value) instead of
                    // recomputing from CreatedAtUtc, which would drift on a different calendar day.
                    if (string.IsNullOrWhiteSpace(payload.CartonBarcodeSerial))
                        payload.CartonBarcodeSerial = ThermalPrinterCommandBuilder.ComputeCartonBarcodeSerial(payload);
                    payload.MfgDateCode = ThermalPrinterCommandBuilder.ComputeMfgDateCode(payload.CreatedAtUtc);
                    payload.ItemNumberMasked = ThermalPrinterCommandBuilder.MaskItemNumber(payload.ItemNumber);
                    payload.PartDescriptionShort = ThermalPrinterCommandBuilder.ClipField(payload.PartDescription, 36);
                    payload.ShadeLotCode = ThermalPrinterCommandBuilder.ComputeShadeLotCode(payload);
                    payload.InspectorDisplay = ThermalPrinterCommandBuilder.ComputeInspectorDisplay(payload);
                    var rendered = SatoTemplateRenderer.RenderTemplate(template, payload);
                    return DecodeSatoControlMarkers(rendered);
                }
            }
        }

        return ThermalPrinterCommandBuilder.Build(_thermalPrinterType, payload);
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
        DateTime localTime, int iRef, int shade, string sizeCode, int shift, int lineId, int plant, int lisQty)
    {
        var year4  = localTime.Year.ToString("0000");
        var julday = localTime.DayOfYear.ToString("000");
        var shade2 = shade < 1000 ? shade * 10 : shade;
        var szCode = string.IsNullOrWhiteSpace(sizeCode) ? "0" : sizeCode.Trim()[..1];
        return $"%{year4}{julday}{Math.Clamp(iRef, 0, 999999):000000}{shade2:0000}{szCode}{Math.Clamp(shift, 0, 9)}{Math.Clamp(lineId, 0, 99):00}{Math.Clamp(plant, 0, 999):000}{Math.Clamp(lisQty, 0, 99999):00000}";
    }

    internal static string ComputeCartonBarcodeSerial(ThermalLabelPayload p)
    {
        var localTime = p.CreatedAtUtc.ToLocalTime();
        var shadeInt = int.TryParse(DigitsOnly(p.Shade, 10, "0"), out var sv) ? sv : 0;
        var sizeCode = string.IsNullOrWhiteSpace(p.Caliber) ? p.Size : p.Caliber;
        return ComputeCartonBarcodeSerial(localTime, p.IRef, shadeInt, sizeCode, p.Shift, p.LineNumber, p.Plant, p.LisQty);
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
