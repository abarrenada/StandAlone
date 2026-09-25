using StandAlone.CartonUi.Models;

namespace StandAlone.CartonUi.Services;

/// <summary>
/// Abstraction over the data source that provides box and stacker records.
/// In the legacy system these came from the Progress <c>box</c> and <c>stacker</c> tables.
/// Implement against Oracle or any other source by swapping this interface.
/// </summary>
public interface IBoxRepository
{
    /// <summary>Returns up to <paramref name="count"/> most recent boxes for the line, newest first.</summary>
    Task<List<BoxRecord>> GetLastBoxesAsync(int lineId, int count, CancellationToken ct);

    /// <summary>Returns the stacker record for the given line + stack number, or null if not found.</summary>
    Task<StackerRecord?> GetStackerAsync(int lineId, string stackNum, CancellationToken ct);

    /// <summary>
    /// Returns an item display string in the format <c>itemNumber-lisQty-shade-size</c>
    /// by looking up the item reference in itemdet (or mitemdet for Mexico items).
    /// Returns null if the item is not found.
    /// </summary>
    Task<string?> GetItemDisplayAsync(int iRef, bool isMexicoItem, int shade, string size,
        bool w4DigitShade, CancellationToken ct);

    /// <summary>
    /// Returns the complete ItemDetail record by IRef from itemdet or mitemdet CSV.
    /// Returns null if not found.
    /// </summary>
    Task<ItemDetail?> GetItemDetailByIRefAsync(int iRef, bool isMexicoItem, CancellationToken ct);

    /// <summary>
    /// Returns the complete ItemDetail record by ItemNumber.
    /// Searches itemdet first, then mitemdet if <paramref name="searchMexicoAlso"/> is true.
    /// Returns null if not found in either file.
    /// </summary>
    Task<ItemDetail?> GetItemDetailByNumberAsync(string itemNumber, bool searchMexicoAlso,
        CancellationToken ct);

    /// <summary>
    /// Returns the first box record whose BarcodeSerial matches <paramref name="barcodeSerial"/>
    /// for the given line, or null if not found.
    /// </summary>
    Task<BoxRecord?> GetBoxByBarcodeSerialAsync(int lineId, string barcodeSerial, CancellationToken ct);

    /// <summary>
    /// Atomically increments and returns the next pallet serial number for the given plant.
    /// Persisted in <c>pallet-serial-{plant:000}.txt</c> in the data directory.
    /// Wraps from 999,999,999 back to 1.
    /// </summary>
    Task<int> AllocatePalletSerialAsync(int plant, CancellationToken ct);

    /// <summary>
    /// Appends a printed pallet's info to the pallet registry (<c>pallets.csv</c>) so it can
    /// later be looked up by serial during EOL scanning.
    /// </summary>
    Task SavePalletRecordAsync(PalletRecord record, CancellationToken ct);

    /// <summary>
    /// Returns the most recently printed pallet matching <paramref name="palletId"/>
    /// (format "PPP-SSSSSSSSS"), or null if it was never printed/registered.
    /// </summary>
    Task<PalletRecord?> GetPalletBySerialAsync(string palletId, CancellationToken ct);

    /// <summary>Appends one EOL scan transaction to the local log (<c>eol-scans.csv</c>).</summary>
    Task AppendEolScanAsync(EolScanRecord record, CancellationToken ct);

    /// <summary>Returns up to <paramref name="count"/> most recent EOL scans, newest first.</summary>
    Task<List<EolScanRecord>> GetLastEolScansAsync(int count, CancellationToken ct);

    /// <summary>Returns every stacker record configured for the given line (Stacker Maintenance screen).</summary>
    Task<List<StackerRecord>> GetAllStackersAsync(int lineId, CancellationToken ct);

    /// <summary>
    /// Adds or updates a stacker record, keyed by LineId+StackNum. Any existing row(s) for that
    /// key are removed first so a single, current row remains (stackers{NN}.csv has no unique-key
    /// enforcement of its own, and GetStackerAsync returns the first match, so leftover duplicate
    /// rows from hand-editing could otherwise shadow the update).
    /// </summary>
    Task SaveStackerAsync(StackerRecord record, CancellationToken ct);

    /// <summary>Removes the stacker record matching LineId+StackNum, if any.</summary>
    Task DeleteStackerAsync(int lineId, string stackNum, CancellationToken ct);

    /// <summary>
    /// Returns every itemdet/mitemdet row matching <paramref name="itemNumber"/> — an item number
    /// can have more than one row differing only by Lis Qty. Empty list if none found.
    /// </summary>
    Task<List<ItemDetail>> GetAllItemDetailsByNumberAsync(string itemNumber, bool searchMexicoAlso,
        CancellationToken ct);

    /// <summary>
    /// Returns the most recent EOL scan already logged for <paramref name="palletId"/>, or null
    /// if this pallet has never been scanned at EOL before (duplicate-scan check).
    /// </summary>
    Task<EolScanRecord?> FindEolScanByPalletIdAsync(string palletId, CancellationToken ct);

    /// <summary>
    /// Looks up a retail customer's own Customer Product Number (CPN) for the given item, matching
    /// Progress <c>bc-cpn</c> (<c>bcc-item</c>/<c>bcc-lis-qty</c>/<c>bcc-cust-nbr</c>/<c>bcc-case-cpn</c>
    /// plus the <c>bcc-ctn-*</c> carton-UPC-override fields F&amp;D "D" items use). <paramref name="custNbr"/>
    /// is the already-mapped customer-number constant (e.g. "74035" for Lowe's, "FD001" for F&amp;D — see
    /// <c>ThermalPrinterCommandBuilder.ResolveCpnCustomerNumber</c>), not the raw single-char CustomerChar.
    /// Returns null if no matching row exists in <c>bc_cpn.csv</c> — callers must decide whether that's
    /// a hard failure (F&amp;D Private Label "D" items) or a soft blank (everyone else), matching legacy's
    /// split behavior in <c>dtplc067.p</c>'s GetCpn.
    /// </summary>
    Task<CpnLookupResult?> GetCpnAsync(string itemNumber, int lisQty, string custNbr, CancellationToken ct);

    /// <summary>
    /// Looks up a brand's print-ready description and whether it should actually be printed, matching
    /// Progress <c>brand</c> (<c>br-desc</c>/<c>br-print</c>). Returns null if the brand code isn't
    /// found in <c>brands.csv</c>, or if found but <c>BrPrint</c> is false — either way the caller
    /// should treat the brand name as blank, matching legacy's <c>prt-name = ""</c> fallback.
    /// </summary>
    Task<string?> GetBrandNameAsync(string brandCode, CancellationToken ct);

    /// <summary>
    /// Returns whether the given grade code should draw the highlight box around the Qual/Cal field,
    /// matching Progress <c>grade.gr-highlight</c>. False (no highlight) if the code isn't found in
    /// <c>grades.csv</c>.
    /// </summary>
    Task<bool> GetGradeHighlightAsync(string gradeCode, CancellationToken ct);
}
