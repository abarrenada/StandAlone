namespace LabelDecisionApp.CartonUi.Models;

/// <summary>
/// One entry from the LabelSzPrmpt config file.
/// Progress arrays: aSizes, aSzPrmpt, aInclUs, aInclMx.
/// </summary>
public class LabelSizeOption
{
    /// <summary>The size code sent to the PLC, e.g. "4.5x3".</summary>
    public string SizeCode { get; set; } = string.Empty;

    /// <summary>Human-readable description shown on screen.</summary>
    public string Prompt { get; set; } = string.Empty;

    public bool IncludeUs { get; set; }
    public bool IncludeMexico { get; set; }

    public override string ToString() => $"{SizeCode,-10} {Prompt}";
}
