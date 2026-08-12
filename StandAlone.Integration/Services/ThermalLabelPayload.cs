namespace StandAlone.Integration.Services;

public sealed class ThermalLabelPayload
{
    public string LabelFormat { get; init; } = "CARTON_LABEL";
    public int LabelTypeCode { get; init; }
    public string PalletId { get; init; } = string.Empty;
    public string ItemNumber { get; init; } = string.Empty;
    public int IRef { get; init; }
    public int Plant { get; init; }
    public string PartDescription { get; init; } = string.Empty;
    public string ColorDesc { get; init; } = string.Empty;
    public string ShapeDesc { get; init; } = string.Empty;
    public string SeriesDesc { get; init; } = string.Empty;
    public string LabelSize { get; init; } = string.Empty;
    public string StackNumber { get; init; } = string.Empty;
    public string Shade { get; init; } = string.Empty;
    public string Size { get; init; } = string.Empty;
    public int BoxesPerPallet { get; init; }
    public decimal SalesQty { get; init; }
    public string SalesUom { get; init; } = string.Empty;
    public decimal PackageWeight { get; init; }
    public string Inspector { get; init; } = string.Empty;
    public int Shift { get; init; }
    public int LineNumber { get; init; }
    public int Quantity { get; init; } = 1;
    public int LisQty { get; init; }
    public string UccBarcode { get; init; } = string.Empty;
    public string CartonUpc { get; init; } = string.Empty;
    public string CartonUpcNumSys { get; init; } = string.Empty;
    public string CartonUpcMfg { get; init; } = string.Empty;
    public string CartonUpcProd { get; init; } = string.Empty;
    public string CartonUpcChkdgt { get; init; } = string.Empty;
    public string CartonBarcodeSerial { get; set; } = string.Empty;
    public string MfgDateCode { get; set; } = string.Empty;
    public string ItemNumberMasked { get; set; } = string.Empty;
    public string PartDescriptionShort { get; set; } = string.Empty;
    public string ShopOrder { get; init; } = string.Empty;
    public string Caliber { get; init; } = string.Empty;
    public string TemplateOrientation { get; init; } = "ref-first";
    public bool IsMexicoItem { get; init; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    // ── Pallet-label fields ──────────────────────────────────────────────────
    public int Grade { get; init; }
    public string Location { get; init; } = string.Empty;
    public string PlantName { get; init; } = string.Empty;

    /// <summary>
    /// 30-char barcode of the carton that was scanned to look up this pallet's item
    /// (Progress prt-barcode). Embedded as a secondary reference barcode on the WMS
    /// pallet label. Left empty when the pallet was looked up by item/stack number
    /// instead of an actual carton scan.
    /// </summary>
    public string CartonReferenceBarcode { get; init; } = string.Empty;

    /// <summary>Login/operator id printed on the pallet label footer (Progress p-userid).</summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>Short printer/terminal identifier for the pallet label footer (Progress w-trk-term).</summary>
    public string PrinterTermId { get; init; } = string.Empty;

    /// <summary>itemhdr.ih-wms-uom. When "CT", pkgconfig and total pieces use boxes-per-pallet directly.</summary>
    public string WmsUom { get; init; } = string.Empty;
}
