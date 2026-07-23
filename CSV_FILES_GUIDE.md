# Carton Label Decision — CSV Files Guide

This document explains each CSV file used by the system and provides examples.

---

## Overview

The system uses four CSV files to manage carton production, stacker configuration, and item details:

| File | Purpose | Created By | Line Number |
|------|---------|-----------|------------|
| `boxes{NN}.csv` | Carton box records from sorter line | PLC/Sorter System | Required (e.g., `boxes01.csv` for line 1) |
| `stackers{NN}.csv` | Stacker configuration & inventory | PLC/Sorter System | Required (e.g., `stackers01.csv` for line 1) |
| `itemdet.csv` | US item master data | Manual Setup | Shared across all lines |
| `mitemdet.csv` | Mexico item master data | Manual Setup | Shared across all lines |

The `{NN}` placeholder is replaced with the **2-digit production line number** (e.g., `01`, `02`, `12`).

---

## 1. boxes{NN}.csv — Carton Records

**Purpose**: Contains one row per carton processed by the sorter. PLC writes records as boxes are sorted.

**Location**: `C:\LabelPrint\data\` (configurable in Settings)

**Filename Example**: 
- Line 01: `boxes01.csv`
- Line 12: `boxes12.csv`

**Columns** (in order):

| Column | Type | Example | Notes |
|--------|------|---------|-------|
| **RecId** | Integer | 1001 | Unique record ID per box |
| **LineId** | Integer | 1 | Production line number |
| **MakeTime** | DateTime | 2026-07-22 14:30:45 | When the box was created (HH:mm:ss format) |
| **StackNum** | String | " 1" or " 2" | 2-character stack number (space-padded) |
| **PlcMsg** | String | (65 chars) | Full PLC message; position 33-62 contains item description |
| **ErrMsg** | String | "" or "ERROR: No item" | Error message; empty if OK |
| **PrintNum** | Integer | 1 | Print count; must be > 0 to print label |

**Format**: Comma-separated values (CSV)

**Header**: 
```
RecId,LineId,MakeTime,StackNum,PlcMsg,ErrMsg,PrintNum
```

**Example Data**:
```csv
RecId,LineId,MakeTime,StackNum,PlcMsg,ErrMsg,PrintNum
1001,1,2026-07-22 14:30:45," 1","(65 chars) item info at pos 33",""，1
1002,1,2026-07-22 14:30:47," 1","(65 chars) item info at pos 33","",1
1003,1,2026-07-22 14:30:49," 2","(65 chars) item info at pos 33","ERROR: No item",0
1004,1,2026-07-22 14:30:51," 3","(65 chars) item info at pos 33","",1
```

**Key Notes**:
- **Browse Panel** displays the last 12 boxes in reverse chronological order (newest first)
- Only boxes with `PrintNum > 0` and empty `ErrMsg` can print labels
- `StackNum` is always 2 characters (padded with space on left, e.g., " 1", " 9")
- `PlcMsg` is fixed at 65 characters, padded with spaces
- Timestamps must be in `YYYY-MM-DD HH:mm:ss` format (24-hour)

**Append Mode**: PLC continuously appends new records; UI reads last 12 and refreshes every 1 second.

---

## 2. stackers{NN}.csv — Stacker Configuration

**Purpose**: Defines the current stacker setup for each stack number on the line.

**Location**: `C:\LabelPrint\data\` (configurable in Settings)

**Filename Example**: 
- Line 01: `stackers01.csv`
- Line 12: `stackers12.csv`

**Columns** (in order):

| Column | Type | Example | Notes |
|--------|------|---------|-------|
| **LineId** | Integer | 1 | Production line number |
| **StackNum** | String | " 1" or " 9" | 2-character stack number (space-padded) |
| **IRef** | Integer | 42 | Item reference; links to `itemdet.csv` or `mitemdet.csv` |
| **PlcMsg** | String | "M-..." | PLC message (65 chars); position 33-34 indicates if Mexico item |
| **Shade** | Integer | 1234 | Shade code (1-4 digits depending on config) |
| **Size** | String | "L" | Size code (e.g., "L", "M", "S") |
| **ErrMsg** | String | "" or "*OK" | Status; empty or "*OK" = valid, otherwise error |

**Format**: Comma-separated values (CSV)

**Header**:
```
LineId,StackNum,IRef,PlcMsg,Shade,Size,ErrMsg
```

**Example Data**:
```csv
LineId,StackNum,IRef,PlcMsg,Shade,Size,ErrMsg
1," 1",42,"(65 chars with M- at pos 33)",1234,"L",""
1," 2",15,"(65 chars with M- at pos 33)",2,"M","*OK"
1," 3",0,"(65 chars)",0,"","ERROR: No item loaded"
1," 4",28,"(65 chars)",5,"S",""
```

**Key Notes**:
- **StackNum** matches the stack number in `boxes{NN}.csv`
- **IRef** is the lookup key: if `PlcMsg` position 33-34 == "M-", look in `mitemdet.csv`; otherwise look in `itemdet.csv`
- **IsValid** = (`ErrMsg` is empty OR starts with "*OK")
- **Shade**: 
  - If "4DIGITSHADE" flag is set, shade is 0000-9999 (4-digit raw)
  - If not set, shade × 10 (e.g., 2 → 20, 5 → 50)
- **Size**: Usually "L", "M", "S"; passed to item label determination

**Update Mode**: PLC updates stackers when operators reload items; UI reads on-demand during Reprint or item lookup.

---

## 3. itemdet.csv — US Item Master Data

**Purpose**: Item catalog for **US/non-Mexico items**. Lookup table by `IRef`.

**Location**: `C:\LabelPrint\data\` (configurable in Settings)

**Filename**: `itemdet.csv` (shared, not line-specific)

**Columns** (in order):

| Column | Type | Example | Notes |
|--------|------|---------|-------|
| **IRef** | Integer | 42 | Item reference ID (primary key) |
| **ItemNumber** | String | "SKU-12345" | Item/SKU number |
| **LisQty** | Integer | 24 | Quantity per label/case |

**Format**: Comma-separated values (CSV)

**Header**:
```
IRef,ItemNumber,LisQty
```

**Example Data**:
```csv
IRef,ItemNumber,LisQty
1,ABC-001,12
2,ABC-002,12
15,XYZ-100,24
28,PQR-050,6
42,LMN-999,12
99,UNKNOWN,0
```

**Key Notes**:
- Used when stacker `PlcMsg` position 33-34 is NOT "M-"
- `IRef` values must match those in `stackers{NN}.csv` / `boxes{NN}.csv`
- `ItemNumber` is displayed on labels and in the Browse grid
- `LisQty` = quantity per label (used for multi-part labels or case packing)
- If `IRef` not found, item displays as "(Unknown)"

---

## 4. mitemdet.csv — Mexico Item Master Data

**Purpose**: Item catalog for **Mexico items only**. Lookup table by `IRef`.

**Location**: `C:\LabelPrint\data\` (configurable in Settings)

**Filename**: `mitemdet.csv` (shared, not line-specific)

**Columns** (in order):

| Column | Type | Example | Notes |
|--------|------|---------|-------|
| **IRef** | Integer | 50 | Item reference ID (primary key) |
| **ItemNumber** | String | "MEX-12345" | Item/SKU number (Mexico variant) |
| **LisQty** | Integer | 12 | Quantity per label/case |

**Format**: Comma-separated values (CSV)

**Header**:
```
IRef,ItemNumber,LisQty
```

**Example Data**:
```csv
IRef,ItemNumber,LisQty
50,MEX-001,12
51,MEX-002,6
52,MEX-003,24
```

**Key Notes**:
- Used when stacker `PlcMsg` position 33-34 == "M-"
- Structure identical to `itemdet.csv`; kept separate for sourcing/audit purposes
- If Mexico mode is disabled (config flag `DoesMexico` = false), this file is not read
- `IRef` values in Mexico items can overlap with US items (IRef 50 can exist in both files)

---

## Complete Flow Example

### Scenario: Line 01, Stack 1, Item Lookup

**1. PLC writes to `boxes01.csv`:**
```csv
1001,1,2026-07-22 14:30:45," 1","65-char message with item at pos 33..."，""，1
```

**2. UI requests last 12 boxes → reads `boxes01.csv`**

**3. UI needs item details → reads `stackers01.csv`:**
```csv
1," 1",42,"M-...",1234,"L",""
```

**4. Stacker shows IRef=42, PlcMsg contains "M-" → Mexico item lookup**

**5. UI searches `mitemdet.csv` for IRef=42:**
```csv
42,MEX-12345,12
```

**6. UI displays:**
```
ItemDisplay = "MEX-12345-12-1234-L"
(ItemNumber-Qty-Shade-Size)
```

---

## File Creation & Maintenance

### Initial Setup

1. **Download item masters** from SAP/ERP → create `itemdet.csv` and `mitemdet.csv`
2. **Configure PLC** to write `boxes{NN}.csv` and `stackers{NN}.csv` to data directory
3. **Set path in Settings** dialog (⚙ button on startup screen):
   - CSV Directory: `C:\LabelPrint\data\`
   - Production Line: `01` (auto-creates `boxes01.csv`, `stackers01.csv`)

### Ongoing Operations

- **`boxes{NN}.csv`**: PLC appends new records; UI reads append-only
- **`stackers{NN}.csv`**: PLC updates when operators reload items; UI reads on-demand
- **`itemdet.csv`**: Update when new items are added to production (can add rows anytime)
- **`mitemdet.csv`**: Update when new Mexico items are added

### Best Practices

- ✅ Back up `.csv` files daily
- ✅ Use unique `IRef` values per item (coordinate with SAP)
- ✅ Keep `itemdet.csv` and `mitemdet.csv` in sync with SAP
- ✅ Monitor `boxes{NN}.csv` file size (archive when > 100 MB)
- ✅ Test PLC → CSV pipe before going live
- ❌ Do NOT edit files while system is running (PLC may corrupt locks)
- ❌ Do NOT use non-ASCII characters (breaks parsing)

---

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| "Item (Unknown)" in Browse | `IRef` not found in `.csv` | Check `IRef` value in stacker vs. itemdet/mitemdet |
| Browse shows no boxes | `boxes{NN}.csv` missing or empty | Check PLC is writing to correct path |
| Stack shows "ERROR: No item" | `ErrMsg` is not empty | Check stacker file or ask PLC to reset |
| Cannot print label | `PrintNum = 0` or `ErrMsg` not empty | Wait for PLC to update or manually reset in stacker |
| Mexico items not resolving | `PlcMsg` at pos 33-34 is not "M-" | Verify PLC is marking Mexico items correctly |
| Settings path is wrong | Path not configured in Settings | Click ⚙ Settings, enter correct path (default: `C:\LabelPrint\data\`) |

---

## File Format Specification

### CSV Rules

- **Delimiter**: Comma (`,`)
- **Encoding**: UTF-8 (no BOM)
- **Line Ending**: LF (`\n`) or CRLF (`\r\n`)
- **Comments**: Lines starting with `#` are ignored
- **Empty Lines**: Skipped
- **Whitespace**: Trimmed per field (leading/trailing spaces removed)
- **Strings**: No quotes needed unless containing comma or newline
- **Numbers**: Integers only (no decimals or currency symbols)

### Datetime Format

- **Format**: `YYYY-MM-DD HH:mm:ss` (ISO 8601)
- **Timezone**: UTC (assume system is UTC for consistency)
- **Example**: `2026-07-22 14:30:45`

---

## Settings Integration

The **⚙ Settings** dialog on the startup screen allows configuration:

1. **CSV Directory Path** → where all `.csv` files are located
2. **Production Line Number** → determines which `boxes{NN}.csv` / `stackers{NN}.csv` to read
3. **PLC Connection Type** → Serial Port or IP (for future integration)
4. **Label Output Type** → NicelabelXml, SerialPort, HttpPost (for label printer integration)

Changes are saved to `carton-settings.json` and persisted across restarts.

---

## Questions?

For help:
- Check the **Browse** panel to verify boxes are being received
- Review `NoGo{NN}.txt` if the system shows "Stopped" state
- Check `carton-settings.json` to verify paths are correct
- Contact the PLC/sorter team if boxes are not appearing

