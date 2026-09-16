namespace StandAlone.CartonUi.Models;

/// <summary>
/// One end-of-line scan transaction: a pallet serial confirmed as shipped/complete,
/// logged locally and sent to both SAP (production backflush) and WMS (warehouse
/// receipt). Modeled on the legacy Progress pipeline: dtrcv001.p (manual "Pallet
/// Receiving") or its automated twin dtscn012.p validate and create sap-receipt/
/// wms-receipt staging rows, which sendprod-sap.p/sendprod-orawms.p then push to
/// Oracle via DataServer stored-procedure calls (dtrcv-orawms.p PROC-create-MfgOrdConf
/// is the confirmation shape SAP's side is based on). See
/// <see cref="StandAlone.Integration.Services.PalletIntegrationPayload"/> and
/// <see cref="StandAlone.Integration.Services.WmsIntegrationPayload"/> for the two
/// integration-facing shapes.
/// </summary>
public class EolScanRecord
{
    public string PalletId { get; set; } = string.Empty;
    public int Plant { get; set; }
    public string ItemNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ShopOrder { get; set; } = string.Empty;
    public int LisQty { get; set; }
    public int BoxesPerPallet { get; set; }
    public int ConfirmedQty { get; set; }
    public int Shift { get; set; }
    public int LineNumber { get; set; }
    public string Inspector { get; set; } = string.Empty;
    public DateTime ScanTimeUtc { get; set; }
    public bool SapSuccess { get; set; }
    public string SapDetail { get; set; } = string.Empty;
    public bool WmsSuccess { get; set; }
    public string WmsDetail { get; set; } = string.Empty;

    public string TimeDisplay => ScanTimeUtc.ToLocalTime().ToString("MM/dd HH:mm:ss");
    public string SapStatusDisplay => SapSuccess ? "Sent" : "FAILED";
    public string WmsStatusDisplay => WmsSuccess ? "Sent" : "FAILED";
}
