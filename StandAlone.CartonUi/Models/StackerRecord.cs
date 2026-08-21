namespace StandAlone.CartonUi.Models;

/// <summary>
/// Represents a stacker → item assignment, equivalent to the Progress <c>stacker</c> table as
/// displayed on its "System Stacker Information" maintenance screen (dtmnt060.p).
///
/// Persisted in <c>stackers{NN}.csv</c> as a plain 5-column, human-editable file (header row +
/// one row per stacker, "Stacker,Item Number,Qty,Shade,Size" — no LineId column since the file
/// itself is already scoped to one line): the operator-facing format, not the underlying
/// Progress schema (which stores an item reference, not the item number text, and derives Qty
/// from the item master rather than storing it). ItemNumber/Qty here are stored directly so the
/// file is self-describing and hand-editable; Qty is re-resolved from itemdet at print time
/// rather than trusted, since it's informational, not the source of truth.
/// </summary>
public class StackerRecord
{
    public int LineId { get; set; }

    /// <summary>Stacker position, "1".."9" (unpadded, matching the plant's stacker file convention).</summary>
    public string StackNum { get; set; } = string.Empty;

    /// <summary>Item number assigned to this stacker (primary or secondary item number).</summary>
    public string ItemNumber { get; set; } = string.Empty;

    /// <summary>Lis Qty shown for reference — re-resolved from itemdet at print time, not authoritative.</summary>
    public int Qty { get; set; }

    public int Shade { get; set; }

    /// <summary>
    /// NOT a physical tile size or caliber code, despite the column header — it's the trailing
    /// digit of a 5-digit shade/lot code, split off from the 4-digit Shade above (Progress
    /// dtlbl007.i: id-shade split into prt-shade/prt-shade2 by dividing by 10; recombined at
    /// print time as prt-shade*10+size and printed together under "Shade/Teinte/Tono"). A
    /// single 4-digit base shade can have multiple lot/blend sub-variants (this digit), each
    /// routed to its own physical stacker for traceability. Still a required single digit
    /// 0-9, embedded as-is in the printed carton barcode.
    /// </summary>
    public string Size { get; set; } = string.Empty;
}
