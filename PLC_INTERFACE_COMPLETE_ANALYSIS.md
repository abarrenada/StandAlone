# PLC Interface Analysis — Complete Documentation

## Executive Summary

The StandAlone project implements a .NET port of the Progress-based label printing system used with tile sorting equipment. The project targets **Nuovofina sorters** (primary equipment), but the architecture supports adaptation to other sorter types with minimal configuration changes.

This document provides complete technical analysis of all available PLC interface options, data formats, and implementation guidance.

---

## Part 1: Equipment Types and Programs

### 1.1 Nuovofina Sorters (PRIMARY — Currently Implemented)

**Primary Production Equipment**

| Aspect | Details |
|--------|---------|
| **Programs** | dtplc041, 065, 066, 067, 067a, 076, 076_do |
| **Current Version** | dtplc067.p (production 2007+) |
| **Data Format** | 65-character messages with dual format support |
| **Configuration** | 7 flag files (LineNumber, DoesManStk, DoesTwoPrims, etc.) |
| **Message Types** | Format 1 (manual/reprint) + Format 2 (PLC) |
| **Item Parsing** | Yes — automatically extracts item, quantity, shade, caliber |
| **Status** | ✅ **Production-Ready** |

**Supported Features:**
- Dynamic primary and secondary item selection
- Mexico-only item differentiation
- 4-digit shade codes
- Multiple label size options
- Automatic stacker stack-down on empty stacks
- Reprint with label variable rebuild

**File Locations:**
```
Flag files (workspace root):
  DoesManStk              — Line number (enables primary item field)
  DoesTwoPrims            — Line number (enables 2nd primary field)
  LineNumber              — Line number for this sorter
  MEXICO-ONLY             — Presence enables Mexico items
  4DIGITSHADE             — Presence enables 4-digit shades
  LabelSzPrmpt            — Label size definitions
  HOSTNAME                — Host/plant identifier

Data files (data/ directory):
  boxes01.csv            — Carton history
  stackers01.csv         — Stack configuration
  itemdet.csv            — US items (43 columns)
  mitemdet.csv           — Mexico items (43 columns)
  plc01                  — PLC pipe (bidirectional)
  VfyScn/                — Verification/cleanup logs
```

---

### 1.2 System Sorters (LEGACY — Adaptable)

**Older Equipment Type**

| Aspect | Details |
|--------|---------|
| **Program** | dtplc060.p |
| **Data Format** | 65-character messages, Format 1 only |
| **Configuration** | Minimal flag files |
| **Message Types** | Manual/reprint format only (no PLC Format 2) |
| **Item Parsing** | Yes — but simpler extraction |
| **Status** | ⚠️ Requires config switch, same message structure |

**Differences from Nuovofina:**
- No Format 2 support (PLC doesn't send item-level detail)
- Simpler label variable reconstruction
- All records treated as manual input
- Less sophisticated error handling

**Adaptation Path:**
```
1. Add equipment type selector to CartonAppConfig
2. Create dtplc060-compatible message parser
3. Disable Format 2 item parsing when System Sorter selected
4. All 65-char message positioning identical
```

---

### 1.3 SATI Sorters (LEGACY — Minimal Adaptation)

**Alternative Equipment (MIX/ROTOMIX Emulation)**

| Aspect | Details |
|--------|---------|
| **Program** | dtplc043.p |
| **Data Format** | 65-character messages |
| **Configuration** | Minimal |
| **Message Types** | PLC direct format only |
| **Item Parsing** | No — stack number hardcoded "??" |
| **Status** | ⚠️ Requires config switch, different stack handling |

**Special Characteristics:**
- Stack number not parsed from message
- All received messages treated as direct PLC data
- Simpler stacker lookup (no stack number match)
- Different physical equipment capabilities

**Adaptation Path:**
```
1. Add SATI equipment type to CartonAppConfig
2. Skip stack number parsing (use "??")
3. Match by line and item only (ignore stack)
4. Use default stack for printnum increment
```

---

### 1.4 Private Label System (OUTPUT-ONLY — Different Architecture)

**Command-Based Interface**

| Aspect | Details |
|--------|---------|
| **Program** | dtplc045.p |
| **Direction** | SENDS TO PLC (reverse flow) |
| **Data Format** | Command-based (varies) |
| **Configuration** | Device script + command definition |
| **Message Types** | Reprint commands, status queries |
| **Item Parsing** | N/A (output only) |
| **Status** | ❌ Requires reverse-flow architecture |

**Characteristics:**
- Sends label requests TO the PLC
- Receives responses back (bidirectional)
- Command type varies by device (serial, socket, shell script)
- Not suitable for current implementation without major redesign

**Why Not Current Focus:**
- Requires complete architecture change
- No real-time box/stacker record creation
- Different workflow paradigm
- Would be separate module entirely

---

## Part 2: Universal Message Format

### 2.1 Core Specification

**All receiving sorter types use identical message structure:**

```
Total Length:       65 characters (fixed)
Encoding:           ASCII
Separator:          Hyphen "-" (field delimiter)
Terminator:         Newline (implicit in file write)
```

**Position Breakdown (1-indexed notation):**

| Position | Length | Content | Example |
|----------|--------|---------|---------|
| 0-1      | 2      | Stack number (formatted) | " 1" |
| 2-31     | 30     | Filler/padding (spaces) | [31 spaces] |
| 32-61    | 30     | Item information | "FL9036MOD1P4       " |
| 62-65    | 4+     | Quantity/Shade/Caliber | "2-R-1" |

**Visual Example:**

```
Position: 0   10   20   30   40   50   60   65
          |   |    |    |    |    |    |    |
Data:     " 1[31 spaces]FL9036MOD1P4          2  R  1"
          ^^                ^^^^^^^^^^^^^^     ^ ^ ^
          Stack            Item Number        Q S C
```

**Formula for Construction (C# / 0-indexed):**

```csharp
string stackNum = $" {stackNumber}";                           // 2 chars
string plcMsg = stackNum + 
                new string(' ', 31) +                          // 31 spaces
                itemNumber.PadRight(30) +                      // 30 chars
                quantity.ToString().PadLeft(2) +               // 2 chars
                shade.ToString().PadLeft(1) +                  // 1 char
                caliber.ToString().PadLeft(1);                 // 1 char
if (plcMsg.Length < 65) plcMsg = plcMsg.PadRight(65);          // Pad to 65
```

---

### 2.2 Data Format Variants

#### **Format 1: Manual/Reprint (From Other Programs)**

Used by: All sorter types
Triggered by: F1 Reprint, F3 Reprint by Stack, manual entry

```
Structure: stacker#(99), reprint(X), shift(9), Inspec(XX)
Example:   " 1,Y,1,AB"
Positions: 0-1 (stack) / 2 (reprint flag) / 3 (shift) / 4-5 (inspector)
```

**Processing:**
- Extract stack number from position 0-1
- Check position 2 for reprint flag (Y = reprint, blank = new read)
- Extract shift from position 3
- Extract inspector from positions 4-5
- Do NOT extract item (use existing stacker record item)

---

#### **Format 2: PLC Direct (From Sorter PLC)**

Used by: Nuovofina sorters (dtplc065, 066, 067, 076)
Not used by: System Sorters (dtplc060), SATI (dtplc043)
Triggered by: Physical sorter reading carton stack

```
Structure: stacker#(99), filler, Item-CtnQty-Shade-Calbr
Example:   " 1 FL9036MOD1P4-2-R-1"
Positions: 0-1 (stack) / 2-31 (spaces) / 32-61 (item) / 62-65 (metadata)
```

**Processing:**
1. Extract stack number: position 0-1 → formatted 2-char string
2. Extract item number: position 32-61 → lookup in itemdet.csv
3. Extract quantity: extract from parsed item portion (between hyphens)
4. Extract shade: numeric value (between hyphens)
5. Extract caliber: letter code (between hyphens)
6. Create box record with good read (ErrMsg = empty)
7. Update stacker record with new item/quantity/shade/caliber
8. Trigger label print immediately

**Error Handling in Format 2:**
- Item not found → Set ErrMsg, do NOT print
- Quantity parsing fails → Set ErrMsg, do NOT print
- Stack number missing → Set ErrMsg, do NOT print

---

#### **Format 2 Extended: Language Flag (Nuovofina dtplc076 only)**

```
Structure: stacker#(99), filler, Item-CtnQty-Shade-Calbr-LangFlag
Example:   " 1 FL9036MOD1P4-2-R-1-EN"
Positions: Same as Format 2, plus language at end (positions 65+)
```

**Processing:**
- Same as Format 2, but also extract language code at end
- Allows per-carton language selection
- Not currently implemented in .NET version (use Format 2)

---

## Part 3: Box and Stacker Record Structure

### 3.1 Box Record (CSV Format)

**File Name:** `boxes{NN}.csv` (where NN = line number, zero-padded)

**Record Structure:**

| Field | Position | Type | Length | Source | Example |
|-------|----------|------|--------|--------|---------|
| RecId | 1 | Integer | Variable | Auto-increment | 1 |
| LineId | 2 | String | 2 | Config | "01" |
| MakeTime | 3 | DateTime | Variable | Current time | "2026-07-25 14:30:45" |
| StackNum | 4 | String | 2-3 | Parsed msg | " 1" |
| PlcMsg | 5 | String | 65 | Full message | "[65-char msg]" |
| ErrMsg | 6 | String | Variable | Empty or error | "" or "*ERR-Item not found" |
| PrintNum | 7 | Integer | 1 | Print count | 1 |

**CSV Line Example:**

```
1,01,2026-07-25 14:30:45," 1","[65-char plcmsg]","",1
```

**Field Notes:**
- **RecId:** Monotonically increasing, unique per line
- **LineId:** From config (DoesManStk or LineNumber flag file)
- **MakeTime:** Unique timestamp; incremented if duplicate time exists
- **StackNum:** Extracted from PlcMsg positions 0-1
- **PlcMsg:** Complete 65-character message (never truncated)
- **ErrMsg:** Empty = valid/good read (triggers print); non-empty = error (no print)
- **PrintNum:** Incremented by label printing system

**Print Eligibility Logic:**

```
CanPrint = (PrintNum > 0) AND (ErrMsg is empty or null)
```

---

### 3.2 Stacker Record (CSV Format)

**File Name:** `stackers{NN}.csv` (where NN = line number, zero-padded)

**Record Structure:**

| Field | Position | Type | Length | Source | Example |
|-------|----------|------|--------|--------|---------|
| LineId | 1 | String | 2 | Config | "01" |
| StackNum | 2 | String | 2-3 | Parsed msg | " 1" |
| IRef | 3 | Integer | Variable | Item lookup | 1234 |
| PlcMsg | 4 | String | 65 | Full message | "[65-char msg]" |
| Shade | 5 | Integer | 1-2 | Item lookup | 5 |
| Size | 6 | String | 1 | Item lookup | "L" |
| ErrMsg | 7 | String | Variable | Status | "*OK 1 FL9036..." |

**CSV Line Example:**

```
01," 1",1234,"[65-char plcmsg]",5,"L","*OK  1 FL9036MOD1P4"
```

**Field Notes:**
- **LineId:** From config
- **StackNum:** Stack identifier (linked to box records)
- **IRef:** Item reference number (primary key from itemdet.csv)
- **PlcMsg:** Complete message for audit trail
- **Shade:** Numeric shade code (0-9 or 00-99 if 4DIGITSHADE enabled)
- **Size:** Single character size code (1-9 or custom from LabelSzPrmpt)
- **ErrMsg:** Status indicator; "*OK" means valid stacker

**Stacker Lookup Logic:**

```csharp
// Find stacker by line and stack number
stacker = stackers
    .Where(s => s.LineId == currentLine && s.StackNum == stackFromMessage)
    .FirstOrDefault();

if (stacker != null && !string.IsNullOrEmpty(stacker.IRef))
{
    // Use stacker's item details for label printing
    itemDetail = itemDictionary[stacker.IRef];
}
else
{
    // Stacker not found or invalid; handle error
}
```

---

### 3.3 Item Detail Record (CSV Format)

**File Names:** `itemdet.csv` (US), `mitemdet.csv` (Mexico)

**Total Columns:** 43 (combined itemdet + itemhdr tables)

**Key Columns:**

| Column | Position | Type | Example | Source Table |
|--------|----------|------|---------|---------------|
| IRef | 1 | Integer | 1234 | itemdet |
| ItemNumber | 2 | String | FL9036MOD1P4 | itemdet |
| LisQty | 3 | Integer | 2 | itemdet |
| Shade | 4 | Integer | 5 | itemdet |
| ColorDesc | 5 | String | WHITE | itemhdr |
| ShapeDesc | 6 | String | 3 X 6 X 0.31 IN | itemhdr |
| SeriesDesc | 7 | String | FINISH LINE | itemhdr |
| Brand | 8 | String | FINISH LINE | itemhdr |
| (34 more columns) | 9-43 | Various | (see CSV_FILES_GUIDE.md) | itemdet/itemhdr |

**Lookup Pattern (LINQ):**

```csharp
// Find by item number + quantity
var item = itemDetails
    .Where(i => i.ItemNumber == "FL9036MOD1P4" && i.LisQty == 2)
    .FirstOrDefault();

// Mexico alternate
if (item == null && doesMexico)
{
    item = mexicoItems
        .Where(i => i.ItemNumber == "FL9036MOD1P4" && i.LisQty == 2)
        .FirstOrDefault();
}
```

**Display Format (UI):**

```
{ColorDesc} | {ShapeDesc} | {SeriesDesc}
WHITE | 3 X 6 X 0.31 IN | FINISH LINE
```

---

## Part 4: Implementation Guidance

### 4.1 Nuovofina Implementation (Current)

**Status:** ✅ Production-Ready

**Message Processing Flow:**

```
1. PLC writes carton to plc01 pipe
2. App reads pipe line: " 1 FL9036MOD1P4-2-R-1"
3. Detect Format 2 (has item at position 32)
4. Extract:
   - Stack: " 1"
   - Item: "FL9036MOD1P4"
   - Qty: "2"
   - Shade: "R"
   - Caliber: "1"
5. Lookup itemdet.csv for IRef, size
6. Create box record (7 fields, ErrMsg empty)
7. Create/update stacker record (7 fields)
8. Trigger F1 print with item details
9. Export to NiceLabel XML
10. Send to printer
```

**Configuration Required:**

```
Flag file: DoesManStk (contains "01")
CSV files:
  - boxes01.csv (written by app + PLC)
  - stackers01.csv (written by app + PLC)
  - itemdet.csv (static, 43 columns)
  - mitemdet.csv (if doesMexico=true)
JSON:
  - carton-settings.json (persists primary items)
```

**No Additional Code Changes Needed** — All features working.

---

### 4.2 System Sorters Implementation (if needed)

**Status:** ⚠️ Requires Configuration Switch

**Message Processing Flow:**

```
1. PLC writes carton to plc01 pipe
2. App reads pipe line: " 1,Y,1,AB"
3. Detect Format 1 (no item at position 32)
4. Extract:
   - Stack: " 1"
   - Reprint flag: "Y"
   - Shift: "1"
   - Inspector: "AB"
5. Skip item parsing (use existing stacker)
6. If reprint="Y": Reload stacker item, rebuild labels
7. If reprint blank: Create new box record
8. Trigger print if applicable
```

**Code Changes Required:**

```csharp
// 1. Add equipment type to CartonAppConfig
public enum EquipmentType { Nuovofina, SystemSorter, SATI }
public EquipmentType Equipment { get; set; }

// 2. Add detection logic
if (message[32] == ' ' || equipment == EquipmentType.SystemSorter)
{
    // Format 1 processing
    ParseFormat1(message);
}
else
{
    // Format 2 processing
    ParseFormat2(message);
}

// 3. Skip item parsing for Format 1
private void ParseFormat1(string message)
{
    stackNum = message.Substring(0, 2);
    reprintFlag = message[3];
    shift = message[4];
    inspector = message.Substring(5, 2);
    // NO ITEM PARSING
}
```

**File Structure:** Same as Nuovofina (no changes needed)

---

### 4.3 SATI Implementation (if needed)

**Status:** ⚠️ Requires Configuration Switch

**Message Processing Flow:**

```
1. PLC writes carton to plc01 pipe
2. App reads pipe line: " ? FL9036MOD1P4-2-R-1"
3. Detect SATI equipment type
4. Extract:
   - Stack: "??" (not parsed, always unknown)
   - Item: "FL9036MOD1P4"
   - Qty: "2"
   - Shade: "R"
   - Caliber: "1"
5. Lookup itemdet.csv for IRef, size
6. Create box record with StackNum="??"
7. Lookup stacker by LINE+ITEM (ignore stack number)
8. If stacker not found, use default stacker
9. Trigger print
```

**Code Changes Required:**

```csharp
// 1. Add equipment type enum
public enum EquipmentType { Nuovofina, SystemSorter, SATI }

// 2. Skip stack parsing for SATI
private void ParseStackNumber(string message)
{
    if (equipment == EquipmentType.SATI)
    {
        stackNum = "??";  // Unknown
    }
    else
    {
        stackNum = message.Substring(0, 2);
    }
}

// 3. Change stacker lookup for SATI
private StackerRecord FindStacker(string item, string line)
{
    if (equipment == EquipmentType.SATI)
    {
        // Lookup by line + item only
        return stackers
            .Where(s => s.LineId == line && s.ItemNumber == item)
            .FirstOrDefault();
    }
    else
    {
        // Lookup by line + stack
        return stackers
            .Where(s => s.LineId == line && s.StackNum == currentStack)
            .FirstOrDefault();
    }
}
```

**File Structure:** Same as Nuovofina (no changes needed)

---

### 4.4 Private Label Implementation (Not Recommended Currently)

**Status:** ❌ Different Architecture Required

**Why Not Current Scope:**
1. Requires reverse data flow (send TO PLC)
2. Message format varies by device
3. No real-time box/stacker recording
4. Would need separate module architecture
5. Different UI paradigm (commands instead of monitoring)

**If Future Requirement:**
- Create separate `StandAlone.PrivateLabelInterface` project
- Implement command builder instead of message parser
- Bidirectional socket/serial communication
- Command queue system for batch operations

---

## Part 5: Current Implementation Status

### 5.1 Nuovofina (dtplc067.p) — 100% Aligned ✅

**Validation Results:**

| Aspect | Specification | Implementation | Status |
|--------|---------------|-----------------|--------|
| Message length | 65 chars | 65 chars | ✅ |
| Stack position | 0-1 | 0-1 | ✅ |
| Item position | 32-61 (0-idx) | 32-61 (0-idx) | ✅ |
| Box record fields | 7 fields | 7 fields | ✅ |
| Stacker record fields | 7 fields | 7 fields | ✅ |
| ErrMsg semantics | Empty=good | Empty=good | ✅ |
| Item lookup | Yes | Yes | ✅ |
| Flag file support | 7 files | 7 files | ✅ |
| Mexico items | Yes | Yes | ✅ |
| 4-digit shades | Yes | Yes | ✅ |

**Conclusion:** Production-ready. No changes needed.

---

### 5.2 System Sorters (dtplc060.p) — Adaptable ⚠️

**Estimated Effort:** 2-4 hours
- Add EquipmentType enum to config
- Add format detection logic (item at position 32)
- Add Format 1 parser
- Test with System Sorter message samples

**No file structure changes needed** — CSV format identical.

---

### 5.3 SATI (dtplc043.p) — Adaptable ⚠️

**Estimated Effort:** 1-2 hours
- Add EquipmentType enum to config
- Skip stack parsing (always "??")
- Change stacker lookup (line + item instead of line + stack)
- Test with SATI message samples

**No file structure changes needed** — CSV format identical.

---

### 5.4 Private Label (dtplc045.p) — New Architecture ❌

**Estimated Effort:** 20-40 hours
- Separate project/module required
- Command builder instead of parser
- Bidirectional communication
- Different data flow model
- UI redesign (commands vs monitoring)

**Recommendation:** Defer to Phase 2 if needed.

---

## Part 6: Configuration File Reference

### 6.1 Flag Files

Located in workspace root (parent of data/ directory)

**LineNumber** (Required)
```
Format: Single line with 2-digit line number
Content: 01
Purpose: Identifies this sorter line
```

**DoesManStk** (Enables Primary Item field)
```
Format: Single line with 2-digit line number
Content: 01
Purpose: Shows Primary Item TextBox + async lookup in UI
Effect: If absent, Primary Item field hidden
```

**DoesTwoPrims** (Enables Secondary Item field)
```
Format: Single line with 2-digit line number
Content: 01
Requirement: DoesManStk must also exist
Purpose: Shows both Primary Item and 2nd Primary Item fields
```

**MEXICO-ONLY** (Enables Mexico items)
```
Format: Presence/absence (no content required)
Content: (any content, file just needs to exist)
Purpose: Enables lookup in mitemdet.csv for Mexico-only items
```

**4DIGITSHADE** (Enables 4-digit shade codes)
```
Format: Presence/absence (no content required)
Content: (any content, file just needs to exist)
Purpose: Allows shade values 0000-9999 instead of 0-9
```

**LabelSzPrmpt** (Defines label sizes)
```
Format: Multiple lines, each line = one label size
Content:
  1,4.5x3,dtlbl060b.i,10
  2,2x7.25,dtlbl060d.i,20
Purpose: Maps size code to filename, prompt size
Usage: Populated by UI in "Select Label Size" combo
```

**HOSTNAME** (Host identifier for multi-plant setup)
```
Format: Single line with hostname
Content: PLANT_NAME or SERVER_NAME
Purpose: Used in logging, error reporting
```

### 6.2 CSV File Formats

#### **boxes{NN}.csv**

```
RecId,LineId,MakeTime,StackNum,PlcMsg,ErrMsg,PrintNum
1,01,2026-07-25 14:30:45," 1","[65-char msg]","",1
2,01,2026-07-25 14:31:02," 2","[65-char msg]","*ERR-Item not found",0
```

#### **stackers{NN}.csv**

```
LineId,StackNum,IRef,PlcMsg,Shade,Size,ErrMsg
01," 1",1234,"[65-char msg]",5,"L","*OK  1 FL9036MOD1P4"
01," 2",5678,"[65-char msg]",3,"M",""
```

#### **itemdet.csv** (43 columns)

```
IRef,ItemNumber,LisQty,Shade,ColorDesc,ShapeDesc,SeriesDesc,Brand,...
1234,FL9036MOD1P4,2,5,WHITE,3 X 6 X 0.31 IN,FINISH LINE,FINISH LINE,...
5678,LMN-999,1,3,CLASSIC WHITE,12 X 12 IN,STANDARD SERIES,STANDARD,...
```

### 6.3 JSON Configuration

**carton-settings.json** (Auto-created in workspace root)

```json
{
  "CurrentPrimaryItem": "FL9036MOD1P4",
  "CurrentSecondaryItem": ""
}
```

**Purpose:** Persist user-selected items across sessions
**Modified By:** MainForm (startup panel)
**Read By:** SettingsManager.Load() at app startup

---

## Part 7: Testing Checklist

### 7.1 Nuovofina Sorter (Production System)

**Startup Configuration:**
- [ ] Shift: 1
- [ ] Inspector: TEST
- [ ] Label Size: (select available)
- [ ] Primary Item: FL9036MOD1P4
- [ ] Click "Begin"

**Browse Panel:**
- [ ] Grid shows last 5 boxes
- [ ] Item descriptions display correctly
- [ ] Primary item persists in settings file

**Simulate Carton Read:**
- [ ] Click "Simulate" button (bottom-right)
- [ ] New carton appears in grid row 0
- [ ] Item shows with description (green text = success)
- [ ] F1 auto-triggers and label prints

**Error Cases:**
- [ ] Invalid item (INVALID123) shows error in red
- [ ] Empty item field shows warning
- [ ] Non-existent CSV files create directories automatically

**F-Key Functions:**
- [ ] F1: Reprint selected (triggers label print)
- [ ] F3: Reprint by stack (dialog appears)
- [ ] F4: New setup (shows startup panel)
- [ ] F6: Stop reason (shows message if available)
- [ ] F7: Event log (shows log file content)
- [ ] F8: Statistics (shows report file content)

---

### 7.2 System Sorters (if implemented)

**Setup:**
1. Add EquipmentType selector to SettingsForm
2. Set to "System Sorter"
3. Restart app

**Test Messages:**
```
Format 1: " 1,Y,1,AB"  (reprint, shift 1, inspector AB)
Format 1: " 2, ,2,CD"  (new, shift 2, inspector CD)
```

**Validation:**
- [ ] No item parsing attempted
- [ ] Uses existing stacker item for label
- [ ] Reprint flag honored

---

### 7.3 SATI Sorters (if implemented)

**Setup:**
1. Add EquipmentType selector to SettingsForm
2. Set to "SATI Sorter"
3. Restart app

**Test Messages:**
```
Format 2: "?? FL9036MOD1P4-2-R-1"  (item-based, no stack)
```

**Validation:**
- [ ] Stack shows as "??"
- [ ] Item parsing works normally
- [ ] Stacker lookup by item (not stack)
- [ ] Label prints correctly

---

## Part 8: Troubleshooting Guide

### 8.1 Item Not Found

**Symptom:** Red error text "Item 'XXXX' not found in item master"

**Causes:**
1. Item doesn't exist in itemdet.csv
2. Mexico item searched but MEXICO-ONLY flag not present
3. CSV parsing failed (malformed line)

**Solution:**
1. Verify item in itemdet.csv (grep/search)
2. Check MEXICO-ONLY flag file exists (if Mexico item)
3. Check CSV file format (commas, quotes)
4. Verify data directory path in carton-settings.json

---

### 8.2 Simulate Button Doesn't Create Records

**Symptom:** Simulate button clicked but no new row appears

**Causes:**
1. CSV files not writable (permissions)
2. Data directory doesn't exist
3. File path parsing failed

**Solution:**
1. Check data/ directory exists and is writable
2. Check file permissions (not read-only)
3. Check carton-settings.json for correct path
4. Look at console output for error messages

---

### 8.3 Labels Print But Item Field Wrong

**Symptom:** Carton prints but shows wrong item or blank item

**Causes:**
1. IRef lookup failed
2. Shade/Size fields empty
3. Item description formatting error

**Solution:**
1. Check itemdet.csv has IRef populated
2. Verify Shade and Size columns have values
3. Check stacker record has correct IRef value
4. Validate XML export includes all 43 fields

---

### 8.4 Browse Panel Empty After Simulate

**Symptom:** Simulate succeeds but browse grid stays empty

**Causes:**
1. Refresh timer hasn't fired yet
2. Box file path incorrect
3. CSV parsing failed

**Solution:**
1. Wait 2-3 seconds (refresh interval)
2. Manually click Begin to refresh
3. Check boxes01.csv file exists and has records
4. Check console for parsing errors

---

## Part 9: Production Deployment

### 9.1 File Structure Setup

```
C:\LabelPrint\                          (Installation directory)
├── StandAlone.CartonUi.exe       (Main application)
├── StandAlone.CartonUi.dll       (Framework libraries)
├── config\
│   ├── LineNumber                      (Flag file: "01")
│   ├── DoesManStk                      (Flag file: "01")
│   ├── DoesTwoPrims                    (Flag file: "01" - optional)
│   ├── MEXICO-ONLY                     (Flag file - optional)
│   ├── 4DIGITSHADE                     (Flag file - optional)
│   └── LabelSzPrmpt                    (Label size definitions)
├── data\
│   ├── boxes01.csv                     (Auto-created by PLC)
│   ├── stackers01.csv                  (Auto-created by PLC)
│   ├── itemdet.csv                     (Import from SAP/ERP)
│   ├── mitemdet.csv                    (Import if Mexico items needed)
│   └── VfyScn\                         (Auto-created)
└── carton-settings.json                (Auto-created)
```

### 9.2 Data Import from SAP/ERP

**itemdet.csv** (43 columns from Progress tables):

```sql
-- SQL to export from SAP/Oracle
SELECT 
  itemdet.iref,
  itemdet.item_number,
  itemdet.lis_qty,
  itemdet.shade,
  itemhdr.color_desc,
  itemhdr.shape_desc,
  -- ... 37 more columns
FROM itemdet 
JOIN itemhdr ON itemdet.iref = itemhdr.iref
ORDER BY itemdet.item_number
INTO OUTFILE 'itemdet.csv'
FIELDS TERMINATED BY ',' 
ENCLOSED BY '"';
```

### 9.3 PLC Configuration

**Ensure PLC writes to correct CSV files:**

```
boxes01.csv  — Should match {workspace_root}/data/boxes01.csv
stackers01.csv — Should match {workspace_root}/data/stackers01.csv
plc01 pipe — Should be {workspace_root}/data/plc01
```

**Verify PLC data format:**

```
Format: " 1 FL9036MOD1P4-2-R-1" (65 chars, hyphen-separated)
Stack: Positions 0-1
Item: Positions 32-61
Write frequency: Real-time (one line per carton read)
```

### 9.4 Label Printer Configuration

**NiceLabel XML Export:**

1. Ensure NiceLabel printer driver installed
2. Create label template for each size (4.5x3, 2x7.25, etc.)
3. Map to LabelSzPrmpt entries
4. Test print from application

**Alternative: Serial/Network printer:**

1. Configure in application settings (future enhancement)
2. Test print handshake
3. Validate label format on physical media

---

## Part 10: Summary and Recommendations

### 10.1 Current Status

✅ **Nuovofina Sorters**: Production-ready
- Fully implemented and tested
- 100% aligned with dtplc067.p
- All features working (simulate, F1-F8, item lookup, Mexico items)

---

### 10.2 Future Enhancements (Phase 2)

⚠️ **System Sorters**: Low effort, quick ROI
- Add EquipmentType enum
- Add Format 1 parser
- Estimated 2-4 hours

⚠️ **SATI Sorters**: Low effort, niche market
- Add EquipmentType enum
- Skip stack parsing
- Estimated 1-2 hours

❌ **Private Label**: High effort, different architecture
- Requires separate module
- Defer to Phase 3 if demand exists
- Estimated 20-40 hours

---

### 10.3 Key Design Decisions

1. **CSV-Based Data Layer**: Matches PLC pipe writing, simpler than database
2. **Async Item Lookup**: Responsive UI, no blocking on slow CSV reads
3. **Flag File Configuration**: Matches legacy system, easy to deploy
4. **Dual-Panel UI**: Startup form → Browse form (matches Progress dtlbl067.p)
5. **Format 1 + Format 2**: Supports both manual and PLC data

---

### 10.4 Deployment Recommendation

**Phase 1 (Current):** Nuovofina sorter only
- Ship production version
- Deploy to line 01 (test environment)
- Validate with real sorter data
- Train operators

**Phase 2 (Q4 2026):** Add System Sorters + SATI
- If demand exists
- Minimal code changes
- Backward compatible

**Phase 3 (2027):** Private Label
- If business case justified
- New separate module
- Different deployment model

---

## Part 11: Technical References

### 11.1 Legacy Source Programs

| Program | Purpose | Status |
|---------|---------|--------|
| dtlbl067.p | Label printing logic | Blueprint for CartonUi |
| dtplc067.p | PLC interface (Nuovofina) | Message format spec |
| dtplc060.p | PLC interface (System) | Alternative format |
| dtplc043.p | PLC interface (SATI) | Alternative format |
| dtplc045.p | Private label output | Reverse-flow (deferred) |

**Access:** `C:\workspace\southalr\dtplc*.p`

### 11.2 Data Schema

**Progress Tables (Source):**
```
box              → boxes01.csv (7 fields)
stacker          → stackers01.csv (7 fields)
itemdet          → itemdet.csv (cols 1-20)
itemhdr          → itemdet.csv (cols 21-43)
```

**Note:** Combined into single CSV for .NET compatibility

### 11.3 Message Format References

**Nuovofina Format 2 (Item-based):**
- Position 0-1: Stack (e.g., " 1")
- Position 2-31: Filler (31 spaces)
- Position 32-61: Item number (30 chars)
- Position 62-63: Quantity (2 digits)
- Position 64: Shade (1 char or number)
- Position 65: Caliber (1 char)

**Example:** `" 1" + [31 spaces] + "FL9036MOD1P4       " + "2" + "R" + "1"`

---

## Part 12: Label Output Methods

This section documents all four label printing output options supported by the CartonUi application.

### 12.1 NiceLabel XML Export (Recommended)

**Status:** ✅ Production-Ready

**Overview:**
Exports label data to NiceLabel XML format, allowing the NiceLabel Print Server (or desktop application) to handle printing independently. This is the most flexible and recommended method.

**Architecture:**

```
CartonUi Application
    ↓ (Build Label Data)
XML Generator
    ↓ (Create XML file)
NiceLabel XML File
    ↓ (Write to file)
Output Directory: data/Labels/
    ↓ (Print Server watches directory)
NiceLabel Print Server
    ↓ (Auto-process XML)
Network Printer
    ↓
Physical Label
```

**XML File Structure:**

```xml
<?xml version="1.0" encoding="utf-8"?>
<Document xmlns="http://www.nicelabel.com/2011/LabelDefinition">
  <LabelData>
    <Item name="ItemNumber" value="FL9036MOD1P4" />
    <Item name="Description" value="FL90-WHITE | 3 X 6 X 0.31 IN | FINISH LINE" />
    <Item name="Quantity" value="2" />
    <Item name="Shade" value="R" />
    <Item name="StackNumber" value="1" />
    <Item name="ShiftNumber" value="1" />
    <Item name="InspectorInitials" value="AB" />
    <Item name="PrintTime" value="2026-07-25 14:30:45" />
    <!-- Additional 36 fields from itemdet.csv -->
  </LabelData>
</Document>
```

**Key Fields:**

| Field | Type | Length | Source | Example |
|-------|------|--------|--------|---------|
| ItemNumber | String | 20 | itemdet.csv | FL9036MOD1P4 |
| Description | String | 100 | formatted | WHITE \| 3 X 6 X 0.31 IN \| FINISH LINE |
| Quantity | Integer | 2 | itemdet.csv | 2 |
| Shade | String | 2 | itemdet.csv | R |
| StackNumber | String | 2 | plc_msg | 1 |
| ShiftNumber | Integer | 1 | startup | 1 |
| InspectorInitials | String | 2 | startup | AB |
| PrintTime | DateTime | Variable | current | 2026-07-25 14:30:45 |
| (34 more fields) | Various | (see itemdet.csv) | itemdet.csv | (various) |

**Implementation (C#):**

```csharp
public class NiceLabelXmlExporter
{
    public string GenerateXml(BoxRecord box, StackerRecord stacker, ItemDetail item)
    {
        var doc = new XDocument(
            new XElement("Document",
                new XAttribute("xmlns", "http://www.nicelabel.com/2011/LabelDefinition"),
                new XElement("LabelData",
                    new XElement("Item", new XAttribute("name", "ItemNumber"), 
                        new XAttribute("value", item.ItemNumber)),
                    new XElement("Item", new XAttribute("name", "Description"), 
                        new XAttribute("value", 
                            $"{item.ColorDesc} | {item.ShapeDesc} | {item.SeriesDesc}")),
                    new XElement("Item", new XAttribute("name", "Quantity"), 
                        new XAttribute("value", item.LisQty.ToString())),
                    new XElement("Item", new XAttribute("name", "Shade"), 
                        new XAttribute("value", stacker.Shade.ToString())),
                    new XElement("Item", new XAttribute("name", "StackNumber"), 
                        new XAttribute("value", stacker.StackNum.Trim())),
                    new XElement("Item", new XAttribute("name", "PrintTime"), 
                        new XAttribute("value", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")))
                    // ... add remaining 37 fields
                )
            )
        );
        
        return doc.ToString();
    }

    public void ExportToFile(string xmlContent, string outputDir, string filename)
    {
        Directory.CreateDirectory(outputDir);
        string filepath = Path.Combine(outputDir, filename);
        File.WriteAllText(filepath, xmlContent);
    }
}
```

**Configuration (carton-settings.json):**

```json
{
  "PrinterConfig": {
    "Method": "NiceLabelXml",
    "OutputDirectory": "C:\\LabelPrint\\data\\Labels",
    "TemplateFile": "carton-label-template.nlbl",
    "RetryAttempts": 3,
    "RetryDelayMs": 1000
  }
}
```

**Deployment:**

1. Install NiceLabel Print Server on separate machine or same machine
2. Configure NiceLabel to watch `C:\LabelPrint\data\Labels\` directory
3. Create label template file (carton-label-template.nlbl) with all 43 fields
4. Update carton-settings.json with OutputDirectory path
5. Test: Generate label XML manually and verify Print Server picks it up

**Error Handling:**

```csharp
try
{
    var xmlContent = exporter.GenerateXml(box, stacker, item);
    var filename = $"label_{box.RecId}_{DateTime.Now:yyyyMMddHHmmss}.xml";
    exporter.ExportToFile(xmlContent, _config.PrinterConfig.OutputDirectory, filename);
    
    // Log success
    File.AppendAllText(
        Path.Combine(_config.DataDirectory, "PrintLog.txt"),
        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | XML exported: {filename}\n"
    );
}
catch (IOException ex)
{
    // Retry logic
    for (int i = 0; i < _config.PrinterConfig.RetryAttempts; i++)
    {
        System.Threading.Thread.Sleep(_config.PrinterConfig.RetryDelayMs);
        try
        {
            exporter.ExportToFile(xmlContent, _config.PrinterConfig.OutputDirectory, filename);
            break;
        }
        catch { /* continue */ }
    }
}
```

**Troubleshooting:**

| Issue | Cause | Solution |
|-------|-------|----------|
| XML file not processed | Print Server not watching directory | Verify OutputDirectory in NiceLabel settings |
| Fields missing from label | Template doesn't have all 43 fields | Update carton-label-template.nlbl with missing fields |
| Permission denied | Output directory not writable | Check NTFS permissions on data/Labels/ |
| Printer offline | Network issue | Verify printer network connectivity |

---

### 12.2 Serial Port Printing

**Status:** ⚠️ Requires Configuration & Printer Support

**Overview:**
Sends label data directly to a printer connected via COM port (serial connection). Requires printer that understands ESC/POS or similar command language.

**Architecture:**

```
CartonUi Application
    ↓ (Format Label Data)
Serial Port Formatter (ESC/POS, ZPL, etc.)
    ↓ (Build command sequence)
Serial Port (COM1, COM2, etc.)
    ↓ (Send bytes)
Serial/USB Printer
    ↓
Physical Label
```

**Supported Printer Formats:**

| Format | Vendor | Protocol | Baudrate | Example |
|--------|--------|----------|----------|---------|
| ESC/POS | Epson | Binary escape sequences | 9600 | Epson TM series |
| ZPL | Zebra | Plain text commands | 9600 | Zebra ZP450 |
| TSPL | TSC | Plain text commands | 9600 | TSC TTP-243 |
| PCL | Hewlett Packard | Plain text commands | 9600 | HP LaserJet |

**ESC/POS Command Example:**

```csharp
public class EscPosFormatter
{
    public byte[] GenerateEscPosCommands(ItemDetail item, StackerRecord stacker)
    {
        var commands = new List<byte>();

        // Initialize printer
        commands.AddRange(new byte[] { 0x1B, 0x40 }); // ESC @ (reset)
        
        // Set font size (2x height, 2x width)
        commands.AddRange(new byte[] { 0x1D, 0x21, 0x11 }); // GS ! (set size)
        
        // Print title
        string title = $"Item: {item.ItemNumber}";
        commands.AddRange(Encoding.ASCII.GetBytes(title));
        commands.AddRange(new byte[] { 0x0A, 0x0A }); // LF x2
        
        // Print description
        string desc = $"{item.ColorDesc} | {item.ShapeDesc}";
        commands.AddRange(Encoding.ASCII.GetBytes(desc));
        commands.AddRange(new byte[] { 0x0A, 0x0A });
        
        // Print barcode (if available)
        string barcode = item.ItemNumber;
        commands.AddRange(new byte[] { 0x1D, 0x6B, 0x41, (byte)barcode.Length }); // GS k A
        commands.AddRange(Encoding.ASCII.GetBytes(barcode));
        commands.AddRange(new byte[] { 0x0A });
        
        // Cut paper
        commands.AddRange(new byte[] { 0x1D, 0x56, 0x42, 0x00 }); // GS V B (cut)
        
        return commands.ToArray();
    }
}
```

**Implementation (C#):**

```csharp
public class SerialPortPrinter : IPrinter
{
    private SerialPort _port;
    private readonly CartonAppConfig _config;

    public SerialPortPrinter(CartonAppConfig config)
    {
        _config = config;
    }

    public void Initialize()
    {
        _port = new SerialPort(_config.PrinterConfig.SerialPort)
        {
            BaudRate = _config.PrinterConfig.BaudRate,
            Parity = Parity.None,
            DataBits = 8,
            StopBits = StopBits.One,
            ReadTimeout = 5000,
            WriteTimeout = 5000
        };
        _port.Open();
    }

    public void PrintLabel(ItemDetail item, StackerRecord stacker, BoxRecord box)
    {
        if (!_port.IsOpen)
            Initialize();

        var formatter = new EscPosFormatter();
        byte[] commands = formatter.GenerateEscPosCommands(item, stacker);
        
        _port.Write(commands, 0, commands.Length);
        System.Threading.Thread.Sleep(2000); // Wait for printer
    }

    public void Dispose()
    {
        if (_port?.IsOpen ?? false)
            _port.Close();
    }
}
```

**Configuration (carton-settings.json):**

```json
{
  "PrinterConfig": {
    "Method": "SerialPort",
    "SerialPort": "COM1",
    "BaudRate": 9600,
    "Format": "EscPOS",
    "RetryAttempts": 3,
    "RetryDelayMs": 500
  }
}
```

**Testing:**

```csharp
// Manual test
var printer = new SerialPortPrinter(config);
printer.Initialize();
printer.PrintLabel(testItem, testStacker, testBox);
printer.Dispose();
```

**Troubleshooting:**

| Issue | Cause | Solution |
|-------|-------|----------|
| Port already in use | Another app using COM port | Close other apps; restart PC |
| Timeout on write | Printer not responding | Check physical connection; test with Hyperterminal |
| Gibberish on label | Wrong ESC/POS dialect | Update formatter for your printer model |
| No response | Baud rate mismatch | Verify printer baud rate; try 19200 or 115200 |

---

### 12.3 Network Printer (IP/DNS)

**Status:** ⚠️ Requires Network Configuration

**Overview:**
Sends label data directly to a network printer via TCP socket (port 9100) or HTTP. Works with IP addresses or DNS hostnames.

**Architecture:**

```
CartonUi Application
    ↓ (Format Label Data)
TCP Socket Formatter (ESC/POS, ZPL, etc.)
    ↓ (Build byte sequence)
Network Socket (TCP port 9100 or HTTP 80/443)
    ↓ (Send over network)
Network Printer (IP or DNS)
    ↓
Physical Label
```

**Supported Printers:**

| Model | Protocol | Port | IP Lookup | Example |
|-------|----------|------|-----------|---------|
| Zebra ZP450 | Raw TCP | 9100 | Dynamic | 192.168.1.100 |
| TSC TTP-243 | Raw TCP | 9100 | mDNS | printer-01.local |
| Epson TM-m30 | HTTP | 80 | DNS | carton-printer-01.company.local |
| HP LaserJet | Raw TCP | 9100 | DHCP | 10.0.1.50 |

**Implementation (C#):**

```csharp
public class NetworkPrinter : IPrinter
{
    private readonly CartonAppConfig _config;
    private TcpClient _client;

    public NetworkPrinter(CartonAppConfig config)
    {
        _config = config;
    }

    public void PrintLabel(ItemDetail item, StackerRecord stacker, BoxRecord box)
    {
        // Resolve hostname to IP (if needed)
        string ipAddress = ResolveHostname(_config.PrinterConfig.HostnameOrIp);
        
        _client = new TcpClient();
        _client.Connect(ipAddress, _config.PrinterConfig.Port);
        
        var formatter = new EscPosFormatter();
        byte[] commands = formatter.GenerateEscPosCommands(item, stacker);
        
        using (var stream = _client.GetStream())
        {
            stream.Write(commands, 0, commands.Length);
            stream.Flush();
            System.Threading.Thread.Sleep(2000);
        }
        
        _client.Close();
    }

    private string ResolveHostname(string hostnameOrIp)
    {
        // If it's already an IP, return it
        if (System.Net.IPAddress.TryParse(hostnameOrIp, out var ip))
            return ip.ToString();
        
        // Otherwise resolve DNS
        try
        {
            var hostEntry = System.Net.Dns.GetHostEntry(hostnameOrIp);
            return hostEntry.AddressList[0].ToString();
        }
        catch (Exception ex)
        {
            throw new Exception($"Cannot resolve hostname '{hostnameOrIp}': {ex.Message}");
        }
    }
}
```

**Configuration (carton-settings.json):**

```json
{
  "PrinterConfig": {
    "Method": "NetworkPrinter",
    "HostnameOrIp": "carton-printer-01.company.local",
    "Port": 9100,
    "Format": "ZPL",
    "TimeoutMs": 5000,
    "RetryAttempts": 3,
    "RetryDelayMs": 1000
  }
}
```

**Network Discovery:**

```csharp
// Find printers on network (requires Bonjour/mDNS)
public class PrinterDiscovery
{
    public static List<string> DiscoverNetworkPrinters()
    {
        var printers = new List<string>();
        
        // Method 1: Ping common IP ranges
        for (int i = 1; i <= 254; i++)
        {
            string ip = $"192.168.1.{i}";
            var ping = new System.Net.NetworkInformation.Ping();
            try
            {
                var result = ping.Send(ip, 500);
                if (result.Status == System.Net.NetworkInformation.IPStatus.Success)
                {
                    // Try to connect to port 9100 (printer default)
                    var client = new TcpClient();
                    client.ConnectAsync(ip, 9100).Wait(500);
                    if (client.Connected)
                    {
                        printers.Add(ip);
                        client.Close();
                    }
                }
            }
            catch { /* continue */ }
        }
        
        return printers;
    }
}
```

**Troubleshooting:**

| Issue | Cause | Solution |
|-------|-------|----------|
| Cannot resolve hostname | DNS misconfiguration | Verify hostname in DNS; use IP directly |
| Connection timeout | Printer offline | Ping printer IP; check network connectivity |
| Port 9100 refused | Printer not listening | Check printer config; some printers use 80 or custom port |
| Incomplete label | Network latency | Increase RetryDelayMs; add Thread.Sleep(500) |

---

### 12.4 HTTP POST (Cloud/Web Service)

**Status:** ⚠️ Requires Web Service Integration

**Overview:**
Posts label data as JSON to a remote HTTP(S) endpoint (e.g., cloud printing service, custom web API, PrintNode, etc.).

**Architecture:**

```
CartonUi Application
    ↓ (Build Label JSON)
JSON Serializer
    ↓ (Create JSON payload)
HTTP Client (POST request)
    ↓ (Send over HTTPS)
Cloud Printing Service / Web API
    ↓ (Process & queue job)
Remote Printer or Print Server
    ↓
Physical Label
```

**Supported Services:**

| Service | Endpoint | Protocol | Auth | Cost |
|---------|----------|----------|------|------|
| PrintNode | api.printnode.com | HTTPS | API Key | $0.01-0.05/page |
| Google Cloud Print | cloudprint.google.com | HTTPS | OAuth 2.0 | Free (deprecated) |
| Custom API | company.com/api/print | HTTPS | JWT/Token | Custom |
| Ezeep Blue | cloud.ezeep.com | HTTPS | Basic Auth | $0-50/month |

**JSON Payload Format:**

```json
{
  "jobId": "20260725_143045_00001",
  "lineNumber": "01",
  "itemNumber": "FL9036MOD1P4",
  "description": "FL90-WHITE | 3 X 6 X 0.31 IN | FINISH LINE",
  "stackNumber": "1",
  "quantity": 2,
  "shade": "R",
  "shiftNumber": 1,
  "inspectorInitials": "AB",
  "printTime": "2026-07-25T14:30:45Z",
  "printerName": "carton-printer-01",
  "labelTemplate": "carton-label-4x6",
  "copies": 1,
  "metadata": {
    "iRef": 1234,
    "colorDesc": "WHITE",
    "shapeDesc": "3 X 6 X 0.31 IN",
    "seriesDesc": "FINISH LINE"
  }
}
```

**Implementation (C#):**

```csharp
public class HttpPoster : IPrinter
{
    private readonly CartonAppConfig _config;
    private readonly HttpClient _httpClient;

    public HttpPoster(CartonAppConfig config)
    {
        _config = config;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        
        // Add auth header if configured
        if (!string.IsNullOrEmpty(config.PrinterConfig.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add(
                "Authorization", 
                $"Bearer {config.PrinterConfig.ApiKey}"
            );
        }
    }

    public async Task PrintLabelAsync(ItemDetail item, StackerRecord stacker, BoxRecord box)
    {
        var payload = new
        {
            jobId = $"{box.MakeTime:yyyyMMdd_HHmmss}_{box.RecId:00001}",
            lineNumber = box.LineId,
            itemNumber = item.ItemNumber,
            description = $"{item.ColorDesc} | {item.ShapeDesc} | {item.SeriesDesc}",
            stackNumber = stacker.StackNum.Trim(),
            quantity = item.LisQty,
            shade = stacker.Shade,
            shiftNumber = _config.CurrentShift,
            inspectorInitials = _config.CurrentInspector,
            printTime = DateTime.UtcNow.ToString("O"),
            printerName = _config.PrinterConfig.PrinterName,
            labelTemplate = _config.PrinterConfig.LabelTemplate,
            copies = 1,
            metadata = new
            {
                iRef = item.IRef,
                colorDesc = item.ColorDesc,
                shapeDesc = item.ShapeDesc,
                seriesDesc = item.SeriesDesc,
                // ... add remaining 39 fields
            }
        };

        var json = JsonConvert.SerializeObject(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(_config.PrinterConfig.EndpointUrl, content);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"HTTP {(int)response.StatusCode}: {errorBody}");
            }

            // Log success
            LogPrintJob(box.RecId, "success", response.StatusCode.ToString());
        }
        catch (Exception ex)
        {
            LogPrintJob(box.RecId, "error", ex.Message);
            throw;
        }
    }

    private void LogPrintJob(int recordId, string status, string details)
    {
        File.AppendAllText(
            Path.Combine(_config.DataDirectory, "HttpPrintLog.txt"),
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | RecId={recordId} | Status={status} | Details={details}\n"
        );
    }
}
```

**Configuration (carton-settings.json):**

```json
{
  "PrinterConfig": {
    "Method": "HttpPost",
    "EndpointUrl": "https://api.printservice.com/v1/print",
    "ApiKey": "sk-abc123def456",
    "PrinterName": "carton-printer-01",
    "LabelTemplate": "carton-label-4x6",
    "TimeoutMs": 10000,
    "RetryAttempts": 3,
    "RetryDelayMs": 2000
  }
}
```

**Error Handling & Retry:**

```csharp
public async Task PrintWithRetryAsync(ItemDetail item, StackerRecord stacker, BoxRecord box)
{
    int attempts = 0;
    while (attempts < _config.PrinterConfig.RetryAttempts)
    {
        try
        {
            await PrintLabelAsync(item, stacker, box);
            return; // Success
        }
        catch (HttpRequestException ex) when (attempts < _config.PrinterConfig.RetryAttempts - 1)
        {
            attempts++;
            await Task.Delay(_config.PrinterConfig.RetryDelayMs);
            LogPrintJob(box.RecId, "retry", $"Attempt {attempts}: {ex.Message}");
        }
    }
    
    // All retries exhausted
    LogPrintJob(box.RecId, "failed", "Max retries exceeded");
    throw new Exception("Failed to print after all retry attempts");
}
```

**Webhook for Print Confirmation:**

```csharp
// Optional: Listen for webhook callbacks from print service
[HttpPost("api/print-webhook")]
public IActionResult OnPrintComplete([FromBody] PrintWebhook webhook)
{
    // webhook.jobId = "20260725_143045_00001"
    // webhook.status = "completed" or "failed"
    // webhook.errorMessage = null or error details
    
    LogPrintJob(webhook.jobId, webhook.status, webhook.errorMessage);
    return Ok();
}
```

**Troubleshooting:**

| Issue | Cause | Solution |
|-------|-------|----------|
| 401 Unauthorized | Invalid API key | Verify ApiKey in config; check service docs |
| 404 Not Found | Wrong endpoint URL | Verify EndpointUrl; check service API docs |
| 429 Too Many Requests | Rate limiting | Increase RetryDelayMs; implement queue |
| Timeout | Service slow or unavailable | Increase TimeoutMs; check service status |

---

### 12.5 Output Method Comparison & Selection

| Aspect | NiceLabel XML | Serial Port | Network Printer | HTTP POST |
|--------|---------------|-------------|-----------------|-----------|
| **Setup Complexity** | Medium | Low | Medium | High |
| **Hardware Needed** | NiceLabel Server | Serial printer | Network printer | Internet connection |
| **Flexibility** | High (any label size) | Medium (format-dependent) | Medium (network-dependent) | High (cloud-based) |
| **Cost** | NiceLabel license | Printer cost | Printer + network | Service fee |
| **Scalability** | Good (batch processing) | Poor (one printer) | Good (multiple printers) | Excellent (cloud) |
| **Offline Support** | No (requires server) | Yes (direct USB) | Yes (local network) | No (cloud required) |
| **Print Speed** | Fast (batches) | Slow (serial) | Fast (network) | Variable (internet) |
| **Recommended Use** | Production facilities | Legacy systems | Small offices | Enterprise/cloud |

**Decision Matrix:**

```
Use NiceLabel XML if:
  ✓ You have NiceLabel infrastructure
  ✓ You need batch/queue processing
  ✓ You want flexible label templates
  ✓ You have multiple printer types

Use Serial Port if:
  ✓ You have legacy USB/serial printer
  ✓ You want direct control
  ✓ You have no network available
  ✓ You need low-latency printing

Use Network Printer if:
  ✓ You have network printer on LAN
  ✓ You want simple setup
  ✓ You have single printer per line
  ✓ You need high speed

Use HTTP POST if:
  ✓ You want cloud printing
  ✓ You have multiple locations
  ✓ You want enterprise monitoring
  ✓ You're using PrintNode or similar service
```

---

## Part 13: Printer Service Architecture (IPrinterService)

### 13.1 Interface Definition

**Purpose:** Unified abstraction allowing any printer method to be swapped without code changes.

**Interface Definition (C#):**

```csharp
/// <summary>
/// Unified printer interface supporting all output methods
/// </summary>
public interface IPrinterService
{
    /// <summary>
    /// Initialize printer connection/resources
    /// </summary>
    void Initialize();

    /// <summary>
    /// Print label synchronously
    /// </summary>
    void PrintLabel(ItemDetail item, StackerRecord stacker, BoxRecord box);

    /// <summary>
    /// Print label asynchronously (for HTTP/cloud methods)
    /// </summary>
    Task PrintLabelAsync(ItemDetail item, StackerRecord stacker, BoxRecord box);

    /// <summary>
    /// Verify printer is connected and ready
    /// </summary>
    bool IsPrinterReady();

    /// <summary>
    /// Get current printer status
    /// </summary>
    PrinterStatus GetStatus();

    /// <summary>
    /// Dispose resources
    /// </summary>
    void Dispose();
}

/// <summary>
/// Printer status information
/// </summary>
public class PrinterStatus
{
    public bool IsConnected { get; set; }
    public string CurrentMethod { get; set; }
    public string LastError { get; set; }
    public DateTime LastPrintTime { get; set; }
    public int PrintCount { get; set; }
}
```

### 13.2 Factory Pattern for Printer Selection

**Factory Implementation:**

```csharp
public class PrinterServiceFactory
{
    public static IPrinterService CreatePrinterService(CartonAppConfig config)
    {
        return config.PrinterConfig.Method.ToLower() switch
        {
            "nicelabelxml" => new NiceLabelXmlPrinter(config),
            "serialport" => new SerialPortPrinter(config),
            "networkprinter" => new NetworkPrinter(config),
            "httppost" => new HttpPoster(config),
            _ => throw new ArgumentException($"Unknown printer method: {config.PrinterConfig.Method}")
        };
    }
}
```

### 13.3 Base Implementation Class

**Shared functionality for all printers:**

```csharp
public abstract class BasePrinterService : IPrinterService
{
    protected CartonAppConfig _config;
    protected PrinterStatus _status;
    protected IPrintLogger _logger;

    protected BasePrinterService(CartonAppConfig config, IPrintLogger logger)
    {
        _config = config;
        _logger = logger;
        _status = new PrinterStatus
        {
            CurrentMethod = _config.PrinterConfig.Method,
            IsConnected = false,
            PrintCount = 0,
            LastPrintTime = DateTime.MinValue
        };
    }

    public abstract void Initialize();
    public abstract void PrintLabel(ItemDetail item, StackerRecord stacker, BoxRecord box);
    
    public virtual async Task PrintLabelAsync(ItemDetail item, StackerRecord stacker, BoxRecord box)
    {
        // Default: call sync version
        PrintLabel(item, stacker, box);
        await Task.CompletedTask;
    }

    public virtual bool IsPrinterReady()
    {
        return _status.IsConnected;
    }

    public virtual PrinterStatus GetStatus()
    {
        return _status;
    }

    public virtual void Dispose() { }

    protected void LogPrintJob(int recordId, string status, string details = "")
    {
        _logger.LogPrintJob(new PrintJobLog
        {
            RecordId = recordId,
            PrintMethod = _config.PrinterConfig.Method,
            Status = status,
            Details = details,
            Timestamp = DateTime.Now
        });

        _status.LastPrintTime = DateTime.Now;
        _status.PrintCount++;
    }

    protected void LogError(Exception ex)
    {
        _status.LastError = ex.Message;
        _logger.LogError(ex);
    }
}
```

### 13.4 Concrete Implementations

**NiceLabel XML Implementation:**

```csharp
public class NiceLabelXmlPrinter : BasePrinterService
{
    private readonly NiceLabelXmlExporter _exporter;

    public NiceLabelXmlPrinter(CartonAppConfig config, IPrintLogger logger) 
        : base(config, logger)
    {
        _exporter = new NiceLabelXmlExporter();
    }

    public override void Initialize()
    {
        // Create output directory if needed
        Directory.CreateDirectory(_config.PrinterConfig.OutputDirectory);
        _status.IsConnected = true;
        _logger.LogInfo($"NiceLabel XML printer initialized at {_config.PrinterConfig.OutputDirectory}");
    }

    public override void PrintLabel(ItemDetail item, StackerRecord stacker, BoxRecord box)
    {
        if (!_status.IsConnected) Initialize();

        try
        {
            var xmlContent = _exporter.GenerateXml(box, stacker, item);
            var filename = $"label_{box.RecId}_{DateTime.Now:yyyyMMddHHmmss}.xml";
            _exporter.ExportToFile(xmlContent, _config.PrinterConfig.OutputDirectory, filename);
            
            LogPrintJob(box.RecId, "exported", filename);
        }
        catch (Exception ex)
        {
            LogError(ex);
            throw;
        }
    }
}
```

**Serial Port Implementation:**

```csharp
public class SerialPortPrinter : BasePrinterService
{
    private SerialPort _port;
    private readonly EscPosFormatter _formatter;

    public SerialPortPrinter(CartonAppConfig config, IPrintLogger logger) 
        : base(config, logger)
    {
        _formatter = new EscPosFormatter();
    }

    public override void Initialize()
    {
        try
        {
            _port = new SerialPort(_config.PrinterConfig.OutputAddress)
            {
                BaudRate = int.Parse(_config.PrinterConfig.OutputParameters ?? "9600"),
                Parity = Parity.None,
                DataBits = 8,
                StopBits = StopBits.One,
                ReadTimeout = 5000,
                WriteTimeout = 5000
            };
            _port.Open();
            _status.IsConnected = true;
            _logger.LogInfo($"Serial port printer initialized on {_config.PrinterConfig.OutputAddress}");
        }
        catch (Exception ex)
        {
            LogError(ex);
            throw;
        }
    }

    public override void PrintLabel(ItemDetail item, StackerRecord stacker, BoxRecord box)
    {
        if (!_port?.IsOpen ?? true) Initialize();

        try
        {
            byte[] commands = _formatter.GenerateEscPosCommands(item, stacker);
            _port.Write(commands, 0, commands.Length);
            System.Threading.Thread.Sleep(2000);
            
            LogPrintJob(box.RecId, "printed", $"{commands.Length} bytes sent");
        }
        catch (Exception ex)
        {
            LogError(ex);
            throw;
        }
    }

    public override void Dispose()
    {
        if (_port?.IsOpen ?? false)
        {
            _port.Close();
            _port.Dispose();
        }
    }
}
```

**Network Printer Implementation:**

```csharp
public class NetworkPrinter : BasePrinterService
{
    private readonly EscPosFormatter _formatter;

    public NetworkPrinter(CartonAppConfig config, IPrintLogger logger) 
        : base(config, logger)
    {
        _formatter = new EscPosFormatter();
    }

    public override void Initialize()
    {
        // Test connection
        if (TestConnection())
        {
            _status.IsConnected = true;
            _logger.LogInfo($"Network printer initialized at {_config.PrinterConfig.OutputAddress}:{_config.PrinterConfig.OutputPort}");
        }
        else
        {
            throw new Exception($"Cannot connect to printer at {_config.PrinterConfig.OutputAddress}:{_config.PrinterConfig.OutputPort}");
        }
    }

    public override void PrintLabel(ItemDetail item, StackerRecord stacker, BoxRecord box)
    {
        try
        {
            string ipAddress = ResolveHostname(_config.PrinterConfig.OutputAddress);
            
            using (var client = new TcpClient())
            {
                client.Connect(ipAddress, _config.PrinterConfig.OutputPort);
                
                byte[] commands = _formatter.GenerateEscPosCommands(item, stacker);
                
                using (var stream = client.GetStream())
                {
                    stream.Write(commands, 0, commands.Length);
                    stream.Flush();
                    System.Threading.Thread.Sleep(2000);
                }
            }
            
            LogPrintJob(box.RecId, "printed", $"Sent to {ipAddress}:{_config.PrinterConfig.OutputPort}");
        }
        catch (Exception ex)
        {
            LogError(ex);
            throw;
        }
    }

    public override bool IsPrinterReady()
    {
        return TestConnection();
    }

    private bool TestConnection()
    {
        try
        {
            string ipAddress = ResolveHostname(_config.PrinterConfig.OutputAddress);
            using (var client = new TcpClient())
            {
                var task = client.ConnectAsync(ipAddress, _config.PrinterConfig.OutputPort);
                task.Wait(2000);
                return client.Connected;
            }
        }
        catch
        {
            return false;
        }
    }

    private string ResolveHostname(string hostnameOrIp)
    {
        if (System.Net.IPAddress.TryParse(hostnameOrIp, out var ip))
            return ip.ToString();
        
        try
        {
            var hostEntry = System.Net.Dns.GetHostEntry(hostnameOrIp);
            return hostEntry.AddressList[0].ToString();
        }
        catch (Exception ex)
        {
            throw new Exception($"Cannot resolve hostname '{hostnameOrIp}': {ex.Message}");
        }
    }
}
```

**HTTP POST Implementation:**

```csharp
public class HttpPoster : BasePrinterService
{
    private readonly HttpClient _httpClient;

    public HttpPoster(CartonAppConfig config, IPrintLogger logger) 
        : base(config, logger)
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(int.Parse(_config.PrinterConfig.OutputParameters ?? "10"))
        };
        
        if (!string.IsNullOrEmpty(_config.PrinterConfig.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_config.PrinterConfig.ApiKey}");
        }
    }

    public override void Initialize()
    {
        _status.IsConnected = true;
        _logger.LogInfo($"HTTP POST printer initialized for {_config.PrinterConfig.OutputAddress}");
    }

    public override async Task PrintLabelAsync(ItemDetail item, StackerRecord stacker, BoxRecord box)
    {
        try
        {
            var payload = BuildJsonPayload(item, stacker, box);
            var json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_config.PrinterConfig.OutputAddress, content);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"HTTP {(int)response.StatusCode}: {errorBody}");
            }

            LogPrintJob(box.RecId, "posted", response.StatusCode.ToString());
        }
        catch (Exception ex)
        {
            LogError(ex);
            throw;
        }
    }

    public override void PrintLabel(ItemDetail item, StackerRecord stacker, BoxRecord box)
    {
        // Sync wrapper around async
        PrintLabelAsync(item, stacker, box).Wait();
    }

    private object BuildJsonPayload(ItemDetail item, StackerRecord stacker, BoxRecord box)
    {
        return new
        {
            jobId = $"{box.MakeTime:yyyyMMdd_HHmmss}_{box.RecId:00001}",
            lineNumber = box.LineId,
            itemNumber = item.ItemNumber,
            description = $"{item.ColorDesc} | {item.ShapeDesc} | {item.SeriesDesc}",
            stackNumber = stacker.StackNum.Trim(),
            quantity = item.LisQty,
            shade = stacker.Shade,
            printTime = DateTime.UtcNow.ToString("O"),
            printerName = _config.PrinterConfig.PrinterName,
            metadata = new { iRef = item.IRef, colorDesc = item.ColorDesc }
        };
    }
}
```

---

## Part 14: Configuration & Logging System

### 14.1 Updated carton-settings.json Schema

**Complete configuration with all printer methods:**

```json
{
  "CurrentPrimaryItem": "FL9036MOD1P4",
  "CurrentSecondaryItem": "",
  "PrinterConfig": {
    "Method": "NiceLabelXml",
    "OutputType": "NiceLabelXml|SerialPort|NetworkPrinter|HttpPost",
    "OutputAddress": "C:\\LabelPrint\\data\\Labels",
    "OutputPort": 9100,
    "OutputParameters": "9600",
    "ApiKey": "",
    "PrinterName": "carton-printer-01",
    "LabelTemplate": "carton-label-4x6",
    "RetryAttempts": 3,
    "RetryDelayMs": 1000
  }
}
```

**Field Reference:**

| Field | Method | Purpose | Example |
|-------|--------|---------|---------|
| OutputAddress | NiceLabel | Directory for XML files | C:\LabelPrint\data\Labels |
| OutputAddress | SerialPort | COM port name | COM1 |
| OutputAddress | Network | Hostname or IP | carton-printer-01.company.local |
| OutputAddress | HTTP | API endpoint URL | https://api.printservice.com/v1/print |
| OutputPort | Network | TCP port | 9100 |
| OutputPort | SerialPort | N/A (ignored) | 0 |
| OutputPort | HTTP | N/A (ignored) | 0 |
| OutputParameters | SerialPort | Baud rate | 9600 |
| OutputParameters | HTTP | Timeout seconds | 10 |
| ApiKey | HTTP | Authentication | sk-abc123def456 |
| ApiKey | Others | N/A (empty) | "" |

**Printer Method Configurations:**

```json
{
  "comment": "NiceLabel XML - Recommended for El Paso",
  "PrinterConfig": {
    "Method": "NiceLabelXml",
    "OutputAddress": "C:\\LabelPrint\\data\\Labels",
    "OutputPort": 0,
    "OutputParameters": "",
    "ApiKey": "",
    "RetryAttempts": 3,
    "RetryDelayMs": 1000
  }
}
```

```json
{
  "comment": "Serial Port - Legacy USB printer",
  "PrinterConfig": {
    "Method": "SerialPort",
    "OutputAddress": "COM1",
    "OutputPort": 0,
    "OutputParameters": "9600",
    "ApiKey": "",
    "RetryAttempts": 3,
    "RetryDelayMs": 500
  }
}
```

```json
{
  "comment": "Network Printer - IP or DNS",
  "PrinterConfig": {
    "Method": "NetworkPrinter",
    "OutputAddress": "carton-printer-01.company.local",
    "OutputPort": 9100,
    "OutputParameters": "",
    "ApiKey": "",
    "RetryAttempts": 3,
    "RetryDelayMs": 1000
  }
}
```

```json
{
  "comment": "HTTP POST - Cloud or custom API",
  "PrinterConfig": {
    "Method": "HttpPost",
    "OutputAddress": "https://api.printservice.com/v1/print",
    "OutputPort": 0,
    "OutputParameters": "10",
    "ApiKey": "sk-abc123def456",
    "RetryAttempts": 3,
    "RetryDelayMs": 2000
  }
}
```

### 14.2 Print Logging Infrastructure

**Print Job Log Model:**

```csharp
public class PrintJobLog
{
    public int Id { get; set; }
    public int RecordId { get; set; }
    public string PrintMethod { get; set; }
    public string Status { get; set; } // exported, printed, posted, failed, retry
    public string Details { get; set; }
    public DateTime Timestamp { get; set; }
}
```

**Print Logger Interface:**

```csharp
public interface IPrintLogger
{
    void LogPrintJob(PrintJobLog log);
    void LogError(Exception ex);
    void LogInfo(string message);
    List<PrintJobLog> GetPrintHistory(int days = 7);
    PrinterStatistics GetStatistics(DateTime? startDate = null, DateTime? endDate = null);
}
```

**File-Based Logger Implementation:**

```csharp
public class FilePrintLogger : IPrintLogger
{
    private readonly string _logDirectory;
    private readonly string _printLogFile;
    private readonly object _lockObject = new object();

    public FilePrintLogger(string baseDirectory)
    {
        _logDirectory = Path.Combine(baseDirectory, "logs");
        _printLogFile = Path.Combine(_logDirectory, "print_jobs.csv");
        
        Directory.CreateDirectory(_logDirectory);
        
        // Create header if file doesn't exist
        if (!File.Exists(_printLogFile))
        {
            File.WriteAllText(_printLogFile, 
                "Timestamp,RecordId,PrintMethod,Status,Details\n");
        }
    }

    public void LogPrintJob(PrintJobLog log)
    {
        lock (_lockObject)
        {
            var line = $"{log.Timestamp:yyyy-MM-dd HH:mm:ss}," +
                      $"{log.RecordId}," +
                      $"{log.PrintMethod}," +
                      $"{log.Status}," +
                      $"\"{log.Details.Replace("\"", "\"\"")}\"\n";
            
            File.AppendAllText(_printLogFile, line);
        }
    }

    public void LogError(Exception ex)
    {
        lock (_lockObject)
        {
            var errorFile = Path.Combine(_logDirectory, $"errors_{DateTime.Now:yyyyMMdd}.txt");
            var message = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {ex.GetType().Name} | {ex.Message}\n" +
                         $"Stack Trace: {ex.StackTrace}\n\n";
            
            File.AppendAllText(errorFile, message);
        }
    }

    public void LogInfo(string message)
    {
        lock (_lockObject)
        {
            var infoFile = Path.Combine(_logDirectory, $"info_{DateTime.Now:yyyyMMdd}.txt");
            File.AppendAllText(infoFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}\n");
        }
    }

    public List<PrintJobLog> GetPrintHistory(int days = 7)
    {
        lock (_lockObject)
        {
            var logs = new List<PrintJobLog>();
            var lines = File.ReadAllLines(_printLogFile).Skip(1); // Skip header
            var cutoffDate = DateTime.Now.AddDays(-days);

            foreach (var line in lines)
            {
                var parts = ParseCsvLine(line);
                if (parts.Length >= 5 && DateTime.TryParse(parts[0], out var timestamp))
                {
                    if (timestamp >= cutoffDate)
                    {
                        logs.Add(new PrintJobLog
                        {
                            Timestamp = timestamp,
                            RecordId = int.Parse(parts[1]),
                            PrintMethod = parts[2],
                            Status = parts[3],
                            Details = parts[4]
                        });
                    }
                }
            }

            return logs;
        }
    }

    public PrinterStatistics GetStatistics(DateTime? startDate = null, DateTime? endDate = null)
    {
        var stats = new PrinterStatistics();
        var history = GetPrintHistory(90);
        
        var filtered = history
            .Where(h => (!startDate.HasValue || h.Timestamp >= startDate) &&
                       (!endDate.HasValue || h.Timestamp <= endDate))
            .ToList();

        stats.TotalPrints = filtered.Count(h => h.Status == "printed" || h.Status == "exported" || h.Status == "posted");
        stats.FailedPrints = filtered.Count(h => h.Status == "failed");
        stats.RetryCount = filtered.Count(h => h.Status == "retry");
        stats.SuccessRate = stats.TotalPrints > 0 
            ? ((stats.TotalPrints / (double)(stats.TotalPrints + stats.FailedPrints)) * 100) 
            : 0;

        stats.ByMethod = filtered
            .GroupBy(h => h.PrintMethod)
            .ToDictionary(g => g.Key, g => g.Count());

        return stats;
    }

    private string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = "";
        var inQuotes = false;

        foreach (var ch in line)
        {
            if (ch == '"')
                inQuotes = !inQuotes;
            else if (ch == ',' && !inQuotes)
            {
                result.Add(current);
                current = "";
            }
            else
                current += ch;
        }

        result.Add(current);
        return result.ToArray();
    }
}
```

**Printer Statistics Model:**

```csharp
public class PrinterStatistics
{
    public int TotalPrints { get; set; }
    public int FailedPrints { get; set; }
    public int RetryCount { get; set; }
    public double SuccessRate { get; set; }
    public Dictionary<string, int> ByMethod { get; set; }
}
```

---

## Part 15: El Paso Plant Implementation (POC)

### 15.1 El Paso Configuration Profile

**Environment:**
- Plant: El Paso
- Equipment: Nuovofina sorters
- Data Source: PLC machines + SAP ME (no Progress database)
- Printing: NiceLabel XML (primary priority)
- Status: Initial POC & implementation

**Priority Order:**
1. **Phase 1 (Week 1-2):** NiceLabel XML export working end-to-end
2. **Phase 2 (Week 3-4):** Serial/Network printer fallback (if needed)
3. **Phase 3 (Future):** HTTP POST integration (if cloud printing added)

### 15.2 carton-settings.json for El Paso

```json
{
  "CurrentPrimaryItem": "FL9036MOD1P4",
  "CurrentSecondaryItem": "",
  "PrinterConfig": {
    "Method": "NiceLabelXml",
    "OutputAddress": "\\\\elp-nlabel-01\\SharedLabels\\Queue",
    "OutputPort": 0,
    "OutputParameters": "",
    "ApiKey": "",
    "PrinterName": "carton-printer-elp-01",
    "LabelTemplate": "carton-label-4x6",
    "RetryAttempts": 3,
    "RetryDelayMs": 1000
  }
}
```

**Note:** OutputAddress points to NiceLabel Print Server's shared queue directory

### 15.3 El Paso Deployment Checklist

**Phase 1: NiceLabel XML Setup**

```
Infrastructure:
  ☐ NiceLabel Print Server installed on elp-nlabel-01
  ☐ SharedLabels\Queue directory created and shared
  ☐ Print Server configured to watch SharedLabels\Queue
  ☐ Network printer(s) added to NiceLabel

Configuration:
  ☐ CartonUi.exe deployed to workstation
  ☐ carton-settings.json configured with SharedLabels path
  ☐ Flag files created:
    ☐ DoesManStk (contains "01")
    ☐ LineNumber (contains "01")
    ☐ MEXICO-ONLY (if Mexico items needed)

Data Import:
  ☐ SAP ME exports itemdet.csv to data/
  ☐ SAP ME exports stackers01.csv to data/
  ☐ boxes01.csv created (auto-created by app)
  ☐ All CSV files have correct encoding (UTF-8, no BOM)

Testing:
  ☐ App launches without errors
  ☐ Primary Item field visible
  ☐ Simulate button creates XML in SharedLabels\Queue
  ☐ NiceLabel Print Server processes XML
  ☐ Label prints on physical printer
  ☐ Print log created in logs/ directory
```

**Phase 2: Fallback Options (if NiceLabel unavailable)**

```
Option A: Serial Port to Local Printer
  Requirements:
    ☐ USB/Serial printer on workstation COM port
    ☐ Printer supports ESC/POS commands
    ☐ Update OutputAddress to "COM1", OutputParameters to "9600"

Option B: Network Printer
  Requirements:
    ☐ Ethernet-connected printer on plant network
    ☐ Printer IP or DNS name known
    ☐ Update OutputAddress to "carton-printer-elp-01" or "192.168.1.50"
    ☐ Update OutputPort to "9100"
    ☐ Update Method to "NetworkPrinter"
```

### 15.4 PLC Data Integration (El Paso)

**PLC → CSV Pipeline:**

```
PLC Machine (Nuovofina)
    ↓ (writes 65-char messages)
plc01 FIFO pipe
    ↓ (message format: " 1 FL9036MOD1P4-2-R-1")
Data Directory: C:\LabelPrint\data\
    ↓ (app reads in real-time)
CartonUi Application
    ↓ (parses message, looks up item)
boxes01.csv / stackers01.csv
    ↓ (records created)
NiceLabel XML Export
    ↓ (XML generated)
SharedLabels\Queue directory
    ↓ (file dropped)
NiceLabel Print Server (elp-nlabel-01)
    ↓ (auto-processes)
Network Printer
    ↓ (physical label)
```

**SAP ME Integration Points:**

1. **Item Master Export (daily/on-demand):**
   - Export itemdet.csv from SAP ME materials table
   - Format: 43 columns (IRef, ItemNumber, LisQty, Shade, ColorDesc, ShapeDesc, SeriesDesc, Brand, ... 35 more)
   - Save to: `C:\LabelPrint\data\itemdet.csv`
   - Encoding: UTF-8 without BOM

2. **Stacker Configuration (daily/on-demand):**
   - Export stackers01.csv from SAP ME production lines table
   - Format: 7 columns (LineId, StackNum, IRef, PlcMsg, Shade, Size, ErrMsg)
   - Save to: `C:\LabelPrint\data\stackers01.csv`

3. **Production Monitoring (optional future):**
   - CartonUi generates print logs
   - Log location: `C:\LabelPrint\logs\print_jobs.csv`
   - SAP ME can query for print confirmations

### 15.5 NiceLabel Template Setup (El Paso)

**Label Template Requirements:**

```
File: carton-label-4x6.nlbl
Size: 4" x 6" (Zebra standard)
Printer: Any network printer configured in NiceLabel

Fields to include:
  ☐ Item Number (ItemNumber)
  ☐ Description (Description = ColorDesc | ShapeDesc | SeriesDesc)
  ☐ Quantity (Quantity)
  ☐ Shade (Shade)
  ☐ Stack Number (StackNumber) - if applicable
  ☐ Print Time (PrintTime)
  ☐ Barcode (CODE128 of ItemNumber)
  ☐ All 43 fields from itemdet.csv (in template variables)

Setup Steps:
  1. Open NiceLabel Designer
  2. Create new label: 4" x 6"
  3. Add data fields for each XML item
  4. Map fields to XML structure:
     <Item name="ItemNumber" value="..." />
  5. Add barcode: CODE128 of {ItemNumber}
  6. Save as carton-label-4x6.nlbl
  7. Deploy to NiceLabel Print Server
```

**XML Field Mapping Example:**

```xml
<!-- Generated by CartonUi -->
<Document xmlns="http://www.nicelabel.com/2011/LabelDefinition">
  <LabelData>
    <Item name="ItemNumber" value="FL9036MOD1P4" />
    <Item name="Description" value="WHITE | 3 X 6 X 0.31 IN | FINISH LINE" />
    <Item name="Quantity" value="2" />
    <Item name="Shade" value="R" />
    <Item name="StackNumber" value="1" />
    <Item name="ShiftNumber" value="1" />
    <Item name="InspectorInitials" value="AB" />
    <Item name="PrintTime" value="2026-07-25 14:30:45" />
    <!-- ... 35 more fields -->
  </LabelData>
</Document>

<!-- NiceLabel template references these via {ItemNumber}, {Description}, etc. -->
```

### 15.6 Monitoring & Support (El Paso)

**Print Job Dashboard (daily report):**

```csharp
// Generate daily statistics
var logger = new FilePrintLogger(@"C:\LabelPrint\data");
var today = DateTime.Now.Date;
var stats = logger.GetStatistics(today, today.AddDays(1));

Console.WriteLine($"El Paso Production Report - {today:yyyy-MM-dd}");
Console.WriteLine($"Total Labels: {stats.TotalPrints}");
Console.WriteLine($"Failed: {stats.FailedPrints}");
Console.WriteLine($"Success Rate: {stats.SuccessRate:0.00}%");
Console.WriteLine($"By Method: {string.Join(", ", stats.ByMethod.Select(kv => $"{kv.Key}: {kv.Value}"))}");

// Export to CSV for SAP ME reporting
var history = logger.GetPrintHistory(1);
var report = history.Select(h => $"{h.Timestamp:yyyy-MM-dd HH:mm:ss},{h.RecordId},{h.PrintMethod},{h.Status},{h.Details}");
File.WriteAllLines(@"C:\LabelPrint\reports\daily_report.csv", report);
```

**Support Contacts:**
- NiceLabel Server: elp-nlabel-01 (IT network team)
- Printer Issues: Plant operations team
- App Issues: Development team

---

## Appendix: Quick Reference

### Message Format Cheat Sheet

```
NUOVOFINA Format 2 (PLC):
" 1 FL9036MOD1P4-2-R-1[padding to 65 chars]"
^^                                           
Stack at 0-1, item at 32, total 65 chars

SYSTEM SORTER Format 1 (Manual):
" 1,Y,1,AB"
Stack / Reprint Flag / Shift / Inspector

SATI Format 2 (Item-based, no stack):
"?? FL9036MOD1P4-2-R-1[padding to 65 chars]"
Stack unknown, item at 32
```

### File Creation Pattern

```csharp
// Message → Box Record
boxRecord = $"{recId},{lineId},{now:yyyy-MM-dd HH:mm:ss},{stackNum},{plcMsg},,{printNum}";

// Message → Stacker Record  
stackerRecord = $"{lineId},{stackNum},{iref},{plcMsg},{shade},{size},";

// Write to CSV
AppendToFile(csvPath, record + Environment.NewLine);
```

### Boolean Flag Pattern

```csharp
// Check if flag file exists
doesMexico = File.Exists(Path.Combine(dataDir, "MEXICO-ONLY"));
does4DigitShade = File.Exists(Path.Combine(dataDir, "4DIGITSHADE"));
```

---

## Document Information

- **Title:** PLC Interface Analysis — Complete Documentation
- **Date:** 2026-07-25
- **Project:** StandAlone (Carton Label Printing Module)
- **Scope:** All PLC interface types (Nuovofina, System, SATI, Private Label)
- **Audience:** Developers, QA, Operations, Business Analysts

**Word Document Format:** Ready to copy-paste into Microsoft Word

---

End of Document
