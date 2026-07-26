using System.Text;

namespace StandAlone.Console.Services;

public class ItemLabelDecision
{
    public string ItemNumber { get; set; } = string.Empty;
    public int Plant { get; set; }
    public int LabelTypeCode { get; set; }
    public string LabelFormat { get; set; } = string.Empty;
}

public class ItemRunningEntry
{
    public string SerialNumber { get; set; } = string.Empty;
    public string ItemNumber { get; set; } = string.Empty;
    public int Plant { get; set; }
}

public class LabelDecisionService : ILabelDecisionService
{
    public string ResolveLabelType(int labelTypeCode)
    {
        return labelTypeCode switch
        {
            1 => "SLAB_LABEL",
            2 => "PALLET_LABEL",
            3 => "FINISHED_GOOD_LABEL",
            4 => "WIP_LABEL",
            _ => "UNKNOWN"
        };
    }

    public List<ItemLabelDecision> ReadInputCsv(string csvPath)
    {
        if (!File.Exists(csvPath))
        {
            throw new FileNotFoundException($"CSV file not found: {csvPath}");
        }

        var decisions = new List<ItemLabelDecision>();
        var lines = File.ReadAllLines(csvPath);

        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var values = ParseCsvLine(line);
            if (values.Count < 3)
            {
                continue;
            }

            var itemNumber = values[0].Trim().Trim('"');
            var plant = int.TryParse(values[1].Trim(), out var parsedPlant) ? parsedPlant : 0;
            var labelTypeCode = int.TryParse(values[2].Trim(), out var parsedCode) ? parsedCode : 0;

            decisions.Add(new ItemLabelDecision
            {
                ItemNumber = itemNumber,
                Plant = plant,
                LabelTypeCode = labelTypeCode,
                LabelFormat = ResolveLabelType(labelTypeCode)
            });
        }

        return decisions;
    }

    public List<ItemRunningEntry> ReadItemRunningCsv(string csvPath)
    {
        if (!File.Exists(csvPath))
        {
            throw new FileNotFoundException($"CSV file not found: {csvPath}");
        }

        var runningItems = new List<ItemRunningEntry>();
        var lines = File.ReadAllLines(csvPath);

        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var values = ParseCsvLine(line);
            if (values.Count < 3)
            {
                continue;
            }

            runningItems.Add(new ItemRunningEntry
            {
                SerialNumber = values[0].Trim().Trim('"'),
                ItemNumber = values[1].Trim().Trim('"'),
                Plant = int.TryParse(values[2].Trim(), out var plant) ? plant : 0
            });
        }

        return runningItems;
    }

    public void WriteDecisionCsv(IEnumerable<ItemLabelDecision> decisions, string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new StringBuilder();
        builder.AppendLine("item_nbr,plant,label_type_code,label_format");

        foreach (var decision in decisions)
        {
            builder.AppendLine($"{Escape(decision.ItemNumber)},{decision.Plant},{decision.LabelTypeCode},{decision.LabelFormat}");
        }

        File.WriteAllText(outputPath, builder.ToString());
    }

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        values.Add(current.ToString());
        return values;
    }

    private static string Escape(string value)
    {
        return string.IsNullOrEmpty(value)
            ? string.Empty
            : $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
