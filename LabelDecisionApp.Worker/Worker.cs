using LabelDecisionApp.Console.Services;
using LabelDecisionApp.Integration.Services;

namespace LabelDecisionApp.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ILabelDecisionService _labelService;
    private readonly IPlcPayloadParser _payloadParser;
    private readonly ILabelPrinter _printer;
    private readonly ISapIntegrationService _sapIntegration;
    private readonly IConfiguration _config;
    private readonly string _inputDirectory;
    private readonly string _csvPath;
    private readonly string _runningCsvPath;

    public Worker(ILogger<Worker> logger, ILabelDecisionService labelService, IPlcPayloadParser payloadParser, ILabelPrinter printer, ISapIntegrationService sapIntegration, IConfiguration config)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _labelService = labelService ?? throw new ArgumentNullException(nameof(labelService));
        _payloadParser = payloadParser ?? throw new ArgumentNullException(nameof(payloadParser));
        _printer = printer ?? throw new ArgumentNullException(nameof(printer));
        _sapIntegration = sapIntegration ?? throw new ArgumentNullException(nameof(sapIntegration));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _inputDirectory = config["Input:Directory"] ?? Path.Combine(AppContext.BaseDirectory, "input");
        _csvPath = config["Input:CsvPath"] ?? Path.Combine(AppContext.BaseDirectory, "item-label-types.csv");
        _runningCsvPath = config["Input:RunningCsvPath"] ?? Path.Combine(AppContext.BaseDirectory, "item-running.csv");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Label Decision Worker starting at {time}", DateTimeOffset.Now);

        if (!File.Exists(_csvPath))
        {
            _logger.LogWarning("Label lookup CSV file not found at {path}. Worker will wait for it.", _csvPath);
        }

        if (!File.Exists(_runningCsvPath))
        {
            _logger.LogWarning("Item running CSV file not found at {path}. Worker will wait for it.", _runningCsvPath);
        }

        Directory.CreateDirectory(_inputDirectory);
        var watcher = new DirectoryFileWatcher();
        watcher.StartWatching(_inputDirectory, async (filePath) => await OnFileDetected(filePath, stoppingToken), stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(5000, stoppingToken);
            }
        }
        finally
        {
            watcher.StopWatching();
        }

        _logger.LogInformation("Label Decision Worker stopped at {time}", DateTimeOffset.Now);
    }

    private async Task OnFileDetected(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Processing file: {path}", filePath);

            var rawPayload = File.ReadAllText(filePath);
            var payload = _payloadParser.Parse(rawPayload);

            if (payload is null)
            {
                _logger.LogWarning("Could not parse payload from file: {path}", filePath);
                return;
            }

            _logger.LogInformation("Parsed payload: DeviceType={device}, Serial={serial}, ItemNumber={item}, Plant={plant}", payload.DeviceType, payload.SerialNumber, payload.ItemNumber, payload.Plant);

            var decisions = _labelService.ReadInputCsv(_csvPath);
            var runningItems = _labelService.ReadItemRunningCsv(_runningCsvPath);

            if (payload.DeviceType == "EOL")
            {
                await HandleEolScanAsync(payload, runningItems, cancellationToken);
                return;
            }

            if (payload.DeviceType == "SERIAL")
            {
                await HandlePalletScanAsync(payload, decisions, runningItems, cancellationToken);
                return;
            }

            if (!string.IsNullOrWhiteSpace(payload.ItemNumber) && payload.Plant > 0)
            {
                await HandleCartonPrintAsync(payload, decisions, cancellationToken);
                return;
            }

            _logger.LogWarning("Unsupported payload type or missing data for file: {path}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file: {path}", filePath);
        }
    }

    private async Task HandleCartonPrintAsync(PlcPayload payload, List<ItemLabelDecision> decisions, CancellationToken cancellationToken)
    {
        var decision = decisions.FirstOrDefault(d => string.Equals(d.ItemNumber, payload.ItemNumber, StringComparison.OrdinalIgnoreCase) && d.Plant == payload.Plant);

        if (decision is null)
        {
            _logger.LogWarning("No label decision found for Item={item}, Plant={plant}", payload.ItemNumber, payload.Plant);
            return;
        }

        var output = await _printer.PrintAsync(decision.ItemNumber, decision.Plant, decision.LabelFormat, cancellationToken, payload.SerialNumber);
        if (output.Success)
        {
            _logger.LogInformation("Carton label generated for item {item} with format {format}", decision.ItemNumber, decision.LabelFormat);
        }
        else
        {
            _logger.LogError("Carton label generation failed: {error}", output.ErrorMessage);
        }
    }

    private async Task HandlePalletScanAsync(PlcPayload payload, List<ItemLabelDecision> decisions, List<ItemRunningEntry> runningItems, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload.SerialNumber))
        {
            _logger.LogWarning("Pallet scan payload missing serial number.");
            return;
        }

        var matchingItem = runningItems.FirstOrDefault(r => string.Equals(r.SerialNumber, payload.SerialNumber, StringComparison.OrdinalIgnoreCase));
        if (matchingItem is null)
        {
            _logger.LogWarning("No running item found for serial {serial}", payload.SerialNumber);
            return;
        }

        var decision = decisions.FirstOrDefault(d => string.Equals(d.ItemNumber, matchingItem.ItemNumber, StringComparison.OrdinalIgnoreCase) && d.Plant == matchingItem.Plant)
            ?? new ItemLabelDecision
            {
                ItemNumber = matchingItem.ItemNumber,
                Plant = matchingItem.Plant,
                LabelTypeCode = 2,
                LabelFormat = "PALLET_LABEL"
            };

        if (!string.Equals(decision.LabelFormat, "PALLET_LABEL", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Running item {item} mapped to label format {format} but pallet scan expects PALLET_LABEL.", matchingItem.ItemNumber, decision.LabelFormat);
        }

        var output = await _printer.PrintAsync(matchingItem.ItemNumber, matchingItem.Plant, decision.LabelFormat, cancellationToken, matchingItem.SerialNumber);
        if (output.Success)
        {
            _logger.LogInformation("Pallet label generated for serial {serial}, item {item}", matchingItem.SerialNumber, matchingItem.ItemNumber);
        }
        else
        {
            _logger.LogError("Pallet label generation failed: {error}", output.ErrorMessage);
        }
    }

    private async Task HandleEolScanAsync(PlcPayload payload, List<ItemRunningEntry> runningItems, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload.SerialNumber))
        {
            _logger.LogWarning("EOL payload missing serial number.");
            return;
        }

        var matchingItem = runningItems.FirstOrDefault(r => string.Equals(r.SerialNumber, payload.SerialNumber, StringComparison.OrdinalIgnoreCase));
        if (matchingItem is null)
        {
            _logger.LogWarning("No running item found for EOL serial {serial}", payload.SerialNumber);
            return;
        }

        if (!string.IsNullOrWhiteSpace(payload.ItemNumber) && !string.Equals(payload.ItemNumber, matchingItem.ItemNumber, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("EOL scan item mismatch for serial {serial}: expected {expected}, actual {actual}", payload.SerialNumber, matchingItem.ItemNumber, payload.ItemNumber);
        }

        var integrationPayload = new PalletIntegrationPayload
        {
            SerialNumber = matchingItem.SerialNumber,
            ItemNumber = matchingItem.ItemNumber,
            Plant = matchingItem.Plant,
            Source = payload.DeviceType,
            ReceivedAt = DateTimeOffset.UtcNow
        };

        var result = await _sapIntegration.SendPalletIntegrationAsync(integrationPayload, cancellationToken);
        if (result.Success)
        {
            _logger.LogInformation("EOL pallet integration sent for serial {serial}", matchingItem.SerialNumber);
        }
        else
        {
            _logger.LogError("EOL pallet integration failed: {error}", result.ErrorMessage);
        }
    }
}
