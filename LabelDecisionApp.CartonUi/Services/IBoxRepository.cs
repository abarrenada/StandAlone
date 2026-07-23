using LabelDecisionApp.CartonUi.Models;

namespace LabelDecisionApp.CartonUi.Services;

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
}
