using System.Text.RegularExpressions;

namespace StandAlone.Integration.Services;

public static class SatoTemplateResolver
{
    public static string? ResolveTemplateFileName(ThermalLabelPayload payload)
    {
        // Pallet labels have no dedicated .sato template (only carton-sized ones exist
        // in sato_templates/), and the pallet-print screen shares its size combo with
        // the carton screen. Returning null here forces the caller to fall back to
        // ThermalPrinterCommandBuilder.Build(), which renders the correct hand-built
        // pallet layout instead of coincidentally matching a carton template by size.
        if (string.Equals(payload.LabelFormat, "PALLET_LABEL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var sizeToken = NormalizeSizeToken(payload.LabelSize);
        var referenceToken = payload.TemplateOrientation switch
        {
            "ref-last" => "ref-last",
            _ => "ref-first"
        };

        if (payload.IsMexicoItem)
        {
            return $"lt_mexico_{sizeToken}.sato";
        }

        if (payload.LabelTypeCode == 4)
        {
            return $"lt04_default_{sizeToken}.sato";
        }

        if (payload.LabelTypeCode == 6)
        {
            return $"lt06_xover_{sizeToken}_{referenceToken}.sato";
        }

        return $"lt_default_{sizeToken}.sato";
    }

    public static string FindTemplateDirectory()
    {
        var current = AppContext.BaseDirectory;

        while (!string.IsNullOrWhiteSpace(current))
        {
            var candidate = Path.Combine(current, "sato_templates");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }

            current = parent.FullName;
        }

        var workspaceFallback = Path.Combine("c:\\workspace", "southalr", "sato_templates");
        if (Directory.Exists(workspaceFallback))
        {
            return workspaceFallback;
        }

        return Path.Combine(AppContext.BaseDirectory, "sato_templates");
    }

    private static string NormalizeSizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "default";
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "4.5x3" or "45x3" or "4.5 x 3" => "45x3",
            "3x4.5" or "3x45" or "3 x 4.5" => "3x45",
            "1.75x8.38" or "175x838" or "1.75 x 8.38" => "175x838",
            "2x7.25" or "2x725" or "2 x 7.25" => "2x725",
            "2x6" or "2 x 6" => "2x6",
            _ => normalized.Replace(" ", string.Empty).Replace(".", string.Empty)
        };
    }
}

public static class SatoTemplateRenderer
{
    private static readonly Regex PlaceholderRegex = new(@"\{(?<name>[A-Za-z0-9_]+)\}", RegexOptions.Compiled);

    public static string RenderTemplate(string template, ThermalLabelPayload payload)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return string.Empty;
        }

        var matches = PlaceholderRegex.Matches(template);
        if (matches.Count == 0)
        {
            return template;
        }

        var text = template;
        foreach (Match match in matches)
        {
            var placeholder = match.Groups["name"].Value;
            var propertyInfo = typeof(ThermalLabelPayload).GetProperty(placeholder);
            if (propertyInfo == null)
            {
                continue;
            }

            var value = propertyInfo.GetValue(payload)?.ToString() ?? string.Empty;
            text = text.Replace(match.Value, value, StringComparison.Ordinal);
        }

        return text;
    }
}
