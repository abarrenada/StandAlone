using System.Globalization;
using System.Text;

namespace StandAlone.Integration.Services;

/// <summary>
/// "OracleDirect" backflush vehicle — for standard (non-El-Paso) plants, whose real
/// outbound path today is Progress bcmstr3 → Oracle → SAP. Calls the same Oracle
/// stored procedure Progress's sendprod-orawms.p calls for a receiving transaction
/// (ts-trans-type = "A") — p_insert_interface_receipt, in the manuf_interfaces
/// package — directly via OracleInterfaceService, skipping Progress/the TCP-socket
/// hop in between. El Paso has no Oracle database at all — its real path is
/// SAP ME → BizTalk → SAP, i.e. the "BizTalk" vehicle (FileSapIntegrationService)
/// instead; see project_oracle_schema_holder_pipeline / project_el_paso_sfc_architecture
/// in the migration notes. The Oracle hop is expected to be retired for every plant
/// eventually, which is why AppSettings.BackflushVehicle defaults to "BizTalk".
///
/// The 300-byte fixed-width DATA_IN string below mirrors sendprod-orawms.p's sendstr
/// build field-for-field (see BuildSendString). Only ts-trans-type = "A" (receipt) is
/// modeled — inventory adjustments (any other trans-type, which call
/// p_insert_interface_inv_update instead) don't have an equivalent flow in this port.
///
/// Known gaps — fields the real wmstosend row carries that this port has no source
/// for, sent as blank/zero rather than omitted (the fixed-width format has no concept
/// of "absent"): iref (Progress's internal item reference number), comment,
/// packconfig, lockcode (no qc-holds equivalent — see project_standalone_migration's
/// documented gap), mfg-ord-nbr/line, sales-ord-nbr/line, jv-flag, bi-prod-flag.
/// calbr (caliber) is approximated from the first character of
/// PalletIntegrationPayload.Size, and gradedesc from the raw numeric grade code (same
/// simplification already documented for ItemDetail.Grade) — neither is the real
/// Progress value. sales-factor/sales-qty reflect
/// PalletIntegrationPayload.SalesQty/SalesUom, which EolScanForm doesn't currently
/// populate (pre-existing gap, not new here) — so these typically encode as zero.
/// </summary>
public class OracleSapIntegrationService : ISapIntegrationService
{
    private const string ReceiptProcedureName = "p_insert_interface_receipt";

    private readonly IOracleInterfaceService _oracle;

    public OracleSapIntegrationService(IOracleInterfaceService oracle)
    {
        _oracle = oracle;
    }

    public async Task<SapIntegrationResult> SendPalletIntegrationAsync(PalletIntegrationPayload payload, CancellationToken cancellationToken)
    {
        try
        {
            var dataIn = BuildSendString(payload);
            var result = await _oracle.CallInterfaceProcedureAsync(ReceiptProcedureName, dataIn, cancellationToken);

            var statusCode = result.Length >= 3 ? result[..3] : result;
            return statusCode is "SUC" or "DUP"
                ? new SapIntegrationResult { Success = true }
                : new SapIntegrationResult { Success = false, ErrorMessage = $"Oracle returned: {result}" };
        }
        catch (Exception ex)
        {
            return new SapIntegrationResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Field-for-field port of sendprod-orawms.p's sendstr build (300 bytes, space-padded).
    /// Widths below are load-bearing — the Oracle package parses this by fixed position,
    /// same as Progress does; changing a width shifts every field after it.
    /// </summary>
    private static string BuildSendString(PalletIntegrationPayload payload)
    {
        var serialDigits = DigitsOnly(payload.SerialNumber);
        var shadeDigits = DigitsOnly(payload.Shade);

        var sb = new StringBuilder(300);
        sb.Append('F');                                                              // 1  literal
        sb.Append('A');                                                              // 1  ts-trans-type (receipt only, this port)
        sb.Append(PadNumeric(payload.Plant.ToString(CultureInfo.InvariantCulture), 3)); // 3  plant
        sb.Append(PadNumeric(serialDigits, 9));                                      // 9  serial number
        sb.Append(PadRight(payload.ItemNumber, 15));                                 // 15 item nbr
        sb.Append(PadNumeric("0", 6));                                               // 6  iref — gap
        sb.Append(PadNumeric(payload.ConfirmedQty.ToString(CultureInfo.InvariantCulture), 5)); // 5  carton-qty (LisQty)
        sb.Append(EncodeFixedDecimal(payload.Cartons, intDigits: 4, fracDigits: 0, signed: true)); // 5  numctns
        sb.Append(payload.ReceivedAt.ToString("yyyyMMdd", CultureInfo.InvariantCulture)); // 8  mfg-date (approximated from scan time)
        sb.Append(PadNumeric(shadeDigits, 4));                                       // 4  shade
        sb.Append(string.IsNullOrEmpty(payload.Size) ? " " : payload.Size[..1]);     // 1  calbr — approximated
        sb.Append(PadNumeric(Math.Abs(payload.Shift).ToString(CultureInfo.InvariantCulture), 1)); // 1  shift
        sb.Append(PadNumeric(payload.LineNumber.ToString(CultureInfo.InvariantCulture), 2)); // 2  mfg-line
        sb.Append(PadRight(payload.Location, 6));                                    // 6  location
        sb.Append(PadRight(payload.ShopOrder, 11));                                  // 11 ordernum
        sb.Append(PadRight(string.Empty, 35));                                       // 35 comment — gap
        sb.Append(payload.ConfirmedAt.ToString("yyyyMMdd", CultureInfo.InvariantCulture)); // 8  trans-date
        sb.Append(payload.ConfirmedAt.ToString("HHmmss", CultureInfo.InvariantCulture));   // 6  trans-time
        sb.Append(EncodeFixedDecimal(SafeDivide(payload.SalesQty, payload.ConfirmedQty), intDigits: 7, fracDigits: 4, signed: false)); // 11 sales-factor
        sb.Append(EncodeFixedDecimal(payload.SalesQty, intDigits: 9, fracDigits: 4, signed: true)); // 14 sales-qty
        sb.Append(PadRight(payload.SalesUom, 4));                                    // 4  sales-um
        sb.Append(PadRight(Environment.MachineName, 9));                             // 9  hostname
        sb.Append(PadRight(string.Empty, 5));                                        // 5  lockcode — gap (no qc-holds equivalent)
        sb.Append(PadNumeric(payload.ConfirmedQty.ToString(CultureInfo.InvariantCulture), 7)); // 7  numpieces
        sb.Append(PadRight(string.Empty, 12));                                       // 12 packconfig — gap
        sb.Append(PadRight(payload.Grade.ToString(CultureInfo.InvariantCulture), 3)); // 3  gradedesc — raw code, not description
        sb.Append(PadRight(payload.Inspector, 8));                                   // 8  trnuser
        sb.Append(PadRight(string.Empty, 12));                                       // 12 mfg-ord-nbr — gap
        sb.Append(PadNumeric("0", 4));                                               // 4  mfg-ord-line-nbr — gap
        sb.Append(PadRight(string.Empty, 10));                                       // 10 sales-ord-nbr — gap
        sb.Append(PadNumeric("0", 6));                                               // 6  sales-ord-line-nbr — gap
        sb.Append(' ');                                                              // 1  jv-flag — gap
        sb.Append(' ');                                                              // 1  bi-prod-flag — gap

        if (sb.Length < 300)
            sb.Append(' ', 300 - sb.Length);

        return sb.ToString(0, 300);
    }

    private static decimal SafeDivide(decimal numerator, int denominator) =>
        denominator == 0 ? 0m : numerator / denominator;

    private static string DigitsOnly(string value) =>
        new(value.Where(char.IsDigit).ToArray());

    private static string PadRight(string value, int width) =>
        (value.Length > width ? value[..width] : value).PadRight(width);

    /// <summary>Unsigned, zero-padded on the left; truncates from the left (keeps the
    /// least-significant digits) if <paramref name="digits"/> is longer than <paramref name="width"/>.</summary>
    private static string PadNumeric(string digits, int width) =>
        (digits.Length > width ? digits[^width..] : digits).PadLeft(width, '0');

    /// <summary>
    /// Mirrors Progress's habit of formatting a decimal (e.g. "9999999.9999") then
    /// stripping the literal decimal point to get a fixed-width digit string. Width =
    /// (signed ? 1 : 0) + intDigits + fracDigits.
    /// </summary>
    private static string EncodeFixedDecimal(decimal value, int intDigits, int fracDigits, bool signed)
    {
        var sign = signed ? (value < 0 ? "-" : " ") : string.Empty;

        var scale = 1L;
        for (var i = 0; i < fracDigits; i++)
            scale *= 10;

        var scaled = (long)Math.Round(Math.Abs(value) * scale, MidpointRounding.AwayFromZero);
        var intPart = (scaled / scale).ToString(CultureInfo.InvariantCulture);
        var fracPart = (scaled % scale).ToString(CultureInfo.InvariantCulture).PadLeft(fracDigits, '0');

        if (intPart.Length > intDigits)
            intPart = intPart[^intDigits..];
        intPart = intPart.PadLeft(intDigits, '0');

        return sign + intPart + fracPart;
    }
}
