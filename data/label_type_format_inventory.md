# Label Type Inventory (Business Mapping)

Generated: 2026-07-29
Base item used for examples: FL9036MOD1P4 (IRef 615229, Plant 610, Shade 555)

## Business classification

| LabelTypeCode | Business Name | Notes |
|---|---|---|
| 0 | DALTILE | confirmed |
| 1 | pending | business name pending |
| 2 | LOWES | confirmed |
| 3 | HOME DEPOT | confirmed |
| 4 | pending | business name pending |
| 5 | pending | business name pending |
| 6 | pending | business name pending |

## Format/process separation

- Carton labels: use 4x3 size family.
- Pallet labels: use 6x4 size family.
- LabelTypeCode 0..6 applies to both carton and pallet.
- Slab: separate process path with one slab label template.

## Example files

- labeltype_00_CARTON_LABEL_sato.txt
- labeltype_01_SLAB_LABEL_sato.txt
- labeltype_02_PALLET_LABEL_sato.txt
- labeltype_03_FINISHED_GOOD_LABEL_sato.txt
- labeltype_04_WIP_LABEL_sato.txt

### ZPL

- labeltype_00_CARTON_LABEL_zpl.txt
- labeltype_01_SLAB_LABEL_zpl.txt
- labeltype_02_PALLET_LABEL_zpl.txt
- labeltype_03_FINISHED_GOOD_LABEL_zpl.txt
- labeltype_04_WIP_LABEL_zpl.txt

### IPL

- labeltype_00_CARTON_LABEL_ipl.txt
- labeltype_01_SLAB_LABEL_ipl.txt
- labeltype_02_PALLET_LABEL_ipl.txt
- labeltype_03_FINISHED_GOOD_LABEL_ipl.txt
- labeltype_04_WIP_LABEL_ipl.txt

## Notes

- Current app implementation now treats LabelTypeCode as business template variant name, not as carton/slab/pallet selector.
- Missing business names for codes 1, 4, 5 and 6 are kept as pending placeholders in generated output.
- This folder includes a 3-language matrix (SATO, ZPL, IPL) for label types 0..4; we can extend to 5 and 6 once names are provided.

## Business-correct SATO examples (new)

- labeltype_variant_0_daltile_carton_4x3_sato.txt
- labeltype_variant_1_pending_carton_4x3_sato.txt
- labeltype_variant_2_lowes_carton_4x3_sato.txt
- labeltype_variant_3_home_depot_carton_4x3_sato.txt
- labeltype_variant_4_pending_carton_4x3_sato.txt
- labeltype_variant_5_pending_carton_4x3_sato.txt
- labeltype_variant_6_pending_carton_4x3_sato.txt
- labeltype_variant_0_daltile_pallet_6x4_sato.txt
- slab_single_process_label_sato.txt
