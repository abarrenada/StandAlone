using LabelDecisionApp.CartonUi.Models;

namespace LabelDecisionApp.CartonUi.Services;

/// <summary>
/// Loads <see cref="CartonAppConfig"/> from flag/config files in the application directory,
/// mirroring the startup logic of dtlbl067.p.
///
/// Files are searched first in <paramref name="baseDirectory"/>, then in
/// <c>baseDirectory/data/</c> (same order as the Progress <c>search()</c> function).
/// </summary>
public static class CartonConfigLoader
{
    public static CartonAppConfig Load(string baseDirectory)
    {
        var config = new CartonAppConfig
        {
            DataDirectory = Path.Combine(baseDirectory, "data"),
        };

        // Line number from "LineNumber" flag file
        var lineNumFile = FindFile(baseDirectory, "LineNumber");
        if (lineNumFile is not null &&
            int.TryParse(File.ReadAllText(lineNumFile).Trim(), out var ln))
            config.LineNumber = ln;

        // MEXICO-ONLY flag
        config.DoesMexico = FindFile(baseDirectory, "MEXICO-ONLY") is not null;

        // 4DIGITSHADE flag
        config.W4DigitShade = FindFile(baseDirectory, "4DIGITSHADE") is not null;

        // DoesManStk — file exists AND line number appears in it
        var manStkFile = FindFile(baseDirectory, "DoesManStk");
        if (manStkFile is not null)
        {
            foreach (var line in File.ReadAllLines(manStkFile))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('#') || trimmed.StartsWith('.')) continue;
                if (int.TryParse(trimmed, out var lineNum) && lineNum == config.LineNumber)
                {
                    config.DoesManStk = true;
                    break;
                }
            }
        }

        // DoesTwoPrims — just the presence of the flag file is enough
        config.DoesTwoPrims = FindFile(baseDirectory, "DoesTwoPrims") is not null;

        // Label sizes from LabelSzPrmpt
        LoadLabelSizes(baseDirectory, config);

        return config;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static void LoadLabelSizes(string baseDirectory, CartonAppConfig config)
    {
        var filePath = FindFile(baseDirectory, "LabelSzPrmpt");
        if (filePath is null) return;

        var hostName = GetHostName(baseDirectory);
        var allLines = File.ReadAllLines(filePath);

        bool inSection = false;
        var sizes = new List<LabelSizeOption>();
        var validCodes = new List<string>();

        foreach (var raw in allLines)
        {
            var line = raw.TrimEnd();
            if (line.StartsWith('#')) continue;

            if (!inSection)
            {
                // The Progress code checks: if index(tSizes, HostName) > 0
                if (hostName is not null &&
                    line.Contains(hostName, StringComparison.OrdinalIgnoreCase))
                    inSection = true;
                continue;
            }

            // Blank line or new host marker ends the current section
            if (string.IsNullOrWhiteSpace(line) || (!line.StartsWith(' ') && !line.StartsWith('\t') && line.Length > 0 && !char.IsWhiteSpace(line[0])))
                break;

            // Tab- or space-delimited: SizeCode  Prompt  InclUs  InclMx
            var parts = line.Split(new[] { '\t', ' ' }, 4, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) break;

            var opt = new LabelSizeOption
            {
                SizeCode      = parts[0],
                Prompt        = parts[1],
                IncludeUs     = parts[2].Equals("Y", StringComparison.OrdinalIgnoreCase),
                IncludeMexico = parts[3].Equals("Y", StringComparison.OrdinalIgnoreCase),
            };
            sizes.Add(opt);
            validCodes.Add(opt.SizeCode);
            if (sizes.Count >= 10) break; // MaxSizes = 10
        }

        config.LabelSizes = sizes;
        config.ValidLabelSizes = sizes.Count > 0
            ? "," + string.Join(",", validCodes) + ","
            : string.Empty;
    }

    private static string? GetHostName(string baseDirectory)
    {
        var hostFile = FindFile(baseDirectory, "HOSTNAME");
        if (hostFile is null) return null;
        var content = File.ReadAllText(hostFile).Trim();
        return string.IsNullOrEmpty(content) ? null : content;
    }

    /// <summary>
    /// Searches for <paramref name="name"/> in <paramref name="baseDir"/> then in
    /// <c>baseDir/data/</c> — mirroring Progress <c>search()</c>.
    /// </summary>
    private static string? FindFile(string baseDir, string name)
    {
        var direct = Path.Combine(baseDir, name);
        if (File.Exists(direct)) return direct;
        var inData = Path.Combine(baseDir, "data", name);
        if (File.Exists(inData)) return inData;
        return null;
    }
}
