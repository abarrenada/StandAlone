/*--------------------------------------------------------------
   export_item_label_types.p

   Exports item number, plant, and label type code from the
   Progress item master table to a CSV file.

   Usage:
     prowin32 -p export_item_label_types.p -param "c:\\temp\\item_label_types.csv"

   If no parameter is supplied, the file is written to:
     c:\\workspace\\SFC\\item_label_types.csv
--------------------------------------------------------------*/

DEFINE INPUT PARAMETER pcOutputFile AS CHARACTER NO-UNDO
  INITIAL "c:\\workspace\\SFC\\item_label_types.csv".

DEFINE VARIABLE cItemNbr AS CHARACTER NO-UNDO.
DEFINE VARIABLE iPlant AS INTEGER NO-UNDO.
DEFINE VARIABLE iLabelType AS INTEGER NO-UNDO.

OUTPUT TO VALUE(pcOutputFile).

PUT UNFORMATTED "item_nbr,plant,label_type_code" SKIP.

FOR EACH bcmstr3.itemhdr NO-LOCK
    WHERE bcmstr3.itemhdr.ih-item-nbr <> "":

  ASSIGN
    cItemNbr = TRIM(STRING(bcmstr3.itemhdr.ih-item-nbr))
    iPlant = bcmstr3.itemhdr.ih-plant
    iLabelType = bcmstr3.itemhdr.ih-label-type-code.

  PUT UNFORMATTED
    '"' + REPLACE(cItemNbr, '"', '""') + '",' +
    STRING(iPlant) + ',' +
    STRING(iLabelType) SKIP.
END.

OUTPUT CLOSE.

MESSAGE "Export complete: " + pcOutputFile VIEW-AS ALERT-BOX.
