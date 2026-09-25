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

    /// <summary>
    /// "host:port" of a Moxa NPort (TCP Server mode) the Pallet Scan screen connects out to for
    /// scanner input. Blank keeps the older behavior of listening on <see cref="PlcPort"/> for a
    /// scanner that dials in. A devices.csv S-SCAN row for the line still takes precedence.
    /// </summary>
    public string PalletScanAddress { get; set; } = string.Empty;

    /// <summary>"NiceLabel Xml", "NetworkPrinter", "SerialPort", or "HttpPost"</summary>
    public string LabelOutputType { get; set; } = "NiceLabel Xml";

    /// <summary>Thermal language type for direct printer output.</summary>
    public string ThermalPrinterType { get; set; } = "SATO";

    /// <summary>
    /// This station's physical printer model (e.g. "M84Pro", "S86NX") — determines which
    /// reference-point/coordinate variant a label template uses, matching Progress's
    /// ws-dev-model-driven branching. See PrinterModelCatalog for the model-to-hand mapping
    /// (data/printer_models.csv).
    /// </summary>
    public string PrinterModel { get; set; } = "M84Pro";

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

    /// <summary>This station's id within the plant (e.g. "A", "B") — matches the
    /// "Station A / Station B" pairing in the rollout map. Used as part of the data-lake
    /// document key so multiple stations at the same plant don't collide.</summary>
    public string StationId { get; set; } = "A";

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

    // ── Oracle interface DB (WMSOra/dcssfc schema holder — see OracleInterfaceService) ────
    // Off by default (blank Host) — StandAlone doesn't use this for anything on its own yet;
    // it's here so the connection can be configured and tested without a rebuild once a real
    // use for it (e.g. a specific table read/write) is decided.
    public string OracleHost { get; set; } = string.Empty;
    public int OraclePort { get; set; } = 1521;
    public string OracleServiceName { get; set; } = string.Empty;
    public string OracleUsername { get; set; } = string.Empty;

    /// <summary>
    /// Stored in plaintext in carton-settings.json, same as every other setting here — there
    /// is no credential protection. Do not point this at a production Oracle account unless
    /// that's an accepted risk; prefer a low-privilege test-DB login.
    /// </summary>
    public string OraclePassword { get; set; } = string.Empty;

    /// <summary>
    /// "OracleDirect" or "BizTalk" — which vehicle carries the outbound SAP backflush
    /// (EolScanForm's SendToSapAsync) onward. Per-plant, not a fixed choice: standard
    /// plants today are Progress bcmstr3 → Oracle → SAP ("OracleDirect", calling the
    /// same p_insert_interface_receipt/p_insert_interface_inv_update procedures
    /// sendprod-orawms.p calls, via OracleInterfaceService), while El Paso has no
    /// Oracle database at all and goes BizTalk → SAP directly ("BizTalk" —
    /// FileSapIntegrationService today; no real BizTalk connectivity in this port
    /// yet). The Oracle hop is expected to go away for every plant eventually, so
    /// default to "BizTalk" — it's both what El Paso (the current target plant)
    /// actually needs and the direction every other plant is headed.
    /// </summary>
    public string BackflushVehicle { get; set; } = "BizTalk";

    // ── Centralized MongoDB data lake (see DataLakeService) ────────────────────────────
    // On by default against a local instance — every station writes station/hardware/config
    // plus carton and pallet status history one-way into this database. Not a dependency:
    // if it's unreachable, prints and scans still work, they just don't get logged there.
    public string MongoConnectionString { get; set; } = "mongodb://localhost:27017";
    public string MongoDatabaseName { get; set; } = "StandAloneDataLake";
}
