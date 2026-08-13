namespace StandAlone.CartonUi.Models;

/// <summary>
/// Registry entry written when a pallet label is printed, so an EOL scan of the
/// same pallet serial can later look up what's on it. No Progress equivalent —
/// the legacy system relied on live wmstosend/box records instead of a durable
/// serial-&gt;pallet-info map.
/// </summary>
public class PalletRecord
{
    /// <summary>"PPP-SSSSSSSSS" — matches Progress prt-tag-nbr.</summary>
    public string PalletId { get; set; } = string.Empty;
    public int Plant { get; set; }
    public string ItemNumber { get; set; } = string.Empty;
    public string ColorDesc { get; set; } = string.Empty;
    public string ShapeDesc { get; set; } = string.Empty;
    public string SeriesDesc { get; set; } = string.Empty;
    public int LisQty { get; set; }
    public int BoxesPerPallet { get; set; }
    public string Shade { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string ShopOrder { get; set; } = string.Empty;
    public int Grade { get; set; }
    public int LineNumber { get; set; }
    public int Shift { get; set; }
    public string Inspector { get; set; } = string.Empty;
    public DateTime PrintedAtUtc { get; set; }

    public int TotalPieces => LisQty * BoxesPerPallet;

    public string Description => $"{ColorDesc.TrimEnd()} | {ShapeDesc.TrimEnd()} | {SeriesDesc.TrimEnd()}";

    public string TimeDisplay => PrintedAtUtc.ToLocalTime().ToString("MM/dd HH:mm:ss");
}
