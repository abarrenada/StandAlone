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
    public string UccBarcode { get; init; } = string.Empty;
    public string CartonUpc { get; init; } = string.Empty;
    public string TemplateOrientation { get; init; } = "ref-first";
    public bool IsMexicoItem { get; init; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}
