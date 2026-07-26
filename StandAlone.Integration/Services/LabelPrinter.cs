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
        var fileName = $"label_{safeLabelFormat}_{itemNumber}_{plant}_{timestamp}.xml";
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
        var serialSection = string.IsNullOrWhiteSpace(serialNumber)
            ? string.Empty
            : $"  <SerialNumber>{System.Security.SecurityElement.Escape(serialNumber)}</SerialNumber>\n";

        return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<LabelDocument>
  <LabelFormat>{System.Security.SecurityElement.Escape(labelFormat)}</LabelFormat>
  <ItemNumber>{System.Security.SecurityElement.Escape(itemNumber)}</ItemNumber>
  <Plant>{plant}</Plant>
{serialSection}  <PrintedAt>{printedAt:O}</PrintedAt>
</LabelDocument>";
    }
}
