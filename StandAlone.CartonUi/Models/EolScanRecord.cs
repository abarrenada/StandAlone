namespace StandAlone.CartonUi.Models;

/// <summary>
/// One end-of-line scan transaction: a pallet serial confirmed as shipped/complete,
/// logged locally and sent to SAP for production backflush. See
/// <see cref="StandAlone.Integration.Services.PalletIntegrationPayload"/> for the
/// SAP-facing shape and dtrcv-orawms.p PROC-create-MfgOrdConf for the legacy origin.
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

    public string TimeDisplay => ScanTimeUtc.ToLocalTime().ToString("MM/dd HH:mm:ss");
    public string SapStatusDisplay => SapSuccess ? "Sent" : "FAILED";
}
