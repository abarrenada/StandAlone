namespace StandAlone.CartonUi.Models;

/// <summary>
/// Complete item detail from Progress database (itemdet + itemhdr tables).
/// CSV columns: 43 fields total (20 from itemdet + 23 from itemhdr).
/// </summary>
public class ItemDetail
{
    // ===== itemdet table (20 fields) =====
    
    /// <summary>Item reference ID (PRIMARY KEY)</summary>
    public int IRef { get; set; }
    
    /// <summary>Item/SKU number (15 chars max)</summary>
    public string ItemNumber { get; set; } = string.Empty;
    
    /// <summary>Units per label (MANDATORY)</summary>
    public int LisQty { get; set; }
    
    /// <summary>Sales quantity per package</summary>
    public decimal SalesQty { get; set; }
    
    /// <summary>Sales unit of measure (SF, PC, BX, etc.)</summary>
    public string SalesUOM { get; set; } = string.Empty;
    
    /// <summary>Package weight in lbs</summary>
    public decimal PkgWeight { get; set; }
    
    /// <summary>Carton UPC number system</summary>
    public int CartonUPC_NumSys { get; set; }
    
    /// <summary>Carton UPC manufacturer ID</summary>
    public int CartonUPC_Mfg { get; set; }
    
    /// <summary>Carton UPC product ID</summary>
    public int CartonUPC_Prod { get; set; }
    
    /// <summary>Carton UPC check digit</summary>
    public int CartonUPC_Chkdgt { get; set; }
    
    /// <summary>Customer character code</summary>
    public string CustomerChar { get; set; } = string.Empty;
    
    /// <summary>Grade code (0-9, MANDATORY)</summary>
    public int Grade { get; set; }
    
    /// <summary>Package indicator (0-9, MANDATORY)</summary>
    public int PkgIndicator { get; set; }
    
    /// <summary>Card printer flag (Y/N)</summary>
    public string CardPrinter { get; set; } = string.Empty;
    
    /// <summary>LIS short description (7 chars max)</summary>
    public string LISDescription { get; set; } = string.Empty;
    
    /// <summary>Shade code (4-digit)</summary>
    public int Shade { get; set; }
    
    /// <summary>Boxes per pallet</summary>
    public int BoxesPerPallet { get; set; }
    
    /// <summary>Flag: needs pallet label (Y/N)</summary>
    public string NeedPalletLabel { get; set; } = string.Empty;
    
    /// <summary>Company code (usually "DT")</summary>
    public string Company { get; set; } = string.Empty;
    
    /// <summary>Status code (A=Active, I=Inactive)</summary>
    public string Status { get; set; } = string.Empty;
    
    /// <summary>Extract code</summary>
    public string Extract { get; set; } = string.Empty;
    
    // ===== itemhdr table (23 fields) =====
    
    /// <summary>Color description (e.g., "FL90-WHITE") — PRIMARY ITEM DISPLAY</summary>
    public string ColorDesc { get; set; } = string.Empty;
    
    /// <summary>Shape/size description (e.g., "3 X 6 X 0.31 IN") — PRIMARY ITEM DISPLAY</summary>
    public string ShapeDesc { get; set; } = string.Empty;
    
    /// <summary>Series/collection name (e.g., "FINISH LINE") — PRIMARY ITEM DISPLAY</summary>
    public string SeriesDesc { get; set; } = string.Empty;
    
    /// <summary>Brand code (e.g., "DB" for Daltile)</summary>
    public string Brand { get; set; } = string.Empty;
    
    /// <summary>Tile type code (3 chars)</summary>
    public string TypeOfTile { get; set; } = string.Empty;
    
    /// <summary>Color code identifier (4 chars)</summary>
    public string ColorId { get; set; } = string.Empty;
    
    /// <summary>Size/shape code (10 chars)</summary>
    public string SizeShape { get; set; } = string.Empty;
    
    /// <summary>WMS unit of measure (3 chars)</summary>
    public string WmsUOM { get; set; } = string.Empty;
    
    /// <summary>Manufacturing plant ID (3 digits)</summary>
    public int Plant { get; set; }
    
    /// <summary>Product type code</summary>
    public string ProductType { get; set; } = string.Empty;
    
    /// <summary>Single tile UPC number system</summary>
    public int SingleUPC_NumSys { get; set; }
    
    /// <summary>Single tile UPC manufacturer ID</summary>
    public int SingleUPC_Mfg { get; set; }
    
    /// <summary>Single tile UPC product ID</summary>
    public int SingleUPC_Prod { get; set; }
    
    /// <summary>Single tile UPC check digit</summary>
    public int SingleUPC_Chkdgt { get; set; }
    
    /// <summary>PEI rating (0-5)</summary>
    public int PEI { get; set; }
    
    /// <summary>Water absorption rating</summary>
    public decimal WA { get; set; }
    
    /// <summary>Coefficient of friction</summary>
    public decimal COF { get; set; }
    
    /// <summary>Tone-based flag (Y/N)</summary>
    public string Tone { get; set; } = string.Empty;
    
    /// <summary>User who created record</summary>
    public string CreateUser { get; set; } = string.Empty;
    
    /// <summary>User who last updated record</summary>
    public string UpdateUser { get; set; } = string.Empty;
    
    /// <summary>Record creation date (YYYY-MM-DD)</summary>
    public string CreateDate { get; set; } = string.Empty;
    
    /// <summary>Record last update date (YYYY-MM-DD)</summary>
    public string UpdateDate { get; set; } = string.Empty;
    
    /// <summary>Label type (0-99)</summary>
    public int LabelTypeCode { get; set; }

    /// <summary>Most recent schedule order number</summary>
    public string LastScheduleOrder { get; set; } = string.Empty;

    /// <summary>Open quantity remaining on the schedule</summary>
    public decimal OpenQty { get; set; }

    /// <summary>Schedule date (YYYY-MM-DD)</summary>
    public string ScheduleDate { get; set; } = string.Empty;

    // ===== Helper methods =====
    
    /// <summary>
    /// Gets the combined Primary Item description for display.
    /// Format: "ColorDesc | ShapeDesc | SeriesDesc"
    /// Example: "FL90-WHITE | 3 X 6 X 0.31 IN | FINISH LINE"
    /// </summary>
    public string GetPrimaryItemDescription() =>
        $"{ColorDesc.TrimEnd()} | {ShapeDesc.TrimEnd()} | {SeriesDesc.TrimEnd()}";
    
    /// <summary>
    /// Gets the full carton UPC barcode (12 digits).
    /// Concatenates: NumSys (1) + Mfg (5) + Prod (5) + Chkdgt (1)
    /// </summary>
    public string GetCartonUPC() =>
        $"{CartonUPC_NumSys}{CartonUPC_Mfg:00000}{CartonUPC_Prod:00000}{CartonUPC_Chkdgt}";
    
    /// <summary>
    /// Gets the single tile UPC barcode (12 digits).
    /// Concatenates: NumSys (1) + Mfg (5) + Prod (5) + Chkdgt (1)
    /// </summary>
    public string GetSingleUPC() =>
        $"{SingleUPC_NumSys}{SingleUPC_Mfg:00000}{SingleUPC_Prod:00000}{SingleUPC_Chkdgt}";
    
    /// <summary>
    /// Gets the 14-digit UCC (EAN) barcode for carton.
    /// Format: "20" + CartonUPC(12)
    /// </summary>
    public string GetUCC() => $"20{GetCartonUPC()}";
}
