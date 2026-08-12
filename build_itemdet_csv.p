/*****************************************************************************/
/* Program ID  : build_itemdet_csv.p                                        */
/* Program Desc: Exports itemdet.csv for the StandAlone Carton UI app.      */
/*               Joins itemdet + itemhdr (1 row per item/lis-qty combo),    */
/*               plus the most recent MfgSchedule record per item for the   */
/*               trailing LastScheduleOrder/OpenQty/ScheduleDate columns.   */
/*                                                                           */
/* Output format notes for the StandAlone.CartonUi consumer                 */
/* (StandAlone.CartonUi\Services\FileBoxRepository.cs, ParseItemDetail):    */
/*   - Column order below MUST match data\itemdet.csv's header row exactly. */
/*   - The C# parser does a plain line.Split(',') with NO CSV-quote         */
/*     awareness, so fields here are NOT quoted. Any comma inside a         */
/*     free-text field (Color/Shape/Series/LIS description) is stripped so */
/*     it can't shift the column alignment - do not add quotes instead,    */
/*     the consumer would keep the literal quote characters.               */
/*   - Dates are written as YYYY-MM-DD (ISO), not Progress's native MM/DD/YY*/
/*   - Logical id-need-pallet-label is written as lowercase true/false     */
/*     (not Progress's native yes/no), since that's what's parsed today.   */
/*                                                                           */
/* Adjust vOutFile below to the path the Progress team wants to publish to. */
/*****************************************************************************/

def var vOutFile   as char no-undo initial "itemdet.csv".
def var vColorDesc as char no-undo.
def var vShapeDesc as char no-undo.
def var vSeriesDesc as char no-undo.
def var vLisDesc   as char no-undo.
def var vNeedPallet as char no-undo.
def var vIhCreateDt as char no-undo.
def var vIhUpdateDt as char no-undo.
def var vLastOrder as char no-undo.
def var vOpenQty   as decimal no-undo.
def var vSchedDate as char no-undo.

/* Formats a Progress DATE as YYYY-MM-DD, or "" if unknown. */
function fDateIso returns character (input pDate as date):
  if pDate = ? then
    return "".
  return string(year(pDate),"9999") + "-" +
         string(month(pDate),"99")  + "-" +
         string(day(pDate),"99").
end function.

/* Strips commas so a free-text field can't shift CSV column alignment
   (the C# consumer does a plain Split(',') with no quote support). */
function fCsvSafe returns character (input pValue as character):
  if pValue = ? then
    return "".
  return replace(pValue, ",", " ").
end function.

output to value(vOutFile).

/* Header row - must match data\itemdet.csv exactly */
put unformatted
  "IRef,ItemNumber,LisQty,SalesQty,SalesUOM,PkgWeight,"
  "CartonUPC_NumSys,CartonUPC_Mfg,CartonUPC_Prod,CartonUPC_Chkdgt,"
  "CustomerChar,Grade,PkgIndicator,CardPrinter,LISDescription,Shade,"
  "BoxesPerPallet,NeedPalletLabel,Company,Status,Extract,"
  "ColorDesc,ShapeDesc,SeriesDesc,Brand,TypeOfTile,ColorId,SizeShape,"
  "WmsUOM,Plant,ProductType,SingleUPC_NumSys,SingleUPC_Mfg,SingleUPC_Prod,"
  "SingleUPC_Chkdgt,PEI,WA,COF,Tone,CreateUser,UpdateUser,CreateDate,"
  "UpdateDate,LabelTypeCode,LastScheduleOrder,OpenQty,ScheduleDate"
  skip.

for each itemdet no-lock:

  find itemhdr no-lock
       where itemhdr.ih-item-nbr = itemdet.id-item-nbr
       no-error.
  if not available itemhdr then
    next.

  /* Most recent MfgSchedule record for this item (by start date) */
  vLastOrder = "".
  vOpenQty   = 0.
  vSchedDate = "".
  for each MfgSchedule no-lock
      where MfgSchedule.msch-item-nbr = itemdet.id-item-nbr
      by MfgSchedule.msch-start-date descending:
    vLastOrder = MfgSchedule.msch-ord-nbr.
    vOpenQty   = MfgSchedule.msch-ord-qty.
    vSchedDate = fDateIso(MfgSchedule.msch-start-date).
    leave.
  end.

  vColorDesc   = fCsvSafe(itemhdr.ih-color-desc).
  vShapeDesc   = fCsvSafe(itemhdr.ih-shape-desc).
  vSeriesDesc  = fCsvSafe(itemhdr.ih-series-desc).
  vLisDesc     = fCsvSafe(itemdet.id-lis-desc).
  vNeedPallet  = if itemdet.id-need-pallet-label then "true" else "false".
  vIhCreateDt  = fDateIso(itemhdr.ih-create-dte).
  vIhUpdateDt  = fDateIso(itemhdr.ih-update-dte).

  put unformatted
      itemdet.id-iref                 "," /* IRef              */
      itemdet.id-item-nbr             "," /* ItemNumber         */
      itemdet.id-lis-qty              "," /* LisQty             */
      itemdet.id-sales-qty            "," /* SalesQty           */
      itemdet.id-sales-um             "," /* SalesUOM           */
      itemdet.id-pkg-wgt              "," /* PkgWeight          */
      itemdet.id-ctn-num-sys          "," /* CartonUPC_NumSys   */
      itemdet.id-ctn-mfg              "," /* CartonUPC_Mfg      */
      itemdet.id-ctn-prd              "," /* CartonUPC_Prod     */
      itemdet.id-ctn-chkdgt           "," /* CartonUPC_Chkdgt   */
      itemdet.id-cust-char            "," /* CustomerChar       */
      itemdet.id-grade                "," /* Grade              */
      itemdet.id-pkg-indicator        "," /* PkgIndicator       */
      itemdet.id-card-ptr             "," /* CardPrinter        */
      vLisDesc                        "," /* LISDescription     */
      itemdet.id-shade                "," /* Shade              */
      itemdet.id-boxes-per-pallet     "," /* BoxesPerPallet     */
      vNeedPallet                     "," /* NeedPalletLabel    */
      itemdet.id-company              "," /* Company            */
      itemdet.id-status                "," /* Status             */
      itemdet.id-extract               "," /* Extract            */
      vColorDesc                      "," /* ColorDesc          */
      vShapeDesc                      "," /* ShapeDesc          */
      vSeriesDesc                     "," /* SeriesDesc         */
      itemhdr.ih-brand                 "," /* Brand              */
      itemhdr.ih-type-of-tile          "," /* TypeOfTile         */
      itemhdr.ih-color-id              "," /* ColorId            */
      itemhdr.ih-size-shape            "," /* SizeShape          */
      itemhdr.ih-wms-uom               "," /* WmsUOM             */
      itemhdr.ih-plant                 "," /* Plant              */
      itemhdr.ih-product-type          "," /* ProductType        */
      itemhdr.ih-sgl-num-sys           "," /* SingleUPC_NumSys   */
      itemhdr.ih-sgl-mfg               "," /* SingleUPC_Mfg      */
      itemhdr.ih-sgl-prd               "," /* SingleUPC_Prod     */
      itemhdr.ih-sgl-chkdgt            "," /* SingleUPC_Chkdgt   */
      itemhdr.ih-pei                   "," /* PEI                */
      itemhdr.ih-wa                    "," /* WA                 */
      itemhdr.ih-cof                   "," /* COF                */
      itemhdr.ih-tone                  "," /* Tone               */
      itemhdr.ih-create-user           "," /* CreateUser         */
      itemhdr.ih-update-user           "," /* UpdateUser         */
      vIhCreateDt                     "," /* CreateDate         */
      vIhUpdateDt                     "," /* UpdateDate         */
      itemhdr.ih-label-type-code       "," /* LabelTypeCode      */
      vLastOrder                      "," /* LastScheduleOrder  */
      vOpenQty                        "," /* OpenQty            */
      vSchedDate                            /* ScheduleDate       */
      skip.

end.

output close.

message "itemdet.csv export complete: " vOutFile view-as alert-box.
