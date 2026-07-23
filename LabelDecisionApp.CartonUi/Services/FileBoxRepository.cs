using LabelDecisionApp.CartonUi.Models;

namespace LabelDecisionApp.CartonUi.Services;

/// <summary>
/// File-based implementation of <see cref="IBoxRepository"/>.
/// Reads CSV files written by the sorter PLC process.
///
/// Expected file formats (in <c>dataDirectory</c>):
///   boxes{NN}.csv   — RecId,LineId,MakeTime,StackNum,PlcMsg,ErrMsg,PrintNum
///   stackers{NN}.csv — LineId,StackNum,IRef,PlcMsg,Shade,Size,ErrMsg
///   itemdet.csv     — IRef,ItemNumber,LisQty
///   mitemdet.csv    — IRef,ItemNumber,LisQty  (Mexico items)
///
/// Lines starting with '#' are treated as comments.
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
                RecId    = int.TryParse(parts[0].Trim(), out var rid) ? rid : 0,
                LineId   = int.TryParse(parts[1].Trim(), out var lid) ? lid : lineId,
                MakeTime = DateTime.TryParse(parts[2].Trim(), out var mt) ? mt : DateTime.MinValue,
                StackNum = parts[3].Trim(),
                PlcMsg   = parts[4].Trim(),
                ErrMsg   = parts[5].Trim(),
                PrintNum = int.TryParse(parts[6].Trim(), out var pn) ? pn : 0,
            });
            if (result.Count >= count) break;
        }
        return Task.FromResult(result);
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
            var lisQty     = int.TryParse(parts[2].Trim(), out var q) ? q : 0;

            // Progress: w-4digitshade = shade as 4 digits; else shade * 10 as 4 digits
            var shadeStr = w4DigitShade
                ? shade.ToString("0000")
                : (shade * 10).ToString("0000");

            return Task.FromResult<string?>($"{itemNumber.TrimEnd()}-{lisQty}-{shadeStr}-{size}");
        }
        return Task.FromResult<string?>(null);
    }
}
