# Carton Label Decision — CSV Files Guide (Enhanced Schema)

**Based on Progress Database**: `bcmstr3.df` (itemhdr and itemdet tables)

This document describes the complete CSV file structure including all Progress database fields.

---

## Overview

The system uses four CSV files to manage carton production, stacker configuration, and comprehensive item details:

| File | Purpose | Source DB | Created By |
|------|---------|-----------|-----------|
| `boxes{NN}.csv` | Carton box records from sorter line | Progress `box` | PLC/Sorter System |
| `stackers{NN}.csv` | Stacker configuration | Progress sorter config | PLC/Sorter System |
| `itemdet.csv` | **US Item Master (ALL fields from itemdet + itemhdr)** | Progress `itemdet` + `itemhdr` | SAP Data Export |
| `mitemdet.csv` | **Mexico Item Master (ALL fields from itemdet + itemhdr)** | Progress `itemdet` + `itemhdr` | SAP Data Export |

---

## 1. boxes{NN}.csv — Carton Records

**Purpose**: Contains one row per carton processed by the sorter.

**Location**: Configurable via Settings (default: `C:\LabelPrint\data\`)

**Columns** (in order):

| Column | Type | Example | Notes |
|--------|------|---------|-------|
| **RecId** | Integer | 1001 | Unique record ID per box |
| **LineId** | Integer | 1 | Production line number |
| **MakeTime** | DateTime | 2026-07-22 14:30:45 | Box creation timestamp (HH:mm:ss) |
| **StackNum** | String | " 1" | 2-char stack number (space-padded) |
| **PlcMsg** | String | 65-char message | Full PLC message; pos 33-34 indicates Mexico |
| **ErrMsg** | String | "" or "ERROR" | Status message; empty = OK |
| **PrintNum** | Integer | 1 | Print count (> 0 required for printing) |

**Header**:
```
RecId,LineId,MakeTime,StackNum,PlcMsg,ErrMsg,PrintNum
```

---

## 2. stackers{NN}.csv — Stacker Configuration

**Purpose**: Defines stacker setup for each stack on the line.

**Columns** (in order):

| Column | Type | Example | Notes |
|--------|------|---------|-------|
| **LineId** | Integer | 1 | Production line number |
| **StackNum** | String | " 1" | 2-char stack number (space-padded) |
| **IRef** | Integer | 615229 | Item reference (lookup key into itemdet/mitemdet) |
| **PlcMsg** | String | M-... or ... | 65-char; pos 33-34 "M-" = Mexico item |
| **Shade** | Integer | 555 or 5550 | Shade code (4-digit if 4DIGITSHADE flag set, else ×10) |
| **Size** | String | "L" or "4 x 3" | Size code (e.g., "L", "4 x 3", "6 x 9") |
| **ErrMsg** | String | "" or "*OK" | Status; empty or "*OK" = valid |

**Header**:
```
LineId,StackNum,IRef,PlcMsg,Shade,Size,ErrMsg
```

---

## 3. itemdet.csv — Complete US Item Master Data

**Purpose**: Complete item catalog with ALL fields from Progress `itemdet` and `itemhdr` tables.

**Source**: SAP Data Warehouse → Export from Progress database

**Location**: `C:\LabelPrint\data\itemdet.csv` (shared, not line-specific)

### Columns from `itemdet` table (Progress):

| Column | Progress Field | Type | Example | Notes |
|--------|----------------|------|---------|-------|
| **IRef** | `id-iref` | Integer | 615229 | **PRIMARY KEY** - Item reference ID |
| **ItemNumber** | `id-item-nbr` | String | FL9036MOD1P4 | Item/SKU number (15 chars max) |
| **LisQty** | `id-lis-qty` | Integer | 100 | **MANDATORY** - Units per label |
| **SalesQty** | `id-sales-qty` | Decimal | 12.5 | Sales quantity per package |
| **SalesUOM** | `id-sales-um` | String | SF | **MANDATORY** - Sales unit of measure (e.g., SF, PC, BX) |
| **PkgWeight** | `id-pkg-wgt` | Decimal | 37.5 | Package weight in lbs |
| **CartonUPC_NumSys** | `id-ctn-num-sys` | Integer | 0 | Carton UPC number system |
| **CartonUPC_Mfg** | `id-ctn-mfg` | Integer | 81516 | Carton UPC manufacturer ID |
| **CartonUPC_Prod** | `id-ctn-prd` | Integer | 63098 | Carton UPC product ID |
| **CartonUPC_Chkdgt** | `id-ctn-chkdgt` | Integer | 1 | Carton UPC check digit |
| **CustomerChar** | `id-cust-char` | String | "" | Customer character code |
| **Grade** | `id-grade` | Integer | 1 | **MANDATORY** - Grade code (0-9) |
| **PkgIndicator** | `id-pkg-indicator` | Integer | 1 | **MANDATORY** - Package indicator (0-9) |
| **CardPrinter** | `id-card-ptr` | String | "" | Card printer flag (Y/N) |
| **LISDescription** | `id-lis-desc` | String | "FLSH" | LIS short description (7 chars max) |
| **Shade** | `id-shade` | Integer | 555 | Shade code (4-digit) |
| **BoxesPerPallet** | `id-boxes-per-pallet` | Integer | 45 | Boxes per pallet |
| **NeedPalletLabel** | `id-need-pallet-label` | Boolean | true | Flag: needs pallet label (Y/N) |
| **Company** | `id-company` | String | "DT" | Company code (always "DT") |
| **Status** | `id-status` | String | "A" | Status code (A=Active, I=Inactive) |
| **Extract** | `id-extract` | String | "" | Extract code |

### Columns from `itemhdr` table (Progress):

| Column | Progress Field | Type | Example | Notes |
|--------|----------------|------|---------|-------|
| **ColorDesc** | `ih-color-desc` | String | FL90-WHITE | **Item Description (US)** - Color name/code (30 chars) |
| **ShapeDesc** | `ih-shape-desc` | String | 3 X 6 X 0.31 IN | Size/shape description (30 chars) |
| **SeriesDesc** | `ih-series-desc` | String | FINISH LINE | Series/collection name (40 chars) |
| **Brand** | `ih-brand` | String | DB | Brand code (e.g., "DB" for Daltile) |
| **TypeOfTile** | `ih-type-of-tile` | String | PRC | Tile type code (3 chars) |
| **ColorId** | `ih-color-id` | String | FL90 | Color code identifier (4 chars) |
| **SizeShape** | `ih-size-shape` | String | 3X6 | Size/shape code (10 chars) |
| **WmsUOM** | `ih-wms-uom` | String | BX | WMS unit of measure (3 chars) |
| **Plant** | `ih-plant` | Integer | 610 | Manufacturing plant ID (3 digits) |
| **ProductType** | `ih-product-type` | String | "" | Product type code (M=Mosaics) |
| **SingleUPC_NumSys** | `ih-sgl-num-sys` | Integer | 0 | Single tile UPC number system |
| **SingleUPC_Mfg** | `ih-sgl-mfg` | Integer | 81516 | Single tile UPC manufacturer ID |
| **SingleUPC_Prod** | `ih-sgl-prd` | Integer | 63098 | Single tile UPC product ID |
| **SingleUPC_Chkdgt** | `ih-sgl-chkdgt` | Integer | 1 | Single tile UPC check digit |
| **PEI** | `ih-pei` | Integer | 3 | PEI rating (0-5) |
| **WA** | `ih-wa` | Decimal | 8.5 | Water absorption rating |
| **COF** | `ih-cof` | Decimal | 0.67 | Coefficient of friction |
| **Tone** | `ih-tone` | String | "N" | Tone-based flag (Y/N) |
| **CreateUser** | `ih-create-user` | String | "admin" | User who created record |
| **UpdateUser** | `ih-update-user` | String | "admin" | User who last updated record |
| **CreateDate** | `ih-create-dte` | Date | 2026-01-15 | Record creation date |
| **UpdateDate** | `ih-update-dte` | Date | 2026-07-20 | Record last update date |
| **LabelTypeCode** | `ih-label-type-code` | Integer | 0 | Label type (0-99) |

### Complete CSV Header:
```
IRef,ItemNumber,LisQty,SalesQty,SalesUOM,PkgWeight,CartonUPC_NumSys,CartonUPC_Mfg,CartonUPC_Prod,CartonUPC_Chkdgt,CustomerChar,Grade,PkgIndicator,CardPrinter,LISDescription,Shade,BoxesPerPallet,NeedPalletLabel,Company,Status,Extract,ColorDesc,ShapeDesc,SeriesDesc,Brand,TypeOfTile,ColorId,SizeShape,WmsUOM,Plant,ProductType,SingleUPC_NumSys,SingleUPC_Mfg,SingleUPC_Prod,SingleUPC_Chkdgt,PEI,WA,COF,Tone,CreateUser,UpdateUser,CreateDate,UpdateDate,LabelTypeCode
```

### Example Row:
```csv
615229,FL9036MOD1P4,100,12.5,SF,37.5,0,81516,63098,1,"",1,1,"","FLSH",555,45,true,"DT","A","",FL90-WHITE,3 X 6 X 0.31 IN,FINISH LINE,DB,PRC,FL90,3X6,BX,610,"",0,81516,63098,1,3,8.5,0.67,N,admin,admin,2026-01-15,2026-07-20,0
```

### Key Notes for itemdet.csv:

- **IRef**: Primary key; must be unique within itemdet.csv
- **ItemNumber**: Displayed on labels and in Browse grid
- **LisQty**: Quantity per label (critical for carton printing)
- **SalesQty + SalesUOM**: Used in label pricing/shipping info
- **Grade**: Product grade (affects label appearance/highlighting)
- **ColorDesc, ShapeDesc, SeriesDesc**: **Displayed as Primary Item Description** in UI
  - Example combined display: `FL90-WHITE | 3 X 6 X 0.31 IN | FINISH LINE`
- **Shade**: Must match shade in `stackers{NN}.csv` for correct label variant
- **CartonUPC_***: Used in NiceLabel XML barcode generation
- **NeedPalletLabel**: Flag for pallet label printing logic
- **Plant**: Manufacturing location (used for plant-specific configurations)
- **Status**: "A" for active items; "I" or blank for inactive

---

## 4. mitemdet.csv — Complete Mexico Item Master Data

**Purpose**: Complete item catalog for Mexico-only items with ALL fields from Progress tables.

**Structure**: Identical to `itemdet.csv` but for Mexico variants (IRef lookup when PlcMsg pos 33-34 == "M-")

**Header** (identical):
```
IRef,ItemNumber,LisQty,SalesQty,SalesUOM,PkgWeight,CartonUPC_NumSys,CartonUPC_Mfg,CartonUPC_Prod,CartonUPC_Chkdgt,CustomerChar,Grade,PkgIndicator,CardPrinter,LISDescription,Shade,BoxesPerPallet,NeedPalletLabel,Company,Status,Extract,ColorDesc,ShapeDesc,SeriesDesc,Brand,TypeOfTile,ColorId,SizeShape,WmsUOM,Plant,ProductType,SingleUPC_NumSys,SingleUPC_Mfg,SingleUPC_Prod,SingleUPC_Chkdgt,PEI,WA,COF,Tone,CreateUser,UpdateUser,CreateDate,UpdateDate,LabelTypeCode
```

**Key Differences**:
- Used only when `PlcMsg` at position 33-34 contains "M-"
- IRef values can overlap with `itemdet.csv` (separate namespace)
- ColorDesc/ShapeDesc may be in Spanish for Mexico market
- Plant likely different (Mexico manufacturing plant ID)
- All other fields identical in structure/meaning

---

## Complete Data Flow Example

### Scenario: Primary Item Display with Description

**1. Startup Form - User enters Primary Item (if enabled)**
```
TextBox: "FL9036MOD1P4"
LabelBelow: "(pending lookup)"
```

**2. User presses Enter or Tab → Async Lookup Triggered**
```csharp
// Find IRef from ItemNumber in itemdet/mitemdet CSV
IRef = itemdetCSV.Where(x => x.ItemNumber == "FL9036MOD1P4").FirstOrDefault()?.IRef;
// If not found in itemdet, try mitemdet (if Mexico mode enabled)
```

**3. Lookup Result Found (IRef = 615229)**
```csharp
itemRec = itemdetCSV.Where(x => x.IRef == 615229).FirstOrDefault();
```

**4. Display Item Description Below TextBox**
```
ColorDesc: "FL90-WHITE"
ShapeDesc: "3 X 6 X 0.31 IN"
SeriesDesc: "FINISH LINE"

Combined Display (Label below TextBox):
"FL90-WHITE | 3 X 6 X 0.31 IN | FINISH LINE"
// or
"FL90-WHITE - 3 X 6 X 0.31 IN (FINISH LINE)"
```

**5. When User Clicks Begin → PLC Setup Message Sent**
```
Message: "50, ,{shift},{primaryItem},{labelSize}"
```

**6. During Browse → NiceLabel XML Export (F1 Reprint)**
```xml
<ITEM>FL9036MOD1P4</ITEM>
<COLOR_DESC>FL90-WHITE</COLOR_DESC>
<SHAPE_DESC>3 X 6 X 0.31 IN</SHAPE_DESC>
<SERIES_DESC>FINISH LINE</SERIES_DESC>
<GRADE_DESC>STD</GRADE_DESC>
<SALES_QTY>12.5</SALES_QTY>
<SALES_UM>SF</SALES_UM>
<QTY_CARTON>100</QTY_CARTON>
```

---

## File Format Specification

### CSV Standard

- **Delimiter**: Comma (`,`)
- **Encoding**: UTF-8 (no BOM)
- **Line Endings**: LF (`\n`) or CRLF (`\r\n`)
- **Comments**: Lines starting with `#` are ignored
- **Empty Lines**: Skipped
- **Whitespace Handling**: Leading/trailing spaces trimmed per field
- **Strings**: Quote if containing comma/newline
- **Numbers**: Integers only for integer columns; decimals for decimal columns
- **Dates**: `YYYY-MM-DD` format (ISO 8601)
- **Booleans**: "true"/"false" or "Y"/"N" or "1"/"0"

### Data Type Rules

| Type | Format | Example | Notes |
|------|--------|---------|-------|
| **Integer** | Digits only, no decimals | 615229 | No commas for thousands |
| **Decimal** | Digits + 1 decimal point | 12.5 | Max 2 decimals for currency; 5 for conversion factors |
| **String** | Alphanumeric, 1-40 chars | FL90-WHITE | Quote if contains comma or newline |
| **Boolean** | true/false or Y/N | true | Case-insensitive |
| **Date** | YYYY-MM-DD | 2026-07-20 | ISO 8601 format |

---

## Best Practices

✅ **Do**:
- Back up `.csv` files daily
- Use unique IRef values per item
- Keep itemdet/mitemdet in sync with SAP exports
- Test PLC → CSV integration before go-live
- Document any custom mapping between SAP → IRef
- Archive boxes{NN}.csv when > 100 MB

❌ **Don't**:
- Edit files while system is running (PLC may lock)
- Use non-ASCII characters (breaks CSV parsing)
- Duplicate IRef values (causes ambiguous lookups)
- Manually create IRef sequences (coordinate with SAP)
- Mix date formats (always YYYY-MM-DD)
- Leave status blank (use "A" or "I" explicitly)

---

## Integration with NiceLabel XML Export

When F1 (Reprint) is pressed, the system exports to NiceLabel XML using itemdet fields:

```xml
<LABEL>
  <ITEM>FL9036MOD1P4</ITEM>
  <ITEM_FORMATTED>FL90  36MOD1P4</ITEM_FORMATTED>
  <COLOR_DESC>FL90-WHITE</COLOR_DESC>
  <SHAPE_DESC>3 X 6 X 0.31 IN</SHAPE_DESC>
  <SERIES_DESC>FINISH LINE</SERIES_DESC>
  <GRADE_DESC>STD</GRADE_DESC>
  <SALES_QTY>12.5</SALES_QTY>
  <SALES_UM>SF</SALES_UM>
  <PKG_WGT>37.5</PKG_WGT>
  <LB_DESC>LBS</LB_DESC>
  <QTY_CARTON>100</QTY_CARTON>
  <CTN_DESC>UNITS</CTN_DESC>
  <BRAND_DESC>Daltile</BRAND_DESC>
  <!-- UPC barcodes -->
  <UPC_BARCODE>081516630981</UPC_BARCODE>
  <UCC_BARCODE>20081516630985</UCC_BARCODE>
</LABEL>
```

---

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| Primary Item shows "Not found" | IRef/ItemNumber mismatch | Verify ItemNumber exists in CSV with matching IRef |
| No description appears below Item | ColorDesc/ShapeDesc empty in CSV | Export from SAP with all descriptor fields populated |
| Mexico items not showing | PlcMsg at pos 33-34 ≠ "M-" | Check PLC is marking Mexico items correctly |
| NiceLabel XML missing fields | Field not in itemdet CSV | Add missing column to CSV export pipeline |
| Grade shows as "0" on label | Grade column empty in CSV | Ensure Grade is exported from SAP (default to "1") |
| ItemNumber mismatch | SAP ItemNumber ≠ itemdet.csv | Standardize item numbering in SAP export |

---

## Export Instructions for SAP/ERP Teams

### To generate itemdet.csv from SAP:

1. **Query tables**: itemdet + itemhdr (join on item number)
2. **Filter**: Company = "DT", Status = "A" (active items only)
3. **Sort**: By IRef ascending
4. **Output columns** (in this exact order):
   ```
   itemdet.iref,
   itemdet.item_nbr,
   itemdet.lis_qty,
   itemdet.sales_qty,
   itemdet.sales_um,
   itemdet.pkg_wgt,
   itemdet.ctn_num_sys,
   itemdet.ctn_mfg,
   itemdet.ctn_prd,
   itemdet.ctn_chkdgt,
   itemdet.cust_char,
   itemdet.grade,
   itemdet.pkg_indicator,
   itemdet.card_ptr,
   itemdet.lis_desc,
   itemdet.shade,
   itemdet.boxes_per_pallet,
   itemdet.need_pallet_label,
   itemdet.company,
   itemdet.status,
   itemdet.extract,
   itemhdr.color_desc,
   itemhdr.shape_desc,
   itemhdr.series_desc,
   itemhdr.brand,
   itemhdr.type_of_tile,
   itemhdr.color_id,
   itemhdr.size_shape,
   itemhdr.wms_uom,
   itemhdr.plant,
   itemhdr.product_type,
   itemhdr.sgl_num_sys,
   itemhdr.sgl_mfg,
   itemhdr.sgl_prd,
   itemhdr.sgl_chkdgt,
   itemhdr.pei,
   itemhdr.wa,
   itemhdr.cof,
   itemhdr.tone,
   itemhdr.create_user,
   itemhdr.update_user,
   itemhdr.create_dte,
   itemhdr.update_dte,
   itemhdr.label_type_code
   ```
5. **Export format**: CSV with UTF-8 encoding, LF line endings
6. **Schedule**: Daily export at 01:00 AM to `C:\LabelPrint\data\itemdet.csv`

### For Mexico items (mitemdet.csv):

- Same export but filter: `product_type = "M"` (Mosaics) OR `needs_mexico_label = true`
- Alternative: Use separate Mexico ERP system if available
- Place in same directory as itemdet.csv

---

## Contact

For data mapping or export issues, contact:
- SAP Team: `sap-support@company.com`
- PLC/MES Team: `mes-support@company.com`
- Carton Printing System: `label-app@company.com`

