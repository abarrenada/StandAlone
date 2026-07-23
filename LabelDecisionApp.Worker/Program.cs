using LabelDecisionApp.Console.Services;
using LabelDecisionApp.Integration.Services;
using LabelDecisionApp.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddScoped<ILabelDecisionService, LabelDecisionService>();
builder.Services.AddSingleton<IPlcPayloadParser, PlcPayloadParser>();
builder.Services.AddSingleton<ILabelPrinter>(sp =>
    new FileLabelPrinter(Path.Combine(AppContext.BaseDirectory, "output"))
);
builder.Services.AddSingleton<ISapIntegrationService>(sp =>
    new FileSapIntegrationService(Path.Combine(AppContext.BaseDirectory, "sap-output"))
);

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
