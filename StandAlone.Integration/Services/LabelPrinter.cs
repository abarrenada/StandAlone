namespace StandAlone.Integration.Services;

public class LabelOutput
{
    public string ItemNumber { get; set; } = string.Empty;
    public int Plant { get; set; }
    public string LabelFormat { get; set; } = string.Empty;
    public string PrinterName { get; set; } = string.Empty;
    public DateTimeOffset PrintedAt { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public interface ILabelPrinter
{
    Task<LabelOutput> PrintAsync(string itemNumber, int plant, string labelFormat, CancellationToken cancellationToken, string? serialNumber = null);
}

public class FileLabelPrinter : ILabelPrinter
{
    private readonly string _outputDirectory;

    public FileLabelPrinter(string outputDirectory)
    {
        _outputDirectory = outputDirectory;
        Directory.CreateDirectory(outputDirectory);
    }

    public Task<LabelOutput> PrintAsync(string itemNumber, int plant, string labelFormat, CancellationToken cancellationToken, string? serialNumber = null)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        var safeLabelFormat = labelFormat.Replace(" ", "_").ToUpperInvariant();
        var safeItem = SanitizeFileToken(itemNumber);
        var safeSerial = SanitizeFileToken(serialNumber);
        var fileName = string.IsNullOrWhiteSpace(safeSerial)
            ? $"label_{safeLabelFormat}_{safeItem}_{plant}_{timestamp}.xml"
            : $"label_{safeLabelFormat}_{safeItem}_{plant}_{safeSerial}_{timestamp}.xml";
        var filePath = Path.Combine(_outputDirectory, fileName);

        var content = GenerateLabelXml(labelFormat, itemNumber, plant, serialNumber);

        try
        {
            File.WriteAllText(filePath, content);
            return Task.FromResult(new LabelOutput
            {
                ItemNumber = itemNumber,
                Plant = plant,
                LabelFormat = labelFormat,
                PrinterName = "file",
                PrintedAt = DateTimeOffset.UtcNow,
                Success = true
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new LabelOutput
            {
                ItemNumber = itemNumber,
                Plant = plant,
                LabelFormat = labelFormat,
                PrinterName = "file",
                PrintedAt = DateTimeOffset.UtcNow,
                Success = false,
                ErrorMessage = ex.Message
            });
        }
    }

    private static string GenerateLabelXml(string labelFormat, string itemNumber, int plant, string? serialNumber)
    {
        var printedAt = DateTimeOffset.UtcNow;
        var escapedFormat = System.Security.SecurityElement.Escape(labelFormat);
        var escapedItem = System.Security.SecurityElement.Escape(itemNumber);
        var escapedSerial = System.Security.SecurityElement.Escape(serialNumber ?? string.Empty);

        if (labelFormat.StartsWith("PALLET_LABEL", StringComparison.OrdinalIgnoreCase))
        {
            var palletId = string.IsNullOrWhiteSpace(escapedSerial)
                ? $"PALLET-{plant}-{printedAt:yyyyMMddHHmmss}"
                : escapedSerial;

            return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<PalletLabelDocument>
  <LabelFormat>{escapedFormat}</LabelFormat>
  <PalletId>{palletId}</PalletId>
  <ItemNumber>{escapedItem}</ItemNumber>
  <Plant>{plant}</Plant>
  <SerialNumber>{escapedSerial}</SerialNumber>
  <PrintedAt>{printedAt:O}</PrintedAt>
</PalletLabelDocument>";
        }

        var serialSection = string.IsNullOrWhiteSpace(escapedSerial)
            ? string.Empty
            : $"  <SerialNumber>{escapedSerial}</SerialNumber>\n";

        return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<LabelDocument>
  <LabelFormat>{escapedFormat}</LabelFormat>
  <ItemNumber>{escapedItem}</ItemNumber>
  <Plant>{plant}</Plant>
{serialSection}  <PrintedAt>{printedAt:O}</PrintedAt>
</LabelDocument>";
    }

    private static string SanitizeFileToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }
}
