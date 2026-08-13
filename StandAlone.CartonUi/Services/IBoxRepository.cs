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
}
