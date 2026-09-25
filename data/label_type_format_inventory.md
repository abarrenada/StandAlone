# Label Template Cross-Reference

How to determine which `.sato` template a given `itemdet.csv` row resolves to, mirroring
the real Progress dispatch logic in `dtplc067.p` (`c:\Workspace\southalr`) and this port's
`SatoTemplateResolver.ResolveTemplateFileName` (`StandAlone.Integration/Services/SatoTemplateResolver.cs`).

> **Supersedes the previous version of this file** (dated 2026-07-29), which claimed
> `LabelTypeCode` directly encodes the retail customer (e.g. "2 = LOWES", "3 = HOME DEPOT").
> That's wrong — see "What LabelTypeCode is NOT" below. It was corrected 2026-08-25 after
> tracing the actual dispatch logic in `dtplc067.p` field-by-field against source.

## The fields involved

| Field | itemdet.csv column | Progress source | What it actually does |
|---|---|---|---|
| `CustomerChar` | 11 (`id-cust-char`) | `itemdet.id-cust-char` | Blank = Manufacturing. H/L/B/D/F = a real retail customer → Standard Retail. **This is what actually decides "whose label is this."** |
| `LabelTypeCode` | 44 (`ih-label-type-code`) | `itemhdr.ih-label-type-code` | Only 2 values matter to `dtplc067.p`: **4** = F&D Private Label, **6** = CrossOver. Everything else (0, blank, or any other value) is "neither" and falls through to the CustomerChar check. |
| `PanelType` | 48 (new, added 2026-08-24) | `itemhdr.ih-label-type` | Only meaningful on **Standard Retail** labels. 1 = brand name, 2 = customer part number (CPN), 3 = PEI/WA/COF/Tone icon stack. Not populated in `itemdet.csv` yet — see Gaps below. |
| Mexico flag | not a column | `MEXICO-ONLY` trigger file + `stacker.st-plcmsg` "M-" marker | Set at the plant/environment level, not per-item. Routes to the Mexico family (`mitemdet`/`mitemhdr`, a table we don't have a data source for at all — see Gaps). |
| Label size | not a column | `prt-label-size` (passed by the sorter/stacker at print time) | Picks *which* of the 5 files within a family (e.g. 4.5x3 vs 2x6). Never stored on the item — comes from the physical stacker/line configuration at print time. |

### What LabelTypeCode is NOT

It is **not** a customer/brand code. The 1/2/3 values you'll see it compared against in a
handful of *other* legacy programs (`dtlbl061n.i`, `dtlbl062n.i`, `dtlbl090n.i`, etc. — not
`dtplc067.p`) are that program's own re-use of the same field name for the *panel* concept
(brand/CPN/icons) — a different meaning, on a different code path, not one we've traced as
reachable from the driver this app ports. Don't infer a customer from `LabelTypeCode` alone;
use `CustomerChar`.

## Decision tree

```
1. Is this a Mexico item (MEXICO-ONLY flag + PLC "M-" marker)?
     yes → Mexico family (dtlbl066n/066m/060p.i)      [no data source in this port yet]
     no  → continue

2. LabelTypeCode = 4?
     yes → F&D Private Label → lt04_default_{size}.sato
     no  → continue

3. LabelTypeCode = 6?
     yes → CrossOver → lt06_xover_{size}_{ref-first|ref-last}.sato
     no  → continue

4. CustomerChar blank?
     yes → Manufacturing → lt_default_{size}.sato
     no  → Standard Retail (CustomerChar = H/L/B/D/F) → lt_retail_{size}.sato
```

`{size}` is one of `45x3`, `4x3`, `2x725`, `2x6`, `3x45`, `175x838` — supplied by the print
job, not read from `itemdet.csv`.

## Worked examples (real rows from itemdet.csv, 2026-08-25)

**Manufacturing** — IRef 412736, `00001110X20MS1P`: `CustomerChar` blank, `LabelTypeCode` 0.
→ Mexico? no. LabelTypeCode 4/6? no. CustomerChar blank? yes → `lt_default_{size}.sato`.

**Standard Retail** — IRef 562916, `0000CLIKS`: `CustomerChar` H, `LabelTypeCode` 0.
→ Mexico? no. LabelTypeCode 4/6? no. CustomerChar blank? no (H) → `lt_retail_{size}.sato`.
(`PanelType` is empty on this row today, so the right-edge panel will render blank until
Progress starts populating that column.)

**F&D Private Label** — IRef 611776, `100507680`: `CustomerChar` D, `LabelTypeCode` 4.
→ Mexico? no. LabelTypeCode = 4 → `lt04_default_{size}.sato`, regardless of CustomerChar.

## Observed data patterns (whole-file scan, 2026-08-25)

- `LabelTypeCode` distribution: 0 (204,336 rows), 2 (2,221), 1 (1,194), 3 (852), **4 (121)**, **6 (0)**.
- Every row with `CustomerChar = D` has `LabelTypeCode = 4` (121 of each) — consistent with
  F&D Private Label being the "D" customer's family, per `dtplc067.p`'s `GetCpn`.
- **Zero rows have `LabelTypeCode = 6`** — matches the earlier finding that CrossOver is
  confirmed not in production use. Don't be surprised if a CrossOver template never actually
  gets exercised against real data.
- `CustomerChar` distribution: blank (204,443), H (1,910), L (1,338), B (813), D (121), F (84),
  plus a few dozen single stray characters/digits — almost certainly column-misalignment
  artifacts from malformed source rows (a free-text field with an unescaped comma), not real
  customer codes. Don't build logic that trusts an unrecognized `CustomerChar` value blindly.
- The `LabelTypeCode` column also has a handful of rows containing a literal date string
  instead of a number (e.g. `12/16/2002`) — same kind of malformed-row artifact.

## Gaps / things this doc can't fully answer yet

- **`PanelType` is not populated.** The column exists in `itemdet.csv` and the code reads it,
  but no real export has run with the updated `build_itemdet_csv.p` yet, so every row shows
  blank/0 — every Standard Retail label's side panel will render empty until that changes.
- **No Mexico item data source exists** (`mitemdet.csv` doesn't exist in this repo's `data/`
  folder). Mexico routing can be reasoned about but not actually exercised today.
- **Non-tile SKUs may not go through this driver at all.** Several `CustomerChar`/`LabelTypeCode`
  combinations belong to items that read as bath hardware, accessories, etc. (e.g. `ColorDesc`
  containing "SOAP DISH", `SeriesDesc` "BATHROOM ACCESSORIES") rather than tile. It's plausible
  those product lines are printed by an entirely different Progress program, not `dtplc067.p`.
  This doc's decision tree only applies to items actually printed through the driver this app
  ports — verify against the real plant/line before trusting it for an unfamiliar item.
