namespace StandAlone.CartonUi.Models;

/// <summary>
/// Represents a stacker record, equivalent to the Progress <c>stacker</c> table.
/// </summary>
public class StackerRecord
{
    public int LineId { get; set; }

    /// <summary>2-character stack number.</summary>
    public string StackNum { get; set; } = string.Empty;

    /// <summary>Item reference key linking to itemdet/mitemdet.</summary>
    public int IRef { get; set; }

    public string PlcMsg { get; set; } = string.Empty;
    public int Shade { get; set; }
    public string Size { get; set; } = string.Empty;
    public string ErrMsg { get; set; } = string.Empty;

    /// <summary>Progress: <c>stacker.st-errmsg begins "*OK" or stacker.st-errmsg eq ""</c></summary>
    public bool IsValid => string.IsNullOrEmpty(ErrMsg) || ErrMsg.StartsWith("*OK", StringComparison.OrdinalIgnoreCase);

    /// <summary>Progress: <c>substring(stacker.st-plcmsg, 33, 2) eq "M-"</c></summary>
    public bool IsMexicoItem => PlcMsg.Length >= 34 && PlcMsg.Substring(32, 2) == "M-";
}
