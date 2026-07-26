using StandAlone.Console.Services;
using StandAlone.Integration.Services;

var service = new LabelDecisionService();

var inputPath = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "sample-item-label-types.csv");
var outputPath = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "label-decisions.csv");
var mode = args.Length > 2 ? args[2] : "csv";

if (mode.Equals("serial", StringComparison.OrdinalIgnoreCase))
{
    var portName = args.Length > 3 ? args[3] : "COM1";
    var adapter = new SerialInputAdapter(portName);
    var envelope = await adapter.ReadNextAsync(CancellationToken.None);
    Console.WriteLine($"Serial input received: {envelope?.Payload ?? "<none>"}");
    Environment.Exit(0);
}

if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"Input CSV not found: {inputPath}");
    Environment.ExitCode = 1;
    return;
}

var decisions = service.ReadInputCsv(inputPath);
service.WriteDecisionCsv(decisions, outputPath);

Console.WriteLine($"Processed {decisions.Count} rows from {inputPath}");
Console.WriteLine($"Wrote decisions to {outputPath}");
