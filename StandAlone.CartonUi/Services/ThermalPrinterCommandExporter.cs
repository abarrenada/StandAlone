using StandAlone.CartonUi.Models;
using System.Net.Sockets;
using System.Text;

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
            var commandText = ThermalPrinterCommandBuilder.Build(_thermalPrinterType, payload);
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

public sealed class ThermalLabelPayload
{
    public string LabelFormat { get; init; } = "CARTON_LABEL";
    public int LabelTypeCode { get; init; }
    public string PalletId { get; init; } = string.Empty;
    public string ItemNumber { get; init; } = string.Empty;
    public int IRef { get; init; }
    public int Plant { get; init; }
    public string PartDescription { get; init; } = string.Empty;
    public string ColorDesc { get; init; } = string.Empty;
    public string ShapeDesc { get; init; } = string.Empty;
    public string SeriesDesc { get; init; } = string.Empty;
    public string LabelSize { get; init; } = string.Empty;
    public string StackNumber { get; init; } = string.Empty;
    public string Shade { get; init; } = string.Empty;
    public string Size { get; init; } = string.Empty;
    public int BoxesPerPallet { get; init; }
    public decimal SalesQty { get; init; }
    public string SalesUom { get; init; } = string.Empty;
    public decimal PackageWeight { get; init; }
    public string Inspector { get; init; } = string.Empty;
    public int Shift { get; init; }
    public int LineNumber { get; init; }
    public int Quantity { get; init; } = 1;
    public string UccBarcode { get; init; } = string.Empty;
    public string CartonUpc { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}

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
        //tony comment for test
        // Progress-style framing used in legacy SATO flows: STX + ESC A ... ESC Z + ETX.
        const char stx = (char)0x02;
        const char etx = (char)0x03;
        const char esc = (char)0x1B;

        var localTime = p.CreatedAtUtc.ToLocalTime();
        var yddd = localTime.ToString("yy") + localTime.DayOfYear.ToString("000");
        var hhmm = localTime.ToString("HHmm");
        var ctnQty = Math.Clamp(p.Quantity, 1, 9999).ToString("0000");
        var bcShade = DigitsOnly(p.Shade, 4, "0000");
        var bcLine = Math.Clamp(p.LineNumber, 0, 99).ToString("00");
        var bcPlant = Math.Clamp(p.Plant, 0, 999).ToString("000");
        var bc128 = $"%20{yddd}{Math.Clamp(p.IRef, 0, 999999):000000}{bcShade}01{Math.Clamp(p.Shift, 0, 9)}{bcLine}{bcPlant}{ctnQty}";
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
            .Append("XM").Append(ClipAscii($"QTY/CTN: {ctnQty}   SALES: {p.SalesQty:0.##} {p.SalesUom}", 48)).AppendLine();
        sb.Append(esc).Append("H0180").Append(esc).Append("V0325").Append(esc)
            .Append("XM").Append(ClipAscii($"WT: {p.PackageWeight:0.#} LB   LINE: {p.LineNumber:00} SHIFT: {p.Shift}", 48)).AppendLine();
        sb.Append(esc).Append("H0180").Append(esc).Append("V0370").Append(esc)
            .Append("XM").Append(ClipAscii($"INSP: {p.Inspector}  STACK: {p.StackNumber}  SIZECD: {p.LabelSize}", 48)).AppendLine();
        sb.Append(esc).Append("H0180").Append(esc).Append("V0415").Append(esc)
            .Append("XM").Append(ClipAscii($"DATE: {yddd}:{hhmm}  IREF: {p.IRef:000000}  PLANT: {bcPlant}", 48)).AppendLine();
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
        const char stx = (char)0x02;
        const char etx = (char)0x03;
        const char esc = (char)0x1B;

        var localTime = p.CreatedAtUtc.ToLocalTime();
        var dateCode = localTime.ToString("yy") + localTime.DayOfYear.ToString("000") + ":" + localTime.ToString("HHmm");
        var palletId = string.IsNullOrWhiteSpace(p.PalletId)
            ? $"PLT-{localTime:yyyyMMddHHmmss}"
            : p.PalletId;
        var upcBarcode = string.IsNullOrWhiteSpace(p.CartonUpc) ? p.ItemNumber : p.CartonUpc;

        var businessTypeName = ResolveBusinessLabelTypeName(p.LabelTypeCode);

        var sb = new StringBuilder();
        sb.Append(stx);
        sb.Append(esc).Append("A").AppendLine();
        sb.Append(esc).Append("CS2").AppendLine();
        sb.Append(esc).Append("Q").Append(Math.Clamp(p.Quantity, 1, 999)).AppendLine();

        sb.Append(esc).Append("H0180").Append(esc).Append("V0080").Append(esc)
            .Append("L0202").Append(esc).Append("XM").Append(ClipAscii($"{businessTypeName} PALLET LABEL", 32)).AppendLine();
        sb.Append(esc).Append("H0180").Append(esc).Append("V0145").Append(esc)
            .Append("XM").Append(ClipAscii($"PALLET ID: {palletId}", 44)).AppendLine();
        sb.Append(esc).Append("H0180").Append(esc).Append("V0190").Append(esc)
            .Append("XM").Append(ClipAscii($"ITEM: {p.ItemNumber}", 40)).AppendLine();
        sb.Append(esc).Append("H0180").Append(esc).Append("V0235").Append(esc)
            .Append("XM").Append(ClipAscii(BuildDescriptionLine(p), 48)).AppendLine();
        sb.Append(esc).Append("H0180").Append(esc).Append("V0280").Append(esc)
            .Append("XM").Append(ClipAscii($"BOXES/PALLET: {Math.Max(0, p.BoxesPerPallet)}", 40)).AppendLine();
        sb.Append(esc).Append("H0180").Append(esc).Append("V0325").Append(esc)
            .Append("XM").Append(ClipAscii($"PLANT: {p.Plant:000}  LINE: {p.LineNumber:00}  SHIFT: {p.Shift}", 48)).AppendLine();
        sb.Append(esc).Append("H0180").Append(esc).Append("V0370").Append(esc)
            .Append("XM").Append(ClipAscii($"DATE: {dateCode}  INSP: {p.Inspector}", 48)).AppendLine();

        sb.Append(esc).Append("H0180").Append(esc).Append("V0435").Append(esc)
            .Append("B103100").Append(esc).Append("D").Append(ClipAscii(palletId, 32)).AppendLine();
        sb.Append(esc).Append("H0680").Append(esc).Append("V0435").Append(esc)
            .Append("B103100").Append(esc).Append("D").Append(ClipAscii(upcBarcode, 20)).AppendLine();

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
