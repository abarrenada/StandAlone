namespace StandAlone.CartonUi.Models;

/// <summary>
/// Represents a box record from the sorter line, equivalent to the Progress <c>box</c> table.
/// </summary>
public class BoxRecord
{
    public int RecId { get; set; }
    public int LineId { get; set; }
    public DateTime MakeTime { get; set; }

    /// <summary>2-character stack number, e.g. " 1", " 2".</summary>
    public string StackNum { get; set; } = string.Empty;

    /// <summary>Full PLC message (65 chars). Position 1-2 = stack display; position 33-62 = item description.</summary>
    public string PlcMsg { get; set; } = string.Empty;
    public string ErrMsg { get; set; } = string.Empty;
    public int PrintNum { get; set; }
    /// <summary>30-character barcode serial computed at box-creation time (Progress prt-barcode format).</summary>
    public string BarcodeSerial { get; set; } = string.Empty;

    // ── Display helpers ──────────────────────────────────────────────────────

    public string TimeDisplay => MakeTime.ToString("HH:mm:ss");

    /// <summary>
    /// Progress: <c>substring(box.bx-plcmsg,1,2) + " " + substring(box.bx-plcmsg,33,30)</c>
    /// </summary>
    public string SorterMessage
    {
        get
        {
            var s1 = PlcMsg.Length >= 2 ? PlcMsg[..2] : PlcMsg.PadRight(2);
            var s2 = PlcMsg.Length > 32
                ? PlcMsg.Substring(32, Math.Min(30, PlcMsg.Length - 32))
                : string.Empty;
            return $"{s1} {s2}";
        }
    }

    public bool CanPrint => PrintNum > 0 && string.IsNullOrEmpty(ErrMsg);

    /// <summary>Set after stacker lookup: item#-qty-shade-size string.</summary>
    public string ItemDisplay { get; set; } = string.Empty;
}
