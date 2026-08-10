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

    public FileBoxRepository(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
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

    public Task<StackerRecord?> GetStackerAsync(int lineId, string stackNum, CancellationToken ct)
    {
        var filePath = Path.Combine(_dataDirectory, $"stackers{lineId:00}.csv");
        if (!File.Exists(filePath))
            return Task.FromResult<StackerRecord?>(null);

        foreach (var line in File.ReadAllLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            var parts = line.Split(',');
            if (parts.Length < 7) continue;

            if (!int.TryParse(parts[0].Trim(), out var lid) || lid != lineId) continue;
            if (parts[1].Trim() != stackNum) continue;

            return Task.FromResult<StackerRecord?>(new StackerRecord
            {
                LineId   = lid,
                StackNum = parts[1].Trim(),
                IRef     = int.TryParse(parts[2].Trim(), out var ir) ? ir : 0,
                PlcMsg   = parts[3].Trim(),
                Shade    = int.TryParse(parts[4].Trim(), out var sh) ? sh : 0,
                Size     = parts[5].Trim(),
                ErrMsg   = parts[6].Trim(),
            });
        }
        return Task.FromResult<StackerRecord?>(null);
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
