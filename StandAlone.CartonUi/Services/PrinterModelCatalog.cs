namespace StandAlone.CartonUi.Services;

/// <summary>
/// Which physical reference-point move a SATO print engine needs before drawing a label —
/// Progress's ws-dev-model 3-way branch (dtlbl060b.i/dtmlb001.i): right-hand engines vs. two
/// left-hand variants. Not derivable from source (see PrinterModelCatalog); this is catalog data.
/// </summary>
public enum EngineHand
{
    Right,
    Left85L,
    LeftS86LD,
}

/// <summary>
/// Small standalone catalog mapping a printer's marketing model name (e.g. "M84Pro") to its
/// physical engine hand, read from <c>printer_models.csv</c> (format: ModelName,EngineHand,Verified).
///
/// Deliberately not part of IBoxRepository — that interface is scoped to box/item/stacker/pallet
/// domain data, not printer configuration. Kept editable as a plain CSV (not hardcoded) because
/// the real-world model-to-hand mapping is tribal knowledge with no source-of-truth in the legacy
/// system either (see plan notes) — a technician can correct one row without a rebuild.
/// </summary>
public class PrinterModelCatalog
{
    private readonly string _filePath;

    public PrinterModelCatalog(string dataDirectory)
    {
        _filePath = Path.Combine(dataDirectory, "printer_models.csv");
    }

    /// <summary>
    /// Walks up from the running executable looking for a "data" directory, mirroring
    /// Program.cs's baseDir resolution — used by callers (like SettingsForm) that don't already
    /// have a CartonAppConfig.DataDirectory in scope.
    /// </summary>
    public static string FindDataDirectory()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var candidate = Path.Combine(current, "data");
            if (Directory.Exists(candidate))
                return candidate;

            var parent = Directory.GetParent(current);
            if (parent == null)
                break;

            current = parent.FullName;
        }
        return Path.Combine(AppContext.BaseDirectory, "data");
    }

    /// <summary>Model names in catalog order, for populating the Settings dropdown.</summary>
    public IReadOnlyList<string> GetModelNames()
    {
        var names = new List<string>();
        if (!File.Exists(_filePath))
            return names;

        foreach (var line in File.ReadAllLines(_filePath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith("ModelName", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = line.Split(',');
            if (parts.Length >= 1 && !string.IsNullOrWhiteSpace(parts[0]))
                names.Add(parts[0].Trim());
        }
        return names;
    }

    /// <summary>Resolves a model name to its engine hand. Defaults to Right (the more common,
    /// already-validated configuration) if the model isn't found — never blocks a print.</summary>
    public EngineHand GetHand(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName) || !File.Exists(_filePath))
            return EngineHand.Right;

        var term = modelName.Trim();
        foreach (var line in File.ReadAllLines(_filePath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith("ModelName", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = line.Split(',');
            if (parts.Length < 2 || !string.Equals(parts[0].Trim(), term, StringComparison.OrdinalIgnoreCase))
                continue;

            return Enum.TryParse<EngineHand>(parts[1].Trim(), ignoreCase: true, out var hand) ? hand : EngineHand.Right;
        }
        return EngineHand.Right;
    }
}
