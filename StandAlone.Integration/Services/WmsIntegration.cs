namespace StandAlone.Integration.Services;

public class WmsIntegrationPayload
{
    public string SerialNumber { get; set; } = string.Empty;
    public string ItemNumber { get; set; } = string.Empty;
    public int Plant { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; }

    // ── Warehouse-receipt fields ───────────────────────────────────────────────
    // Modeled on the legacy Progress pipeline: dtrcv001.p creates a wms-receipt row
    // (status "A") on receiving, which sendprod-orawms.p later picks up and pushes to
    // Oracle's manuf_interfaces package via a DataServer stored-procedure call
    // (p_insert_interface_receipt). This standalone system has no Oracle DataServer
    // connectivity, so this is a file-stub matching FileSapIntegrationService's
    // approach — same shape, WMS-facing fields instead of production-confirmation ones.
    //
    // Known, deliberate gaps (not oversights): the real pipeline also validates the
    // pallet's order/schedule is still open before receiving at all, and skips the WMS
    // leg entirely for "A-FRAME" material-type items (sapitemhdr.sih-rmatp) — both
    // require order/schedule and SAP-item-master data sources this port doesn't have
    // (same gap already documented for CrossOver/F&D's sapitemhdr-sourced fields), so
    // every pallet is always sent here.
    public string ColorDesc { get; set; } = string.Empty;
    public string ShapeDesc { get; set; } = string.Empty;
    public string SeriesDesc { get; set; } = string.Empty;
    public int Grade { get; set; }
    public string Location { get; set; } = string.Empty;
    public int BoxesPerPallet { get; set; }
    public int LisQty { get; set; }
    public int LineNumber { get; set; }
    public int Shift { get; set; }
    public string Inspector { get; set; } = string.Empty;
}

public class WmsIntegrationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public interface IWmsIntegrationService
{
    Task<WmsIntegrationResult> SendPalletIntegrationAsync(WmsIntegrationPayload payload, CancellationToken cancellationToken);
}

public class FileWmsIntegrationService : IWmsIntegrationService
{
    private readonly string _outputDirectory;

    public FileWmsIntegrationService(string outputDirectory)
    {
        _outputDirectory = outputDirectory;
        Directory.CreateDirectory(outputDirectory);
    }

    public Task<WmsIntegrationResult> SendPalletIntegrationAsync(WmsIntegrationPayload payload, CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        var fileName = $"wms_pallet_receipt_{payload.SerialNumber}_{timestamp}.xml";
        var filePath = Path.Combine(_outputDirectory, fileName);

        var content = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<WarehouseReceipt>
  <SerialNumber>{System.Security.SecurityElement.Escape(payload.SerialNumber)}</SerialNumber>
  <ItemNumber>{System.Security.SecurityElement.Escape(payload.ItemNumber)}</ItemNumber>
  <Plant>{payload.Plant}</Plant>
  <Source>{System.Security.SecurityElement.Escape(payload.Source)}</Source>
  <ReceivedAt>{payload.ReceivedAt:O}</ReceivedAt>
  <ColorDesc>{System.Security.SecurityElement.Escape(payload.ColorDesc)}</ColorDesc>
  <ShapeDesc>{System.Security.SecurityElement.Escape(payload.ShapeDesc)}</ShapeDesc>
  <SeriesDesc>{System.Security.SecurityElement.Escape(payload.SeriesDesc)}</SeriesDesc>
  <Grade>{payload.Grade}</Grade>
  <Location>{System.Security.SecurityElement.Escape(payload.Location)}</Location>
  <BoxesPerPallet>{payload.BoxesPerPallet}</BoxesPerPallet>
  <LisQty>{payload.LisQty}</LisQty>
  <LineNumber>{payload.LineNumber}</LineNumber>
  <Shift>{payload.Shift}</Shift>
  <Inspector>{System.Security.SecurityElement.Escape(payload.Inspector)}</Inspector>
</WarehouseReceipt>";

        try
        {
            File.WriteAllText(filePath, content);
            return Task.FromResult(new WmsIntegrationResult { Success = true });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new WmsIntegrationResult { Success = false, ErrorMessage = ex.Message });
        }
    }
}
