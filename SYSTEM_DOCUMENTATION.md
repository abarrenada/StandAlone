# StandAlone — Functional & Technical System Documentation

**Scope:** the full `c:\Workspace\StandAlone` solution — a from-scratch C#/.NET port of Daltile's Progress (OpenEdge) 4GL carton- and pallet-label printing system, driven by PLC/sorter signals on the plant floor.

**Companion references already in this repo** (not duplicated here — see them for the detail they own):
- `README.md` — project-by-project overview
- `CSV_FILES_GUIDE.md` / `CSV_FILES_GUIDE_ENHANCED.md` — CSV schemas, field-by-field mapped to the legacy Progress `.df` schema
- `PLC_INTERFACE_COMPLETE_ANALYSIS.md` / `QUICK_START.md` — sorter/PLC hardware message formats
- `data/label_type_format_inventory.md` — label-type business names and per-format sample files

The legacy Progress source this system replaces lives in a separate working directory, `c:\Workspace\southalr` (not part of this repo). Where this document says "legacy" or names a `.p`/`.i` file, that's where it lives.

---

## 1. What this system does

Three physical stations on the plant floor, each a separate PC running the same app configured for one job, with its own PLC/scanner and printer:

1. **Carton-label printing station** — as cartons pass a PLC-driven sorter/stacker, the app decides a label format and prints a carton label (barcode, item info, UPC).
2. **Pallet-label printing station** — an operator scans a *carton's* barcode (or types an item/stack number) to look up what's on it, then prints a *pallet* label for the pallet that carton just went onto.
3. **EOL (End-of-Line) scanning station** — as pallets leave the line, an operator scans the *pallet's* serial to confirm it, log the transaction, and send a production-confirmation to SAP for backflush.

A fourth mode, **Settings**, is a password-gated maintenance screen for all the configuration above.

All four are the *same* executable (`StandAlone.CartonUi.exe`), launched with a different command-line flag per station. See §7.

---

## 2. Solution structure

| Project | Role |
|---|---|
| **StandAlone.CartonUi** | The interactive WinForms app — everything in §1. This is where all current, active work has happened. |
| **StandAlone.Integration** | Shared library: label payload model, SATO template resolver/renderer, SAP integration, PLC payload parsing, serial/file input adapters. Referenced by CartonUi *and* by the separate pipeline below. |
| **StandAlone.Console** | A small CLI tool (`dotnet run` with positional args) that reads an `item-label-types.csv` + `item-running.csv` pair and writes label decisions, or reads one envelope over serial. |
| **StandAlone.Worker** | A generic-host background service that watches a directory for dropped files, parses PLC-style payloads, and prints/sends-to-SAP autonomously. |
| **StandAlone.Tests** | xUnit tests — covers the SATO template pipeline, the SAP XML writer, `PlcPayloadParser`, and the Console/Worker `LabelDecisionService`. Does **not** cover `MainForm`/`PalletScanForm`/`EolScanForm` UI logic (untestable without WinForms harness) or `Worker`'s orchestration handlers directly. |

### 2.1 Important: Console/Worker is a separate, disconnected pipeline

`StandAlone.Console` and `StandAlone.Worker` are **not wired into the interactive CartonUi app in any way** — nothing in `MainForm`/`PalletScanForm`/`EolScanForm` launches or calls them, and nothing in `Worker` launches `MainForm`. They share only the `StandAlone.Integration` library, and even there the overlap is partial:

- **Data model**: Console/Worker use `ItemLabelDecision` / `ItemRunningEntry` over `item-label-types.csv` / `item-running.csv` / `label-decisions.csv`. CartonUi uses `BoxRecord` / `StackerRecord` / `ItemDetail` / `PalletRecord` / `EolScanRecord` over `boxes{NN}.csv` / `stackers{NN}.csv` / `itemdet.csv` / `pallets.csv` / `eol-scans.csv`. These never intersect.
- **Label printing**: Worker's `FileLabelPrinter` (`StandAlone.Integration\Services\LabelPrinter.cs`) hand-builds a generic stub XML (`<LabelDocument>` / `<PalletLabelDocument>`) and writes it to a folder — it **never calls** `ThermalPrinterCommandExporter` or `SatoTemplateResolver`, so it does not produce real SATO/SBPL printer commands the way CartonUi does.
- **What genuinely is shared**: `SatoTemplateResolver`, `SatoTemplateRenderer`, `ThermalLabelPayload`, and `FileSapIntegrationService`/`PalletIntegrationPayload` all live in `StandAlone.Integration` and are usable by both, though only CartonUi actually exercises the SATO template path today.

Treat Worker/Console as a **separate, earlier or alternate prototype pipeline** — worth knowing about (in case something references it), but not part of the production flow described in the rest of this document.

---

## 3. CartonUi: functional walkthrough

### 3.1 Carton-label printing (PLC-driven, `CartonPrintMode = "PLC Signal"`)

`MainForm` opens on a startup screen; the operator enters the primary item number, hits **Begin**, and the app opens the configured PLC input (serial port or TCP, per `PlcConnectionType`). Each decoded PLC frame is:
- Appended to a 12-row "browse" grid (`AppendPlcFrameToBrowseAsync`, `MainForm.cs`), which also **writes a new row to `boxes{NN}.csv`**, computing that row's 30-char barcode (`ThermalPrinterCommandBuilder.ComputeCartonBarcodeSerial`) from `DateTime.Now` at that instant.
- If the decoded value is `"8"` (the print-trigger signal), independently and concurrently triggers `AutoPrintForPlc8Async`, which builds and sends the actual carton label via `ExportManualLabelAsync`/`BuildThermalPayload`, computing its *own* separate `DateTime.UtcNow`.

⚠️ These are two independent timestamp computations for what should be the same event — see §8.2.

**F1 / F3 reprint**: selecting a row in the browse grid and pressing F1 (or F3 for reprint-by-stack-number) re-sends that carton's label via `ExportLabelAsync`. This reuses the box's **already-stored** `BarcodeSerial` from the CSV rather than recomputing it (fixed — see §9).

### 3.2 Manual carton/pallet printing (`CartonPrintMode = "Manual Qty"`)

Instead of "Begin", the startup screen shows **Print Carton**, **Print Pallet**, **Pallet Scan**, and **EOL Scan** buttons.

- **Print Carton** (`PrintManualAsync(isPallet: false)`) — operator enters item/quantity manually, prints a carton label on demand.
- **Print Pallet** (`ShowPalletQuery` → the "Pallet Query" panel) — operator types or scans a **carton's** barcode/item#/stack# into "Carton Label Serial No:"; `LookupCartonSerialAsync` tries, in order: (1) exact carton-barcode match in `boxes{NN}.csv`, (2) item number, (3) stack number. On a successful barcode-serial match, the carton's raw barcode is captured (`_palletQueryCartonBarcode`) so it can be embedded as a reference barcode on the printed pallet label. Printing (`PrintPalletLabelAsync`) allocates a new pallet serial, prints, and — critically — **writes a row to `pallets.csv`** (the pallet registry, see §3.4).

### 3.3 Pallet Scan station (`--pallet` flag → `PalletScanForm`)

A dedicated full-screen station: scans (serial or TCP) or manually types a 30-char carton barcode, looks it up the same way as §3.2, allocates a pallet serial, prints the pallet label, and writes to `pallets.csv`. Launched via `Program.cs`'s `--pallet` branch, which shows a small `PalletStartupDialog` (Inspector + Shift) before opening the form, bypassing `MainForm` entirely.

### 3.4 EOL Scan station (`--eol` flag → `EolScanForm`)

A dedicated full-screen station mirroring `PalletScanForm`'s input pattern (manual/serial/IP), but scanning a **pallet** serial instead of a carton barcode:

1. Operator scans/types a pallet serial (accepts either the printed "PPP-SSSSSSSSS" text or the bare 12-digit barcode value — both are normalized to the dashed form).
2. `GetPalletBySerialAsync` looks it up in **`pallets.csv`** — the registry written whenever a pallet label is printed anywhere (§3.2/§3.3). If it's not there, the scan is rejected ("Pallet not found").
3. On a match: logs an `EolScanRecord` to `eol-scans.csv`, sends a production-confirmation payload to SAP via `FileSapIntegrationService` (writes an XML file to `data/sap-output/`), and shows the result in a "last 15 scans" grid (backed by `GetLastEolScansAsync`, so history survives restarts).

**This is a hard dependency**: a pallet serial can only be confirmed at EOL if it was printed by this same system first (i.e., `pallets.csv` has a row for it). If `pallets.csv` isn't shared across stations, EOL can only validate pallets printed on its own station. See §6.3 for the shared-drive setting.

### 3.5 Settings maintenance (`--settings` flag → `SettingsForm`)

Password-gated (`AppSettings.MasterPasswordHash`, SHA-256). Two panels: a password prompt, then the settings form itself — CSV paths (see §6.2 caveat), production line number, PLC connection (serial/IP), label output type/address, thermal printer type, carton print mode, pallet label plant name/location, and the shared pallets-CSV path.

---

## 4. Label rendering pipeline

`ThermalPrinterCommandExporter.ExportAsync` (`StandAlone.CartonUi\Services\ThermalPrinterCommandExporter.cs`) is the single entry point for turning a `ThermalLabelPayload` into printer commands, archiving them, and dispatching to the configured output address (TCP `host:port`, UNC printer share, or a plain file path — auto-detected).

### 4.1 Carton labels — template-driven

For `LabelFormat = "CARTON_LABEL"` (or `"SLAB_LABEL"`), `SatoTemplateResolver.ResolveTemplateFileName` (`StandAlone.Integration\Services\SatoTemplateResolver.cs`) picks a `.sato` file from `sato_templates/` purely by **label size token** (plus Mexico/label-type-code branches):

```
lt_mexico_{size}.sato              — IsMexicoItem
lt04_default_{size}.sato           — LabelTypeCode == 4
lt06_xover_{size}_{ref-first|ref-last}.sato — LabelTypeCode == 6
lt_default_{size}.sato              — everything else (the common case)
```
`{size}` is the label size code with spaces/dots stripped, with a few normalized aliases (`4.5x3`→`45x3`, etc.) — everything else passes through unchanged. The most common carton size in this system's data, `"4x3"`, isn't one of the special-cased aliases, but it *is* still reachable: it just passes through the default branch unchanged, resolving to `lt_default_4x3.sato` (confirmed against real print output). Two files (`lt_default_3x45_inverted.sato`, `lt04_default_3x45_inverted.sato`) don't correspond to any resolver branch and appear to be manually-maintained variants not currently wired up.

The resolved template is read from disk, placeholders like `{ItemNumber}`, `{CartonBarcodeSerial}` etc. are substituted (`SatoTemplateRenderer`, reflecting over `ThermalLabelPayload` properties), and symbolic markers (`<STX>`, `<ESC>`, `\x1b..`) are decoded to raw control bytes.

If no template file resolves (or the printer type isn't SATO), it falls back to `ThermalPrinterCommandBuilder.Build`, which has hand-coded builders per printer language (SATO/IPL/ZPL/Fingerprint) — see §4.3.

### 4.2 Pallet labels — always hand-coded, never a template

For `LabelFormat = "PALLET_LABEL"`, `ResolveTemplateFileName` **deliberately returns `null`** (there's no dedicated pallet-sized `.sato` file, and the pallet-print screens share the carton screen's size combo, so template-by-size-lookup would coincidentally match a *carton* layout). This forces every pallet print through `ThermalPrinterCommandBuilder.BuildSatoPallet`, a byte-for-byte port of the legacy `dtlbl101b.i` (WMS 4×6 pallet label, M8400RV/84Pro printer-model branch) — see §5 for the legacy correspondence and §8.1 for known gaps (no QC-hold flag, no operation/route tie-in, single printer-model branch only).

### 4.3 Fallback/other builders

`ThermalPrinterCommandBuilder.BuildSatoCarton`/`BuildIpl`/`BuildZpl`/`BuildFingerprint` are simpler, self-contained layouts (not legacy-faithful) used when no template resolves or a non-SATO printer type is configured.

### 4.4 Barcode integrity (reprint fix)

`ComputeCartonBarcodeSerial` derives the carton barcode's year+julian-day segment from `payload.CreatedAtUtc`. Originally, every print path (including reprints) recomputed this from "now" — meaning a reprint on a different calendar day than the original scan produced a **different barcode string** than what's stored in `boxes{NN}.csv`, breaking future lookups. Fixed: reprints (`ExportLabelAsync`) now pass the box's originally-stored `BarcodeSerial` through as an explicit override (`ThermalLabelPayload.CartonBarcodeSerial`), and the three call sites that used to unconditionally recompute it now only do so when no value was already supplied.

### 4.5 Archive file

Every export is archived to `data/thermal_{PrinterType}_last.txt` — **overwritten** on each print (previously timestamped per-print; changed to a single rolling file per request).

---

## 5. Legacy Progress correspondence

| Legacy program/include | What it did | C# equivalent |
|---|---|---|
| `dtscn011.p` | Mainline AutoLine Scanner interface for pallet receiving — decodes PLC barcode, validates against `itemdet`/`itemhdr`, creates WMS receipts, decides which pallet label format to print | `MainForm`'s PLC handling + `ExportLabelAsync`/`ExportManualLabelAsync` flow |
| `dtlbl101a.i` | "Plant" 4×6 pallet label format (Color/Shape layout) | **Not ported** — this plant uses WMS format only (confirmed with the user) |
| `dtlbl101b.i` | "WMS" 4×6 pallet label format (Tag/SKU/Shd/Plant/Qty/Grade/Shift/Line layout) | `ThermalPrinterCommandBuilder.BuildSatoPallet` — faithful field/coordinate port, M8400RV/84Pro branch only |
| `dtlbl102d.i` | Customer/HCS pallet label (Home Depot/Lowe's/F&D format) | Not implemented in the current C# port |
| `dtvar060a.i` | Shared `prt-*` print-variable definitions | Modeled as `ThermalLabelPayload` properties |
| `dtrcv-orawms.p` `PROC-create-MfgOrdConf` | Real production-confirmation/backflush logic — confirms qty against a manufacturing order **line and operation** | `PalletIntegrationPayload`/`FileSapIntegrationService` — confirms qty at the **pallet** level only; no operation/route split (no `sl-setup`/`MfgOrdRoutes` equivalent exists) |
| `dtplc067_prep.p` | Item-number display masking (`xxxx  xxxxxxxxxxx` picture) | `ThermalPrinterCommandBuilder.MaskItemNumber` (carton) / `FormatPalletSkuField` (pallet, single-space variant) |

---

## 6. Data & configuration reference

### 6.1 CSV/file families (all under the resolved `data/` directory)

| File | Written by | Read by | Purpose |
|---|---|---|---|
| `boxes{NN}.csv` | PLC frame handler | Reprint, Pallet Query/Scan lookups | Per-carton scan record incl. barcode |
| `stackers{NN}.csv` | (external/config) | Item lookups by stack# | Stack→item reference config |
| `itemdet.csv` / `mitemdet.csv` | (external, from Progress export) | All item lookups | Item master (46-column, matches `bcmstr3.df`); `mitemdet.csv` (Mexico items) is supported in code but not currently present as a file |
| `pallets.csv` | Every successful pallet print (§3.2/§3.3) | EOL Scan lookup | **The pallet registry** — no header row |
| `eol-scans.csv` | Every EOL confirmation | EOL Scan's "last 15" grid | Local audit trail of EOL transactions |
| `pallet-serial-{plant}.txt` | `AllocatePalletSerialAsync` | same | Per-plant running pallet-serial counter |
| `sap-output/*.xml` | EOL confirmation | (external SAP ingestion, out of scope) | One backflush XML per EOL scan |
| `thermal_{type}_last.txt` | Every label print | (troubleshooting) | Last raw command text sent to the printer |
| `carton-settings.json` | Settings screen | `SettingsManager` | All `AppSettings` fields |

### 6.2 A subtlety: not every Settings CSV path field is real

`BoxesCsvPath`, `StackersCsvPath`, `ItemdetCsvPath`, `MitemdetCsvPath` exist in `AppSettings`/Settings UI but are **cosmetic only** — nothing in `FileBoxRepository` actually reads them; file resolution always goes through `_dataDirectory + "{name}{line}.csv"` convention. Don't assume editing them changes where the app looks. **`PalletsCsvPath` is the one exception** — it's genuinely wired into `FileBoxRepository`'s constructor and used for real (see next section).

### 6.3 Multi-station / shared-drive setup

Since pallet printing and EOL scanning can run on different physical stations, `AppSettings.PalletsCsvPath` lets you point every station's `pallets.csv` at the same shared network file (e.g. `\\server\share\StandAlone\pallets.csv`). Leave blank to default to the local `data\pallets.csv` (fine if the whole `data\` folder is already a shared location). All stations that need to see the same pallets **must** agree on this path.

---

## 7. Deployment

`StandAlone.CartonUi.exe` is a single command-line surface with four modes:

| Flag | Launches |
|---|---|
| *(none)* | `MainForm` — full interactive app (carton PLC mode or manual mode per `CartonPrintMode`) |
| `--pallet` | `PalletStartupDialog` → `PalletScanForm` directly |
| `--eol` | `PalletStartupDialog` → `EolScanForm` directly |
| `--settings` | `SettingsForm` directly (password-gated) |

### 7.1 Self-contained publish

For a station with no .NET runtime installed:
```
dotnet publish StandAlone.CartonUi/StandAlone.CartonUi.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o <target-folder>
```
This produces one ~115MB self-contained exe. It does **not** include runtime data — copy alongside it:
```
<target-folder>\
  StandAlone.CartonUi.exe
  carton-settings.json      ← per-station config (PLC address, printer address, etc.)
  data\                     ← CSVs, pallet/EOL registries, sato template runtime dir doesn't live here
  sato_templates\           ← .sato template files
  DoesManStk, DoesTwoPrims  ← optional flag files (only if the feature is used)
```
The whole folder is portable — copy it to a USB drive and onto another Windows x64 PC; no install needed. Windows SmartScreen may warn on first launch (unsigned exe) — "More info" → "Run anyway".

⚠️ `SatoTemplateResolver.FindTemplateDirectory` has a **hardcoded absolute fallback** to `c:\workspace\southalr\sato_templates` if it can't find a `sato_templates` folder by walking up from the exe's location. This only matters if `sato_templates\` is ever *not* copied alongside the exe — as long as it's a sibling folder (as shown above), the fallback is never reached.

---

## 8. Known gaps & limitations

### 8.1 Pallet label content gaps (vs. the real `dtlbl101b.i`)
- **QC-hold audit flag** always prints blank — no `qc-holds` table equivalent exists in this system.
- **Machine/terminal footer** uses `Environment.MachineName` and a settings-derived id, not the real unix `t-machine`/device-path values.
- **Only one printer-model branch ported** (M8400RV/84Pro coordinates). The S86R/S86L/85R/85x branch from the legacy code was not requested/ported.
- **`Grade`** is the raw item-master int code (0–9), not the looked-up 3-character description (e.g. "STD") from the legacy `grade` table — no such lookup table exists here.
- **Customer/HCS pallet label** (`dtlbl102d.i`) was never ported — only the "Plant" and "WMS" pallet formats exist in legacy, and only "WMS" is implemented here.

### 8.2 Auto-print timestamp race (flagged, not fixed)
On the PLC "8" auto-print path, `AppendPlcFrameToBrowseAsync` (writes `boxes.csv`, computes its own barcode from `DateTime.Now`) and `AutoPrintForPlc8Async` (prints the label, via a *separate* `DateTime.UtcNow` inside `BuildThermalPayload`) are two independent, unsynchronized handlers reacting to the same event. They normally agree because the barcode formula only has day-level granularity, but nothing structurally guarantees it — a midnight-boundary race is theoretically possible. This is a live, PLC-triggered path that needs real hardware to test safely, so it was deliberately left alone rather than refactored blind.

### 8.3 SAP backflush is pallet-level only
`PalletIntegrationPayload`'s `ConfirmedQty` is the pallet's total pieces (`LisQty × BoxesPerPallet`). The legacy `PROC-create-MfgOrdConf` confirmed quantity against a specific manufacturing-order **operation/route** (`MfgOrdRoutes`), which has no equivalent table in this system.

### 8.4 Console/Worker pipeline is disconnected
As described in §2.1 — a separate prototype, not exercised by the interactive app, with its own (stub) label printer that doesn't produce real SATO commands.

---

## 9. Change log (this engagement)

1. **Pallet label printing as carton format** — `SatoTemplateResolver` ignored `LabelFormat` and matched a carton-sized template by coincidence; fixed to bypass template lookup entirely for `PALLET_LABEL`.
2. **`BuildSatoPallet` full rewrite** — replaced an invented layout with a faithful `dtlbl101b.i` port (coordinates, QC-flag position, footer, carton-reference barcode, pallet-ID barcode).
3. **Settings Cancel button fixed** — was setting `DialogResult` with no `Close()` call, which only auto-closes inside a `ShowDialog()` loop; `SettingsForm` runs as the top-level `Application.Run` form in `--settings` mode.
4. **Thermal archive file** changed from one timestamped file per print to a single overwritten `thermal_{type}_last.txt`.
5. **EOL scanning feature built from scratch**: pallet registry (`pallets.csv`, `PalletRecord`), EOL scan log (`eol-scans.csv`, `EolScanRecord`), extended `PalletIntegrationPayload` with backflush-shaped fields, `EolScanForm`, `--eol` launch flag (reusing `PalletStartupDialog`).
6. **Carton barcode reprint-drift bug fixed** — reprints now reuse the box's stored barcode instead of recomputing from "now".
7. **`PalletScanForm` payload parity fix** — it was built before the reference-barcode/UserId/PrinterTermId/WmsUom fields existed and never got them; now matches the manual Pallet Query flow.
8. **Self-contained deployment** to `c:\workspace\SA` (single-file, win-x64, no runtime dependency).
9. **`PalletsCsvPath` shared-drive setting** — added and genuinely wired (unlike the pre-existing cosmetic CSV path fields) so multi-station deployments can centralize the pallet registry.
