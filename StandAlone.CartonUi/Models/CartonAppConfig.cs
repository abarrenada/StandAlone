namespace StandAlone.CartonUi.Models;

/// <summary>
/// Runtime configuration for the Carton Label Printing module.
/// Loaded by <see cref="StandAlone.CartonUi.Services.CartonConfigLoader"/>.
/// </summary>
public class CartonAppConfig
{
    public int LineNumber { get; set; } = 1;

    /// <summary>DoesManStk — manually set sorter stacks via a maintenance program.</summary>
    public bool DoesManStk { get; set; }

    /// <summary>DoesTwoPrims — supports two primary item numbers.</summary>
    public bool DoesTwoPrims { get; set; }

    /// <summary>s-does-mexico — plant uses Mexico item numbers (mitemdet).</summary>
    public bool DoesMexico { get; set; }

    /// <summary>w-4digitshade — shade is stored as 4 digits (no ×10 scaling).</summary>
    public bool W4DigitShade { get; set; }

    public string DataDirectory { get; set; } = string.Empty;

    public List<LabelSizeOption> LabelSizes { get; set; } = new();

    /// <summary>Comma-delimited, with leading and trailing commas: ",sz1,sz2,"</summary>
    public string ValidLabelSizes { get; set; } = string.Empty;

    // ── Computed paths matching Progress file conventions ─────────────────

    public string PlcPipePath       => Path.Combine(DataDirectory, $"plc{LineNumber:00}");
    public string NoGoFilePath      => Path.Combine(DataDirectory, "VfyScn", $"NoGo{LineNumber:00}.txt");
    public string MatchFilePath     => Path.Combine(DataDirectory, "VfyScn", $"Match{LineNumber:00}.txt");

    private string LogFile     => Path.Combine(DataDirectory, $"Log{LineNumber:00}.txt");
    private string LogFileAlt  => Path.Combine(DataDirectory, "VfyScn", $"Log{LineNumber:00}.txt");
    private string RptFile     => Path.Combine(DataDirectory, $"Rpt{LineNumber:00}.txt");
    private string RptFileAlt  => Path.Combine(DataDirectory, "VfyScn", $"Rpt{LineNumber:00}.txt");

    public string? ResolvedLogFilePath =>
        File.Exists(LogFile) ? LogFile :
        File.Exists(LogFileAlt) ? LogFileAlt : null;

    public string? ResolvedRptFilePath =>
        File.Exists(RptFile) ? RptFile :
        File.Exists(RptFileAlt) ? RptFileAlt : null;
}
