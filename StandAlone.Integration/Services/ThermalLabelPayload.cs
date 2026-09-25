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

    /// <summary>
    /// The 30-byte barcode's actual encoding directive for the "BG" symbology (dtlbl060b.i/
    /// dtlbl065d.i): "&gt;H" + first 2 chars + "&gt;C" + the rest — a real Code128 mode switch,
    /// not cosmetic. "&gt;C" packs the remaining (all-numeric) digits two-per-symbol, roughly
    /// halving the barcode's physical width versus encoding all 30 characters in one mode.
    /// CartonBarcodeSerial itself stays the plain 30-char string (used for the human-readable
    /// text line under the barcode, which should show the raw value, not this directive).
    /// </summary>
    public string CartonBarcodeCommand { get; set; } = string.Empty;
    public string MfgDateCode { get; set; } = string.Empty;
    public string ItemNumberMasked { get; set; } = string.Empty;
    public string PartDescriptionShort { get; set; } = string.Empty;

    /// <summary>
    /// The combined 5-digit shade/lot code shown on the label under "Shade/Teinte" — Progress
    /// dtlbl060b.i's prt-shade2 (4-digit shade*10) + prt-size (trailing lot digit), printed
    /// together as one number. Computed at render time (see ThermalPrinterCommandBuilder.
    /// ComputeShadeLotCode) from the same Shade/Caliber/Size fields used to build the barcode,
    /// so the visible code always matches what's encoded in the barcode.
    /// </summary>
    public string ShadeLotCode { get; set; } = string.Empty;

    /// <summary>
    /// The physical stacker that produced this carton, "01".."09" — set only for prints tied
    /// to a real PLC stacker event (carton labels only; pallet/HCS labels never show this).
    /// Empty otherwise (e.g. a manual print), in which case InspectorDisplay falls back to "00".
    /// </summary>
    public string PhysicalStackNumber { get; init; } = string.Empty;

    /// <summary>
    /// "{Inspector} {PhysicalStackNumber:00}" (or "{Inspector} 00" when no stacker applies) —
    /// Progress dtplc067.p builds this by concatenating prt-inspector + " " +
    /// string(stacker.st-stacknum,"99") at the point the label string is assembled; prt-inspector
    /// itself only ever holds the raw 2-char inspector code (see Inspector above). Computed at
    /// render time (see ThermalPrinterCommandBuilder.ComputeInspectorDisplay). Carton labels only.
    /// </summary>
    public string InspectorDisplay { get; set; } = string.Empty;

    public string ShopOrder { get; init; } = string.Empty;
    public string Caliber { get; init; } = string.Empty;
    public string TemplateOrientation { get; init; } = "ref-first";
    public bool IsMexicoItem { get; init; }

    /// <summary>
    /// True when this print was operator-triggered ("Manual Qty" CartonPrintMode) rather than
    /// PLC-signal-driven. Currently only changes anything for CrossOver (LabelTypeCode=6):
    /// the manual path uses a different, 2-up/kiss-cut physical layout (Progress
    /// dtlbl061.p/dtlbl071.p + {t0sz...-co-man-2upkiss.i}) instead of the PLC path's
    /// single-up layout (dtplc067.p).
    /// </summary>
    public bool IsManualPrint { get; init; }
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

    // ── Standard Retail fields (itemdet.id-cust-char = H/L/B/D/F) ───────────

    /// <summary>itemdet.id-cust-char — H/L/B/D/F for a real retail customer, blank for Manufacturing.</summary>
    public string CustomerChar { get; init; } = string.Empty;

    /// <summary>
    /// itemhdr.ih-label-type (1/2/3) — selects the Standard Retail right-edge side panel: 1=brand
    /// name, 2=customer part number (CPN) highlighted box, 3=PEI/WA/COF/Tone/grade icon stack.
    /// Not to be confused with <see cref="LabelTypeCode"/> (ih-label-type-code, the 4/6 family selector).
    /// </summary>
    public int PanelType { get; init; }

    public string ColorDescFrench { get; init; } = string.Empty;
    public string ColorDescSpanish { get; init; } = string.Empty;
    public string ShapeDescFrench { get; init; } = string.Empty;
    public string ShapeDescSpanish { get; init; } = string.Empty;

    /// <summary>PEI wear-class rating (1-4), drives the PanelType=3 icon stack.</summary>
    public int Pei { get; init; }

    /// <summary>Water-absorption class (0.5/3.0/3.5/5.0/7.0/16.0), drives the PanelType=3 icon stack.</summary>
    public decimal Wa { get; init; }

    /// <summary>Coefficient-of-friction class (1/2/3), drives the PanelType=3 icon stack.</summary>
    public decimal Cof { get; init; }

    /// <summary>"Y" shows the shade/tone-variation icon in the PanelType=3 icon stack.</summary>
    public string Tone { get; init; } = string.Empty;

    /// <summary>itemhdr.ih-type-of-tile — only "FLT" additionally gates the COF icon on the 2x6 size.</summary>
    public string TypeOfTile { get; init; } = string.Empty;

    /// <summary>
    /// Retail customer's own Customer Product Number (bc-cpn lookup). Drives the PanelType=2 side
    /// panel and the per-size id-cust-char ColorDesc/highlight-box swaps (2x6, 3x4.5).
    /// </summary>
    public string CustomerPartNumber { get; init; } = string.Empty;

    /// <summary>Resolved brand description (brand.br-desc, blank unless br-print is set). Drives the PanelType=1 side panel.</summary>
    public string BrandName { get; init; } = string.Empty;

    /// <summary>grade.gr-highlight — draws a highlight box around the Qual/Cal field when true.</summary>
    public bool GradeHighlight { get; init; }

    /// <summary>itemdet.id-pkg-indicator (0-9) — first digit of the interleaved-2-of-5 case shipping code.</summary>
    public int PkgIndicator { get; init; }

    /// <summary>itemdet.id-lis-desc — unit caption printed after the LIS qty in the qty/weight block (e.g. "SF", "PC").</summary>
    public string LisDesc { get; init; } = string.Empty;

    // ── Standard Retail computed-at-render-time fields ───────────────────────
    // Populated by ThermalPrinterCommandBuilder.ComputeXxx(payload) in ResolveCommandText,
    // once the target lt_retail_{size} template is already known, following the same pattern
    // as ShadeLotCode/InspectorDisplay/MfgDateCode above.

    /// <summary>"{machine4}-{line:00}-{term}" traceability stamp (Progress w-trk-term/t-machine/dd-line-nbr).</summary>
    public string MachineLineTerminalCode { get; set; } = string.Empty;

    /// <summary>
    /// The printer-hand-specific reference-point move (Progress's ws-dev-model-driven
    /// "~033A3H...V..." branch in dtlbl060b.i) — only meaningful on the 45x3/4x3 sizes, the
    /// only ones that ported the full 3-way hand branch. Computed from the station's configured
    /// PrinterModel (see PrinterModelCatalog/EngineHand in StandAlone.CartonUi).
    /// </summary>
    public string EngineReferenceMove { get; set; } = string.Empty;

    /// <summary>
    /// Interleaved-2-of-5 case/shipping barcode payload (Progress prt-scs / s-i2of5), derived from
    /// this item's own carton UPC digits (dtlbl006.i's checksum). Empty when not applicable.
    /// </summary>
    public string CaseShippingCode { get; set; } = string.Empty;

    /// <summary>
    /// Pre-rendered SBPL snippet for the right-edge side panel (brand/CPN/icon stack), sized and
    /// positioned for the specific template being rendered. Empty on the 4x3 size (always suppressed)
    /// or when PanelType has no matching content (e.g. PanelType=1 with a blank BrandName).
    /// </summary>
    public string SidePanelBlock { get; set; } = string.Empty;

    /// <summary>
    /// ColorDesc as it should actually be printed — on most sizes this is just ColorDesc, but the
    /// 2x6 and 3x4.5 templates swap in "{CustomerPartNumber}{ColorDesc}" for certain CustomerChar
    /// values, matching the id-cust-char-driven composite baked into dtlbl060r.i/dtlbl065d.i.
    /// </summary>
    public string ColorDescDisplay { get; set; } = string.Empty;

    /// <summary>SBPL highlight-box graphic drawn around Qual/Cal when GradeHighlight is true; empty otherwise.</summary>
    public string GradeHighlightBlock { get; set; } = string.Empty;

    /// <summary>SBPL interleaved-2/5 case barcode section; empty when CaseShippingCode is blank or PanelType=3.</summary>
    public string CaseBarcodeBlock { get; set; } = string.Empty;

    /// <summary>SBPL qty/weight/LIS-qty block — 3 lines normally, 1 line when carton qty was overridden vs. LIS qty.</summary>
    public string QtyWeightBlock { get; set; } = string.Empty;

    /// <summary>
    /// SBPL highlight-box graphic drawn around ColorDescDisplay when the per-size id-cust-char rule
    /// fires (2x6: CustomerChar B/F; 3x4.5: CustomerChar L/F/B gated on PanelType=1); empty otherwise.
    /// </summary>
    public string CustomerHighlightBlock { get; set; } = string.Empty;

    // ── Generic computed-at-render-time fields ────────────────────────────────
    // Populated for every template (not just one family) in ResolveCommandText, alongside
    // CartonBarcodeSerial/MfgDateCode/ShadeLotCode/InspectorDisplay above.

    /// <summary>Short mfg-date code: last digit of year + 3-digit Julian day, no time suffix
    /// (Progress prt-yjjj, e.g. dtlbl060d.i's CrossOver date field) — distinct from MfgDateCode,
    /// which also appends ":HHmm".</summary>
    public string MfgDateCodeShort { get; set; } = string.Empty;

    /// <summary>First 4 characters of ItemNumber, padded/clipped to 15 chars first (Progress
    /// substr(prt-item-nbr,1,4) — used where a template splits the item number across two
    /// separately-positioned barcode/text fields instead of printing it as one run).</summary>
    public string ItemNumberPart1 { get; set; } = string.Empty;

    /// <summary>Characters 5-15 of ItemNumber, padded/clipped to 15 chars first (Progress
    /// substr(prt-item-nbr,5,11)) — the counterpart to ItemNumberPart1.</summary>
    public string ItemNumberPart2 { get; set; } = string.Empty;

    /// <summary>SalesQty formatted to 2 decimal places as text (Progress "zzzzzz9.99" picture).</summary>
    public string SalesQtyFormatted { get; set; } = string.Empty;

    /// <summary>PackageWeight formatted to 2 decimal places as text (Progress "zzzzzz9.99" picture).</summary>
    public string PackageWeightFormatted { get; set; } = string.Empty;

    // ── F&D Private Label fields (label-type-code=4) ──────────────────────────
    // Progress sources prt-pl-nom-us/met, prt-pl-act-us/met and prt-pl-thickness from a SAP-fed
    // sapitemhdr/sapitemuom buffer (dtlbl007n.i) -- same kind of gap as CrossOver's sapitemhdr
    // dependency (see lt06_xover_2x725_ref-first.sato). No sapitemhdr equivalent exists in this
    // port, so these five ship blank until a real SAP data source is wired up; init-settable so a
    // future integration can populate them without touching this class again.

    /// <summary>Nominal size, US units, pre-formatted (Progress prt-pl-nom-us, e.g. "12.00in x 24.00in").</summary>
    public string NominalSizeUs { get; init; } = string.Empty;

    /// <summary>Nominal size, metric (Progress prt-pl-nom-met, sapitemhdr.sih-nom-metric).</summary>
    public string NominalSizeMetric { get; init; } = string.Empty;

    /// <summary>Actual size, US units, pre-formatted (Progress prt-pl-act-us).</summary>
    public string ActualSizeUs { get; init; } = string.Empty;

    /// <summary>Actual size, metric (Progress prt-pl-act-met, sapitemhdr.sih-act-metric).</summary>
    public string ActualSizeMetric { get; init; } = string.Empty;

    /// <summary>Thickness (Progress prt-pl-thickness, sapitemhdr.sih-thickness).</summary>
    public string Thickness { get; init; } = string.Empty;

    /// <summary>
    /// PackageWeight converted to kg and formatted as " (X.XXkg)" (leading space, Progress
    /// dtplb003/004/005/008/008I.i's inline `" (" + trim(string(prt-pkg-wgt-met,"zzz9.99")) + "kg)"`
    /// — computed generically from PackageWeight × 0.453592, see dtlbl007n.i:349).
    /// </summary>
    public string PackageWeightMetricFormatted { get; set; } = string.Empty;

    /// <summary>
    /// SalesQty converted to sqm and formatted as "(X.XXsqm)" when SalesUom is "SF", else a single
    /// space (Progress prt-met-coverage, dtlbl007n.i:351-360 — no leading space, unlike
    /// PackageWeightMetricFormatted; computed generically from SalesQty ÷ 10.764).
    /// </summary>
    public string CoverageMetricFormatted { get; set; } = string.Empty;

    /// <summary>
    /// 4.5x3-only: the per-model base reference-move offset (e.g. "H0420V0000") that positions
    /// this size's one shared field layout — see ThermalPrinterCommandBuilder.ComputeFndReferenceMoveOffset.
    /// </summary>
    public string FndReferenceMoveOffset { get; set; } = string.Empty;

    /// <summary>
    /// 3x4.5-only: the per-model LabelVarB footer command block — see
    /// ThermalPrinterCommandBuilder.ComputeFnd3x45Footer.
    /// </summary>
    public string Fnd3x45Footer { get; set; } = string.Empty;

    // ── Mexico Store fields (MEXICO-ONLY flag + mitemdet/mitemhdr) ───────────
    // Progress dtplc067.p's Mexico branch is a hard fork from itemdet/itemhdr-driven families —
    // see ThermalPrinterCommandBuilder.ApplyMexicoFields. Only the S86-engine, 4.5x3 (dtlbl066n.i)
    // path is ported so far; 3x4.5 (dtlbl066m.i) and 1.75x8.38 (dtlbl060p.i) are not yet built.

    /// <summary>
    /// The Mexico-specific 30-byte barcode (Progress prt-barcode): same field layout as
    /// CartonBarcodeSerial but with an "M" prefix instead of "%" (dtlbl066n.i/066m.i/060p.i all
    /// share this exact formula). See ThermalPrinterCommandBuilder.ComputeMexicoBarcodeSerial.
    /// </summary>
    public string MexicoBarcodeSerial { get; set; } = string.Empty;

    /// <summary>">H"+2 chars+">C"+rest encoding directive for MexicoBarcodeSerial — same split as CartonBarcodeCommand.</summary>
    public string MexicoBarcodeCommand { get; set; } = string.Empty;

    /// <summary>
    /// The Mexico label's "Lot:" field — NOT a real lot number. Progress
    /// substr(w-shade-x,2,2) + string(prt-size,"9"): the middle 2 digits of the 4-digit shade2
    /// code, plus a single size/caliber digit. See ThermalPrinterCommandBuilder.ComputeMexicoLotCode.
    /// </summary>
    public string MexicoLotCode { get; set; } = string.Empty;

    /// <summary>
    /// Spanish "Contenido ..." quantity string (dtlbl066n.i prt-qty-str) — full sales-qty/weight/LIS
    /// breakdown when Quantity matches LisQty, or a short carton-qty-only form when overridden.
    /// See ThermalPrinterCommandBuilder.ComputeMexicoQtyString.
    /// </summary>
    public string MexicoQtyString { get; set; } = string.Empty;

    /// <summary>
    /// SBPL "Tono/Calbr:" line (shade + caliber) — empty when shade2 is 0, matching dtlbl066n.i's
    /// "if prt-shade2 gt 0" guard. See ThermalPrinterCommandBuilder.ComputeMexicoToneCalbrBlock.
    /// </summary>
    public string MexicoToneCalbrBlock { get; set; } = string.Empty;

    /// <summary>
    /// SBPL PEI/WA/COF/Tone/grade icon stack for the Mexico 4.5x3 label — same icon graphics and
    /// coordinates as Standard Retail's PanelType=3 stack, but the COF icon is additionally gated
    /// on CustomerChar H/L (dtlbl066n.i lines 178-185), which Standard Retail's stack doesn't do.
    /// See ThermalPrinterCommandBuilder.ComputeMexicoIconBlock.
    /// </summary>
    public string MexicoIconBlock { get; set; } = string.Empty;
}
