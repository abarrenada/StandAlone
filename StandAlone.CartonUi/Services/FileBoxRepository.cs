using StandAlone.CartonUi.Models;

namespace StandAlone.CartonUi.Services;

/// <summary>
/// File-based implementation of <see cref="IBoxRepository"/>.
/// Reads CSV files written by the sorter PLC process.
///
/// Expected file formats (in <c>dataDirectory</c>):
///   boxes{NN}.csv   — RecId,LineId,MakeTime,StackNum,PlcMsg,ErrMsg,PrintNum[,BarcodeSerial]
///   stackers{NN}.csv — LineId,StackNum,IRef,PlcMsg,Shade,Size,ErrMsg
///   itemdet.csv     — 43-column format with all itemdet + itemhdr fields (see ItemDetail.cs)
///   mitemdet.csv    — 43-column format for Mexico items (same structure as itemdet.csv)
///
/// Lines starting with '#' are treated as comments.
/// Supports backward-compatible 3-column format (IRef,ItemNumber,LisQty) by detecting column count.
/// </summary>
public class FileBoxRepository : IBoxRepository
{
    private readonly string _dataDirectory;
    private readonly string _palletsCsvPath;

    /// <param name="dataDirectory">Local data directory for box/stacker/item CSVs.</param>
    /// <param name="palletsCsvPath">
    /// Full path to the pallet registry CSV. Pass a shared network path so multiple
    /// stations (pallet printing, EOL scanning) see the same registry. Defaults to
    /// "pallets.csv" under <paramref name="dataDirectory"/> when null/empty.
    /// </param>
    public FileBoxRepository(string dataDirectory, string? palletsCsvPath = null)
    {
        _dataDirectory = dataDirectory;
        _palletsCsvPath = string.IsNullOrWhiteSpace(palletsCsvPath)
            ? Path.Combine(dataDirectory, "pallets.csv")
            : palletsCsvPath;
    }

    public Task<List<BoxRecord>> GetLastBoxesAsync(int lineId, int count, CancellationToken ct)
    {
        var filePath = Path.Combine(_dataDirectory, $"boxes{lineId:00}.csv");
        var result = new List<BoxRecord>();
        if (!File.Exists(filePath))
            return Task.FromResult(result);

        var lines = File.ReadAllLines(filePath);
        foreach (var line in lines.Reverse())
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;
            var parts = line.Split(',');
            if (parts.Length < 7) continue;

            result.Add(new BoxRecord
            {
                RecId         = int.TryParse(parts[0].Trim(), out var rid) ? rid : 0,
                LineId        = int.TryParse(parts[1].Trim(), out var lid) ? lid : lineId,
                MakeTime      = DateTime.TryParse(parts[2].Trim(), out var mt) ? mt : DateTime.MinValue,
                StackNum      = parts[3].Trim(),
                PlcMsg        = parts[4].Trim(),
                ErrMsg        = parts[5].Trim(),
                PrintNum      = int.TryParse(parts[6].Trim(), out var pn) ? pn : 0,
                BarcodeSerial = parts.Length >= 8 ? parts[7].Trim() : string.Empty,
            });
            if (result.Count >= count) break;
        }
        return Task.FromResult(result);
    }

    /// <summary>
    /// Atomically increments and returns the next pallet serial for the plant. Uses an
    /// exclusive file lock (FileShare.None) around the whole read-modify-write, mirroring
    /// Progress's "find first serial exclusive-lock ... serial.ipsn = serial.ipsn + 1 ...
    /// release" atomic increment — needed because multiple stations (e.g. Pallet Query and
    /// Pallet Scan, possibly on different PCs sharing this data directory) can call this for
    /// the same plant at nearly the same instant; an unlocked read-then-write here would let
    /// two callers read the same counter value and mint the same "unique" serial twice.
    /// </summary>
    public async Task<int> AllocatePalletSerialAsync(int plant, CancellationToken ct)
    {
        var filePath = Path.Combine(_dataDirectory, $"pallet-serial-{plant:000}.txt");
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        const int maxAttempts = 20;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

                var serial = 0;
                if (stream.Length > 0)
                {
                    var buffer = new byte[stream.Length];
                    var read = await stream.ReadAsync(buffer, ct);
                    int.TryParse(System.Text.Encoding.ASCII.GetString(buffer, 0, read).Trim(), out serial);
                }
                serial = serial >= 999_999_999 ? 1 : serial + 1;

                var bytes = System.Text.Encoding.ASCII.GetBytes(serial.ToString());
                stream.SetLength(0);
                stream.Position = 0;
                await stream.WriteAsync(bytes, ct);
                await stream.FlushAsync(ct);

                return serial;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                // Locked by another concurrent allocation (this station or another one on a
                // shared drive) — brief backoff and retry rather than risk two callers ever
                // reading the same counter value.
                await Task.Delay(25 * attempt, ct);
            }
        }

        throw new IOException($"Could not allocate a pallet serial for plant {plant:000} — the counter file stayed locked after {maxAttempts} attempts.");
    }

    public Task SavePalletRecordAsync(PalletRecord record, CancellationToken ct)
    {
        var filePath = _palletsCsvPath;
        var line = string.Join(',',
            record.PalletId,
            record.Plant,
            record.ItemNumber,
            SanitizeCsvField(record.ColorDesc),
            SanitizeCsvField(record.ShapeDesc),
            SanitizeCsvField(record.SeriesDesc),
            record.LisQty,
            record.BoxesPerPallet,
            record.Shade,
            record.Size,
            SanitizeCsvField(record.ShopOrder),
            record.Grade,
            record.LineNumber,
            record.Shift,
            SanitizeCsvField(record.Inspector),
            record.PrintedAtUtc.ToString("O"));

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.AppendAllText(filePath, line + Environment.NewLine);
        return Task.CompletedTask;
    }

    public Task<PalletRecord?> GetPalletBySerialAsync(string palletId, CancellationToken ct)
    {
        var filePath = _palletsCsvPath;
        if (!File.Exists(filePath) || string.IsNullOrWhiteSpace(palletId))
            return Task.FromResult<PalletRecord?>(null);

        var needle = palletId.Trim();
        foreach (var line in File.ReadAllLines(filePath).Reverse())
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            var parts = line.Split(',');
            if (parts.Length < 16) continue;
            if (!string.Equals(parts[0].Trim(), needle, StringComparison.OrdinalIgnoreCase)) continue;

            return Task.FromResult<PalletRecord?>(new PalletRecord
            {
                PalletId       = parts[0].Trim(),
                Plant          = int.TryParse(parts[1].Trim(), out var pl) ? pl : 0,
                ItemNumber     = parts[2].Trim(),
                ColorDesc      = parts[3].Trim(),
                ShapeDesc      = parts[4].Trim(),
                SeriesDesc     = parts[5].Trim(),
                LisQty         = int.TryParse(parts[6].Trim(), out var lq) ? lq : 0,
                BoxesPerPallet = int.TryParse(parts[7].Trim(), out var bp) ? bp : 0,
                Shade          = parts[8].Trim(),
                Size           = parts[9].Trim(),
                ShopOrder      = parts[10].Trim(),
                Grade          = int.TryParse(parts[11].Trim(), out var gr) ? gr : 0,
                LineNumber     = int.TryParse(parts[12].Trim(), out var ln) ? ln : 0,
                Shift          = int.TryParse(parts[13].Trim(), out var sf) ? sf : 0,
                Inspector      = parts[14].Trim(),
                PrintedAtUtc   = DateTime.TryParse(parts[15].Trim(), out var pt) ? pt : DateTime.MinValue,
            });
        }
        return Task.FromResult<PalletRecord?>(null);
    }

    public Task AppendEolScanAsync(EolScanRecord record, CancellationToken ct)
    {
        var filePath = Path.Combine(_dataDirectory, "eol-scans.csv");
        var line = string.Join(',',
            record.PalletId,
            record.Plant,
            record.ItemNumber,
            SanitizeCsvField(record.Description),
            SanitizeCsvField(record.ShopOrder),
            record.LisQty,
            record.BoxesPerPallet,
            record.ConfirmedQty,
            record.Shift,
            record.LineNumber,
            SanitizeCsvField(record.Inspector),
            record.ScanTimeUtc.ToString("O"),
            record.SapSuccess,
            SanitizeCsvField(record.SapDetail));

        File.AppendAllText(filePath, line + Environment.NewLine);
        return Task.CompletedTask;
    }

    public Task<List<EolScanRecord>> GetLastEolScansAsync(int count, CancellationToken ct)
    {
        var filePath = Path.Combine(_dataDirectory, "eol-scans.csv");
        var result = new List<EolScanRecord>();
        if (!File.Exists(filePath))
            return Task.FromResult(result);

        foreach (var line in File.ReadAllLines(filePath).Reverse())
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            var parts = line.Split(',');
            if (parts.Length < 14) continue;

            result.Add(new EolScanRecord
            {
                PalletId       = parts[0].Trim(),
                Plant          = int.TryParse(parts[1].Trim(), out var pl) ? pl : 0,
                ItemNumber     = parts[2].Trim(),
                Description    = parts[3].Trim(),
                ShopOrder      = parts[4].Trim(),
                LisQty         = int.TryParse(parts[5].Trim(), out var lq) ? lq : 0,
                BoxesPerPallet = int.TryParse(parts[6].Trim(), out var bp) ? bp : 0,
                ConfirmedQty   = int.TryParse(parts[7].Trim(), out var cq) ? cq : 0,
                Shift          = int.TryParse(parts[8].Trim(), out var sf) ? sf : 0,
                LineNumber     = int.TryParse(parts[9].Trim(), out var ln) ? ln : 0,
                Inspector      = parts[10].Trim(),
                ScanTimeUtc    = DateTime.TryParse(parts[11].Trim(), out var st) ? st : DateTime.MinValue,
                SapSuccess     = bool.TryParse(parts[12].Trim(), out var ss) && ss,
                SapDetail      = parts[13].Trim(),
            });
            if (result.Count >= count) break;
        }
        return Task.FromResult(result);
    }

    public Task<EolScanRecord?> FindEolScanByPalletIdAsync(string palletId, CancellationToken ct)
    {
        var filePath = Path.Combine(_dataDirectory, "eol-scans.csv");
        if (!File.Exists(filePath) || string.IsNullOrWhiteSpace(palletId))
            return Task.FromResult<EolScanRecord?>(null);

        var needle = palletId.Trim();
        foreach (var line in File.ReadAllLines(filePath).Reverse())
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            var parts = line.Split(',');
            if (parts.Length < 14) continue;
            if (!string.Equals(parts[0].Trim(), needle, StringComparison.OrdinalIgnoreCase)) continue;

            return Task.FromResult<EolScanRecord?>(new EolScanRecord
            {
                PalletId       = parts[0].Trim(),
                Plant          = int.TryParse(parts[1].Trim(), out var pl) ? pl : 0,
                ItemNumber     = parts[2].Trim(),
                Description    = parts[3].Trim(),
                ShopOrder      = parts[4].Trim(),
                LisQty         = int.TryParse(parts[5].Trim(), out var lq) ? lq : 0,
                BoxesPerPallet = int.TryParse(parts[6].Trim(), out var bp) ? bp : 0,
                ConfirmedQty   = int.TryParse(parts[7].Trim(), out var cq) ? cq : 0,
                Shift          = int.TryParse(parts[8].Trim(), out var sf) ? sf : 0,
                LineNumber     = int.TryParse(parts[9].Trim(), out var ln) ? ln : 0,
                Inspector      = parts[10].Trim(),
                ScanTimeUtc    = DateTime.TryParse(parts[11].Trim(), out var st) ? st : DateTime.MinValue,
                SapSuccess     = bool.TryParse(parts[12].Trim(), out var ss) && ss,
                SapDetail      = parts[13].Trim(),
            });
        }
        return Task.FromResult<EolScanRecord?>(null);
    }

    private static string SanitizeCsvField(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Replace(',', ';').Replace('\n', ' ').Replace('\r', ' ');

    public Task<BoxRecord?> GetBoxByBarcodeSerialAsync(int lineId, string barcodeSerial, CancellationToken ct)
    {
        var filePath = Path.Combine(_dataDirectory, $"boxes{lineId:00}.csv");
        if (!File.Exists(filePath) || string.IsNullOrWhiteSpace(barcodeSerial))
            return Task.FromResult<BoxRecord?>(null);

        var needle = barcodeSerial.Trim();
        foreach (var line in File.ReadAllLines(filePath).Reverse())
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            var parts = line.Split(',');
            if (parts.Length < 8) continue;
            if (!string.Equals(parts[7].Trim(), needle, StringComparison.OrdinalIgnoreCase)) continue;

            return Task.FromResult<BoxRecord?>(new BoxRecord
            {
                RecId         = int.TryParse(parts[0].Trim(), out var rid) ? rid : 0,
                LineId        = int.TryParse(parts[1].Trim(), out var lid) ? lid : lineId,
                MakeTime      = DateTime.TryParse(parts[2].Trim(), out var mt) ? mt : DateTime.MinValue,
                StackNum      = parts[3].Trim(),
                PlcMsg        = parts[4].Trim(),
                ErrMsg        = parts[5].Trim(),
                PrintNum      = int.TryParse(parts[6].Trim(), out var pn) ? pn : 0,
                BarcodeSerial = parts[7].Trim(),
            });
        }
        return Task.FromResult<BoxRecord?>(null);
    }

    private const string StackersHeader = "Stacker,Item Number,Qty,Shade,Size";

    /// <summary>True for the header row ("Stacker,Item Number,Qty,Shade,Size") or any comment/blank line.</summary>
    private static bool IsStackersNonDataLine(string line) =>
        string.IsNullOrWhiteSpace(line) || line.StartsWith('#') ||
        line.TrimStart().StartsWith("Stacker,", StringComparison.OrdinalIgnoreCase);

    private static StackerRecord? ParseStackerLine(string line, int lineId)
    {
        var parts = line.Split(',');
        if (parts.Length < 5) return null;

        return new StackerRecord
        {
            LineId     = lineId,
            StackNum   = parts[0].Trim(),
            ItemNumber = parts[1].Trim(),
            Qty        = int.TryParse(parts[2].Trim(), out var q) ? q : 0,
            Shade      = int.TryParse(parts[3].Trim(), out var sh) ? sh : 0,
            Size       = parts[4].Trim(),
        };
    }

    public Task<StackerRecord?> GetStackerAsync(int lineId, string stackNum, CancellationToken ct)
    {
        var filePath = Path.Combine(_dataDirectory, $"stackers{lineId:00}.csv");
        if (!File.Exists(filePath))
            return Task.FromResult<StackerRecord?>(null);

        var needle = stackNum.Trim();
        foreach (var line in File.ReadAllLines(filePath))
        {
            if (IsStackersNonDataLine(line)) continue;
            var record = ParseStackerLine(line, lineId);
            if (record != null && record.StackNum == needle)
                return Task.FromResult<StackerRecord?>(record);
        }
        return Task.FromResult<StackerRecord?>(null);
    }

    public Task<List<StackerRecord>> GetAllStackersAsync(int lineId, CancellationToken ct)
    {
        var filePath = Path.Combine(_dataDirectory, $"stackers{lineId:00}.csv");
        var result = new List<StackerRecord>();
        if (!File.Exists(filePath))
            return Task.FromResult(result);

        foreach (var line in File.ReadAllLines(filePath))
        {
            if (IsStackersNonDataLine(line)) continue;
            var record = ParseStackerLine(line, lineId);
            if (record != null)
                result.Add(record);
        }
        return Task.FromResult(result);
    }

    public Task SaveStackerAsync(StackerRecord record, CancellationToken ct)
    {
        var filePath = Path.Combine(_dataDirectory, $"stackers{record.LineId:00}.csv");
        var kept = new List<string>();
        var sawHeader = false;

        if (File.Exists(filePath))
        {
            foreach (var line in File.ReadAllLines(filePath))
            {
                if (IsStackersNonDataLine(line))
                {
                    if (line.TrimStart().StartsWith("Stacker,", StringComparison.OrdinalIgnoreCase))
                        sawHeader = true;
                    kept.Add(line);
                    continue;
                }

                var parsed = ParseStackerLine(line, record.LineId);
                if (parsed == null || parsed.StackNum != record.StackNum.Trim())
                    kept.Add(line);
                // else: this is the old row for the same StackNum — drop it, the fresh row
                // is appended below.
            }
        }

        if (!sawHeader)
            kept.Insert(0, StackersHeader);

        kept.Add(string.Join(',',
            record.StackNum,
            SanitizeCsvField(record.ItemNumber),
            record.Qty,
            record.Shade,
            record.Size));

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllLines(filePath, kept);
        return Task.CompletedTask;
    }

    public Task DeleteStackerAsync(int lineId, string stackNum, CancellationToken ct)
    {
        var filePath = Path.Combine(_dataDirectory, $"stackers{lineId:00}.csv");
        if (!File.Exists(filePath))
            return Task.CompletedTask;

        var needle = stackNum.Trim();
        var kept = new List<string>();
        foreach (var line in File.ReadAllLines(filePath))
        {
            if (IsStackersNonDataLine(line)) { kept.Add(line); continue; }
            var parsed = ParseStackerLine(line, lineId);
            if (parsed == null || parsed.StackNum != needle)
                kept.Add(line);
        }

        File.WriteAllLines(filePath, kept);
        return Task.CompletedTask;
    }

    public Task<string?> GetItemDisplayAsync(int iRef, bool isMexicoItem, int shade, string size,
        bool w4DigitShade, CancellationToken ct)
    {
        var fileName = isMexicoItem ? "mitemdet.csv" : "itemdet.csv";
        var filePath = Path.Combine(_dataDirectory, fileName);
        if (!File.Exists(filePath))
            return Task.FromResult<string?>(null);

        foreach (var line in File.ReadAllLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            var parts = line.Split(',');
            if (parts.Length < 3) continue;
            if (!int.TryParse(parts[0].Trim(), out var id) || id != iRef) continue;

            var itemNumber = parts[1].Trim();
            var lisQty = int.TryParse(parts[2].Trim(), out var q) ? q : 0;

            // Progress: w-4digitshade = shade as 4 digits; else shade * 10 as 4 digits
            var shadeStr = w4DigitShade
                ? shade.ToString("0000")
                : (shade * 10).ToString("0000");

            return Task.FromResult<string?>($"{itemNumber.TrimEnd()}-{lisQty}-{shadeStr}-{size}");
        }
        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Looks up complete ItemDetail by IRef from itemdet or mitemdet CSV.
    /// Returns null if not found or file doesn't exist.
    /// </summary>
    public Task<ItemDetail?> GetItemDetailByIRefAsync(int iRef, bool isMexicoItem,
        CancellationToken ct)
    {
        var fileName = isMexicoItem ? "mitemdet.csv" : "itemdet.csv";
        var filePath = Path.Combine(_dataDirectory, fileName);
        if (!File.Exists(filePath))
            return Task.FromResult<ItemDetail?>(null);

        foreach (var line in File.ReadAllLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            var parts = line.Split(',');
            if (parts.Length < 3) continue; // Need at least IRef, ItemNumber, LisQty
            if (!int.TryParse(parts[0].Trim(), out var id) || id != iRef)
                continue;

            return Task.FromResult<ItemDetail?>(ParseItemDetail(parts));
        }
        return Task.FromResult<ItemDetail?>(null);
    }

    /// <summary>
    /// Looks up complete ItemDetail by ItemNumber from itemdet CSV.
    /// Searches US items first, then Mexico items if enabled.
    /// Returns null if not found in either file.
    /// </summary>
    public async Task<ItemDetail?> GetItemDetailByNumberAsync(string itemNumber,
        bool searchMexicoAlso, CancellationToken ct)
    {
        // First try US items
        var usItem = await SearchItemByNumberAsync("itemdet.csv", itemNumber, ct);
        if (usItem != null)
            return usItem;

        // Then try Mexico items if requested
        if (searchMexicoAlso)
        {
            var mexItem = await SearchItemByNumberAsync("mitemdet.csv", itemNumber, ct);
            if (mexItem != null)
                return mexItem;
        }

        return null;
    }

    /// <summary>
    /// Looks up every ItemDetail row matching ItemNumber (an item can have more than one row,
    /// differing only by Lis Qty). Searches US items first, then Mexico items if enabled.
    /// </summary>
    public async Task<List<ItemDetail>> GetAllItemDetailsByNumberAsync(string itemNumber,
        bool searchMexicoAlso, CancellationToken ct)
    {
        var results = await SearchAllItemsByNumberAsync("itemdet.csv", itemNumber, ct);

        if (searchMexicoAlso)
            results.AddRange(await SearchAllItemsByNumberAsync("mitemdet.csv", itemNumber, ct));

        return results;
    }

    /// <summary>
    /// Internal helper: searches CSV file for every row matching ItemNumber.
    /// </summary>
    private Task<List<ItemDetail>> SearchAllItemsByNumberAsync(string fileName, string itemNumber,
        CancellationToken ct)
    {
        var filePath = Path.Combine(_dataDirectory, fileName);
        var results = new List<ItemDetail>();
        if (!File.Exists(filePath))
            return Task.FromResult(results);

        var searchTerm = itemNumber.Trim();
        foreach (var line in File.ReadAllLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            var parts = line.Split(',');
            if (parts.Length < 2) continue;
            if (parts[1].Trim() == searchTerm)
                results.Add(ParseItemDetail(parts));
        }
        return Task.FromResult(results);
    }

    /// <summary>
    /// Internal helper: searches CSV file by ItemNumber.
    /// </summary>
    private Task<ItemDetail?> SearchItemByNumberAsync(string fileName, string itemNumber,
        CancellationToken ct)
    {
        var filePath = Path.Combine(_dataDirectory, fileName);
        if (!File.Exists(filePath))
            return Task.FromResult<ItemDetail?>(null);

        var searchTerm = itemNumber.Trim();
        foreach (var line in File.ReadAllLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            var parts = line.Split(',');
            if (parts.Length < 2) continue;

            // ItemNumber is in column 1 (0-indexed)
            if (parts[1].Trim() == searchTerm)
                return Task.FromResult<ItemDetail?>(ParseItemDetail(parts));
        }
        return Task.FromResult<ItemDetail?>(null);
    }

    /// <summary>
    /// Parses a CSV line into an ItemDetail object.
    /// Handles both 43-column format (new) and 3-column format (legacy).
    /// 
    /// New format: IRef,ItemNumber,LisQty,SalesQty,SalesUOM,...ColorDesc,ShapeDesc,SeriesDesc... (43 cols)
    /// Legacy format: IRef,ItemNumber,LisQty (3 cols)
    /// </summary>
    private ItemDetail ParseItemDetail(string[] parts)
    {
        var detail = new ItemDetail();

        // Columns always present (IRef, ItemNumber, LisQty)
        if (parts.Length >= 1)
            detail.IRef = int.TryParse(parts[0].Trim(), out var ir) ? ir : 0;
        if (parts.Length >= 2)
            detail.ItemNumber = parts[1].Trim();
        if (parts.Length >= 3)
            detail.LisQty = int.TryParse(parts[2].Trim(), out var lq) ? lq : 0;

        // If only 3 columns (legacy format), return early
        if (parts.Length == 3)
            return detail;

        // Parse new 43-column format (itemdet fields)
        if (parts.Length >= 4)
            detail.SalesQty = decimal.TryParse(parts[3].Trim(), out var sq) ? sq : 0;
        if (parts.Length >= 5)
            detail.SalesUOM = parts[4].Trim();
        if (parts.Length >= 6)
            detail.PkgWeight = decimal.TryParse(parts[5].Trim(), out var pw) ? pw : 0;
        if (parts.Length >= 7)
            detail.CartonUPC_NumSys = int.TryParse(parts[6].Trim(), out var cns) ? cns : 0;
        if (parts.Length >= 8)
            detail.CartonUPC_Mfg = int.TryParse(parts[7].Trim(), out var cm) ? cm : 0;
        if (parts.Length >= 9)
            detail.CartonUPC_Prod = int.TryParse(parts[8].Trim(), out var cp) ? cp : 0;
        if (parts.Length >= 10)
            detail.CartonUPC_Chkdgt = int.TryParse(parts[9].Trim(), out var cc) ? cc : 0;
        if (parts.Length >= 11)
            detail.CustomerChar = parts[10].Trim();
        if (parts.Length >= 12)
            detail.Grade = int.TryParse(parts[11].Trim(), out var gr) ? gr : 0;
        if (parts.Length >= 13)
            detail.PkgIndicator = int.TryParse(parts[12].Trim(), out var pi) ? pi : 0;
        if (parts.Length >= 14)
            detail.CardPrinter = parts[13].Trim();
        if (parts.Length >= 15)
            detail.LISDescription = parts[14].Trim();
        if (parts.Length >= 16)
            detail.Shade = int.TryParse(parts[15].Trim(), out var sh) ? sh : 0;
        if (parts.Length >= 17)
            detail.BoxesPerPallet = int.TryParse(parts[16].Trim(), out var bp) ? bp : 0;
        if (parts.Length >= 18)
            detail.NeedPalletLabel = parts[17].Trim();
        if (parts.Length >= 19)
            detail.Company = parts[18].Trim();
        if (parts.Length >= 20)
            detail.Status = parts[19].Trim();
        if (parts.Length >= 21)
            detail.Extract = parts[20].Trim();

        // Parse itemhdr fields (columns 21+)
        if (parts.Length >= 22)
            detail.ColorDesc = parts[21].Trim();
        if (parts.Length >= 23)
            detail.ShapeDesc = parts[22].Trim();
        if (parts.Length >= 24)
            detail.SeriesDesc = parts[23].Trim();
        if (parts.Length >= 25)
            detail.Brand = parts[24].Trim();
        if (parts.Length >= 26)
            detail.TypeOfTile = parts[25].Trim();
        if (parts.Length >= 27)
            detail.ColorId = parts[26].Trim();
        if (parts.Length >= 28)
            detail.SizeShape = parts[27].Trim();
        if (parts.Length >= 29)
            detail.WmsUOM = parts[28].Trim();
        if (parts.Length >= 30)
            detail.Plant = int.TryParse(parts[29].Trim(), out var pl) ? pl : 0;
        if (parts.Length >= 31)
            detail.ProductType = parts[30].Trim();
        if (parts.Length >= 32)
            detail.SingleUPC_NumSys = int.TryParse(parts[31].Trim(), out var sns) ? sns : 0;
        if (parts.Length >= 33)
            detail.SingleUPC_Mfg = int.TryParse(parts[32].Trim(), out var smfg) ? smfg : 0;
        if (parts.Length >= 34)
            detail.SingleUPC_Prod = int.TryParse(parts[33].Trim(), out var sprod) ? sprod : 0;
        if (parts.Length >= 35)
            detail.SingleUPC_Chkdgt = int.TryParse(parts[34].Trim(), out var scc) ? scc : 0;
        if (parts.Length >= 36)
            detail.PEI = int.TryParse(parts[35].Trim(), out var pei) ? pei : 0;
        if (parts.Length >= 37)
            detail.WA = decimal.TryParse(parts[36].Trim(), out var wa) ? wa : 0;
        if (parts.Length >= 38)
            detail.COF = decimal.TryParse(parts[37].Trim(), out var cof) ? cof : 0;
        if (parts.Length >= 39)
            detail.Tone = parts[38].Trim();
        if (parts.Length >= 40)
            detail.CreateUser = parts[39].Trim();
        if (parts.Length >= 41)
            detail.UpdateUser = parts[40].Trim();
        if (parts.Length >= 42)
            detail.CreateDate = parts[41].Trim();
        if (parts.Length >= 43)
            detail.UpdateDate = parts[42].Trim();
        if (parts.Length >= 44)
            detail.LabelTypeCode = int.TryParse(parts[43].Trim(), out var ltc) ? ltc : 0;
        if (parts.Length >= 45)
            detail.LastScheduleOrder = parts[44].Trim();
        if (parts.Length >= 46)
            detail.OpenQty = decimal.TryParse(parts[45].Trim(), out var oq) ? oq : 0;
        if (parts.Length >= 47)
            detail.ScheduleDate = parts[46].Trim();

        return detail;
    }
}
