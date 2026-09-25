using StandAlone.CartonUi.Models;

namespace StandAlone.CartonUi.Services;

/// <summary>
/// Per-line device wiring (terminal/scanner/printer — SER vs IP), read from data/devices.csv
/// (format: LineNumber,DeviceType,Ifr,Dev,Stty,Cmd,Model). This is the C# analog of Progress's
/// dev-detail table (dtmnt036.p) — that table's per-line/device-type IFR config is exported here
/// as a flat file instead of read live from the Progress database.
///
/// A (line, device type) with no row here has no override; callers fall back to their existing
/// single-device AppSettings fields, so an empty or partially-populated file changes nothing.
/// </summary>
public class LineDeviceCatalog
{
    private readonly string _filePath;

    public LineDeviceCatalog(string dataDirectory)
    {
        _filePath = Path.Combine(dataDirectory, "devices.csv");
    }

    /// <summary>Resolves a line + device type (e.g. "S-SCAN", "A-PTR", "TERM") to its wiring, or
    /// null if no row exists — callers should fall back to their existing settings-based defaults.</summary>
    public LineDeviceConfig? GetDevice(int lineNumber, string deviceType)
    {
        if (!File.Exists(_filePath))
            return null;

        foreach (var line in File.ReadAllLines(_filePath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith("LineNumber", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = line.Split(',');
            if (parts.Length < 4)
                continue;

            if (!int.TryParse(parts[0].Trim(), out var rowLine) || rowLine != lineNumber)
                continue;
            if (!string.Equals(parts[1].Trim(), deviceType, StringComparison.OrdinalIgnoreCase))
                continue;

            return new LineDeviceConfig
            {
                LineNumber = rowLine,
                DeviceType = parts[1].Trim(),
                Ifr        = parts[2].Trim(),
                Dev        = parts[3].Trim(),
                Stty       = parts.Length > 4 ? parts[4].Trim() : string.Empty,
                Cmd        = parts.Length > 5 ? parts[5].Trim() : string.Empty,
                Model      = parts.Length > 6 ? parts[6].Trim() : string.Empty,
            };
        }
        return null;
    }
}
