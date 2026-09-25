namespace StandAlone.CartonUi.Models;

/// <summary>
/// One matched row from bc_cpn.csv (Progress bc-cpn) — the customer's own case CPN, plus the
/// carton-UPC-override fields F&amp;D "D" items substitute for the item's own carton UPC
/// (dtplc067.p's GetCpn: <c>bcc-ctn-num-sys/mfg/prd/chkdgt</c>).
/// </summary>
public sealed record CpnLookupResult(
    string CaseCpn,
    int CtnNumSys,
    int CtnMfg,
    int CtnProd,
    int CtnChkdgt);
