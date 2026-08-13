namespace StandAlone.CartonUi.Models;

/// <summary>
/// Persistent application settings (JSON-serializable).
/// </summary>
public class AppSettings
{
    public string BoxesCsvPath { get; set; } = string.Empty;
    public string StackersCsvPath { get; set; } = string.Empty;
    public string ItemdetCsvPath { get; set; } = string.Empty;
    public string MitemdetCsvPath { get; set; } = string.Empty;
    public int ProductionLineNumber { get; set; } = 1;

    /// <summary>"SerialPort" or "IP"</summary>
    public string PlcConnectionType { get; set; } = "SerialPort";

    /// <summary>COM port name (e.g., "COM1") or IP address.</summary>
    public string PlcAddress { get; set; } = "COM1";

    public int PlcBaudRate { get; set; } = 9600;

    /// <summary>TCP port to listen on when PlcConnectionType is "IP".</summary>
    public int PlcPort { get; set; } = 9000;

    /// <summary>"NiceLabel Xml", "NetworkPrinter", "SerialPort", or "HttpPost"</summary>
    public string LabelOutputType { get; set; } = "NiceLabel Xml";

    /// <summary>Thermal language type for direct printer output.</summary>
    public string ThermalPrinterType { get; set; } = "SATO";

    /// <summary>For HttpPost: target URL. For SerialPort: COM port + baud.</summary>
    public string LabelOutputAddress { get; set; } = string.Empty;

    /// <summary>"PLC Signal" or "Manual Qty".</summary>
    public string CartonPrintMode { get; set; } = "PLC Signal";

    public string MasterPasswordHash { get; set; } = string.Empty;

    /// <summary>Hashed with SHA256 for comparison.</summary>
    public static string HashPassword(string password)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(hash);
    }

    public bool VerifyPassword(string password) =>
        MasterPasswordHash == HashPassword(password);

    /// <summary>Last entered primary item (persisted across sessions).</summary>
    public string CurrentPrimaryItem { get; set; } = string.Empty;

    /// <summary>Last entered secondary item (persisted across sessions).</summary>
    public string CurrentSecondaryItem { get; set; } = string.Empty;

    /// <summary>Plant name printed on pallet labels (e.g., "El Paso", "Dallas").</summary>
    public string PlantName { get; set; } = string.Empty;

    /// <summary>Warehouse location code printed on pallet labels (e.g., "SHRWRAP").</summary>
    public string PalletLocation { get; set; } = "SHRWRAP";

    /// <summary>
    /// Full path to the pallet registry CSV (written on every pallet print, read by EOL
    /// scanning). Unlike the CSV path fields above, this one is actually read at runtime —
    /// point it at a shared network path so the printing station(s) and the EOL station
    /// all see the same registry. Leave empty to default to "pallets.csv" in the local
    /// data directory (single-station/testing use).
    /// </summary>
    public string PalletsCsvPath { get; set; } = string.Empty;
}
