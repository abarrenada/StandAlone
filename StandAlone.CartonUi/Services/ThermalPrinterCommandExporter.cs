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
            var archivePath = ResolveArchivePath(payload.CreatedAtUtc);

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

    private string ResolveArchivePath(DateTime createdAtUtc)
    {
        var suffix = createdAtUtc.ToLocalTime().ToString("yyyyMMdd_HHmmss_fff");
        var fileName = $"thermal_{NormalizeToken(_thermalPrinterType)}_{suffix}.txt";
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
        var shadeInt = int.TryParse(DigitsOnly(p.Shade, 10, "0"), out var sv) ? sv : 0;
        var sizeCode = string.IsNullOrWhiteSpace(p.Caliber) ? p.Size : p.Caliber;
        var bc128 = ComputeCartonBarcodeSerial(localTime, p.IRef, shadeInt, sizeCode, p.Shift, p.LineNumber, p.Plant, p.LisQty);
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
            .Append("XM").Append(ClipAscii($"SHADE: {p.Shade}   SIZE: {p.Size}", 40)).AppendLine();
        sb.Append(esc).Append("H0180").Append(esc).Append("V0235").Append(esc)
            .Append("XM").Append(ClipAscii(descLine, 48)).AppendLine();

        sb.Append(esc).Append("H0180").Append(esc).Append("V0280").Append(esc)
            .Append("XM").Append(ClipAscii($"QTY/CTN: {p.LisQty:00000}   SALES: {p.SalesQty:0.##} {p.SalesUom}", 48)).AppendLine();
        sb.Append(esc).Append("H0180").Append(esc).Append("V0325").Append(esc)
            .Append("XM").Append(ClipAscii($"WT: {p.PackageWeight:0.#} LB   LINE: {p.LineNumber:00} SHIFT: {p.Shift}", 48)).AppendLine();
        sb.Append(esc).Append("H0180").Append(esc).Append("V0370").Append(esc)
            .Append("XM").Append(ClipAscii($"INSP: {p.Inspector}  STACK: {p.StackNumber}  SIZECD: {p.LabelSize}", 48)).AppendLine();
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
        // Matches Progress dtlbl101b.i (WMS 4x6 pallet label) field-for-field.
        const char stx = (char)0x02;
        const char etx = (char)0x03;
        const char esc = (char)0x1B;

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

        // Total pallet pieces: LisQty × BoxesPerPallet  (Progress: prt-nbr-pc, displayed as ZZ,ZZ9)
        var totalPcs = (long)p.LisQty * Math.Max(0, p.BoxesPerPallet);

        // prt-yyyyjjj = YYYY + JJJ (7 chars)
        var mfgDate = $"{localTime.Year:0000}{localTime.DayOfYear:000}";

        // prt-pkgconfig = "QQQQQP BBBBBBC"  (pieces-per-box "P " boxes-per-pallet "C")
        var pkgConfig = $"{p.LisQty:00000}P {p.BoxesPerPallet:00000}C";

        // Plant name capped at 15 chars (substr(prt-plant-name,1,15))
        var plantName = string.IsNullOrWhiteSpace(p.PlantName)
            ? string.Empty
            : p.PlantName[..Math.Min(15, p.PlantName.Length)];

        var sb = new StringBuilder();
        sb.Append(stx);
        sb.Append(esc).Append("A").AppendLine();
        sb.Append(esc).Append("CS2").AppendLine();
        sb.Append(esc).Append("Q").Append(Math.Clamp(p.Quantity, 1, 999)).AppendLine();

        // ── Tag barcode: Code 128, wide, top of label ─────────────────────────
        if (!string.IsNullOrWhiteSpace(tagBarcode))
        {
            sb.Append(esc).Append("H0080").Append(esc).Append("V0025").Append(esc)
                .Append("B103120").Append(esc).Append("D").Append(ClipAscii(tagBarcode, 12)).AppendLine();
        }

        // ── Tag: PPP-SSSSSSSSS ────────────────────────────────────────────────
        sb.Append(esc).Append("H0220").Append(esc).Append("V0025").Append(esc)
            .Append("L0101").Append(esc).Append("XB0").Append("Tag:").AppendLine();
        sb.Append(esc).Append("H0220").Append(esc).Append("V0110").Append(esc)
            .Append("L0201").Append(esc).Append("WL0").Append(ClipAscii(tagDisplay, 20)).AppendLine();

        // ── SKU ───────────────────────────────────────────────────────────────
        sb.Append(esc).Append("H0320").Append(esc).Append("V0025").Append(esc)
            .Append("L0101").Append(esc).Append("XB0").Append("SKU:").AppendLine();
        sb.Append(esc).Append("H0320").Append(esc).Append("V0110").Append(esc)
            .Append("L0202").Append(esc).Append("WL0").Append(ClipAscii(p.ItemNumber, 24)).AppendLine();

        // ── Shd: (left) + Plant: (right) ─────────────────────────────────────
        sb.Append(esc).Append("H0420").Append(esc).Append("V0025").Append(esc)
            .Append("L0101").Append(esc).Append("XB0").Append("Shd:").AppendLine();
        sb.Append(esc).Append("H0420").Append(esc).Append("V0110").Append(esc)
            .Append("L0201").Append(esc).Append("WL0").Append(ClipAscii($"{shade4}{sizeCode}", 6)).AppendLine();
        sb.Append(esc).Append("H0420").Append(esc).Append("V0380").Append(esc)
            .Append("L0101").Append(esc).Append("XB0")
            .Append(ClipAscii($"Plant: {p.Plant:000} - {plantName}", 28)).AppendLine();

        // ── Qty: | Grade: | Shift: | Line: (same row) ─────────────────────────
        sb.Append(esc).Append("H0500").Append(esc).Append("V0025").Append(esc)
            .Append("L0101").Append(esc).Append("XB0")
            .Append(ClipAscii($"Qty: {totalPcs:#,##0}", 14)).AppendLine();
        sb.Append(esc).Append("H0500").Append(esc).Append("V0250").Append(esc)
            .Append("L0101").Append(esc).Append("XB0")
            .Append(ClipAscii($"Grade: {p.Grade}", 10)).AppendLine();
        sb.Append(esc).Append("H0500").Append(esc).Append("V0390").Append(esc)
            .Append("L0101").Append(esc).Append("XB0")
            .Append(ClipAscii($"Shift: {p.Shift}", 10)).AppendLine();
        sb.Append(esc).Append("H0500").Append(esc).Append("V0530").Append(esc)
            .Append("L0101").Append(esc).Append("XB0")
            .Append(ClipAscii($"Line: {p.LineNumber:00}", 10)).AppendLine();

        // ── pkgconfig | Loc: | MfgDate: (same row) ────────────────────────────
        sb.Append(esc).Append("H0580").Append(esc).Append("V0025").Append(esc)
            .Append("L0101").Append(esc).Append("XB0").Append(ClipAscii(pkgConfig, 18)).AppendLine();
        sb.Append(esc).Append("H0580").Append(esc).Append("V0250").Append(esc)
            .Append("L0101").Append(esc).Append("XB0")
            .Append(ClipAscii($"Loc: {p.Location}", 16)).AppendLine();
        sb.Append(esc).Append("H0580").Append(esc).Append("V0430").Append(esc)
            .Append("L0101").Append(esc).Append("XB0")
            .Append(ClipAscii($"MfgDate: {mfgDate}", 18)).AppendLine();

        sb.Append(esc).Append("Z");
        sb.Append(etx);
        return sb.ToString();
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
        sb.AppendLine($"A20,80,0,3,1,1,N,\"SHADE {ClipAscii(p.Shade, 8)} SIZE {ClipAscii(p.Size, 8)}\"");
        sb.AppendLine($"A20,110,0,3,1,1,N,\"SHIFT {p.Shift} INSPECTOR {ClipAscii(p.Inspector, 16)}\"");
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
        sb.AppendLine($"^FO40,120^A0N,28,28^FDSHADE {ClipAscii(p.Shade, 8)}  SIZE {ClipAscii(p.Size, 8)}^FS");
        sb.AppendLine($"^FO40,160^A0N,28,28^FDSHIFT {p.Shift}  INSPECTOR {ClipAscii(p.Inspector, 16)}^FS");
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
        sb.AppendLine($"PP 20,80:{Quote(ClipAscii($"SHADE {p.Shade} SIZE {p.Size}", 30))}");
        sb.AppendLine($"PP 20,110:{Quote(ClipAscii($"SHIFT {p.Shift} INSPECTOR {p.Inspector}", 36))}");
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
