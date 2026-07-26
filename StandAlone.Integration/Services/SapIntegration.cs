namespace StandAlone.Integration.Services;

public class PalletIntegrationPayload
{
    public string SerialNumber { get; set; } = string.Empty;
    public string ItemNumber { get; set; } = string.Empty;
    public int Plant { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; }
}

public class SapIntegrationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public interface ISapIntegrationService
{
    Task<SapIntegrationResult> SendPalletIntegrationAsync(PalletIntegrationPayload payload, CancellationToken cancellationToken);
}

public class FileSapIntegrationService : ISapIntegrationService
{
    private readonly string _outputDirectory;

    public FileSapIntegrationService(string outputDirectory)
    {
        _outputDirectory = outputDirectory;
        Directory.CreateDirectory(outputDirectory);
    }

    public Task<SapIntegrationResult> SendPalletIntegrationAsync(PalletIntegrationPayload payload, CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        var fileName = $"sap_pallet_integration_{payload.SerialNumber}_{timestamp}.xml";
        var filePath = Path.Combine(_outputDirectory, fileName);

        var content = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<PalletIntegration>
  <SerialNumber>{System.Security.SecurityElement.Escape(payload.SerialNumber)}</SerialNumber>
  <ItemNumber>{System.Security.SecurityElement.Escape(payload.ItemNumber)}</ItemNumber>
  <Plant>{payload.Plant}</Plant>
  <Source>{System.Security.SecurityElement.Escape(payload.Source)}</Source>
  <ReceivedAt>{payload.ReceivedAt:O}</ReceivedAt>
</PalletIntegration>";

        try
        {
            File.WriteAllText(filePath, content);
            return Task.FromResult(new SapIntegrationResult { Success = true });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new SapIntegrationResult { Success = false, ErrorMessage = ex.Message });
        }
    }
}
