using StandAlone.CartonUi.Models;
using StandAlone.Integration.Services;

namespace StandAlone.CartonUi.Services;

/// <summary>
/// Builds this station's data-lake <see cref="StationInfo"/> snapshot. Called by every screen
/// that can be a station's entry point (MainForm, and Pallet Scan / EOL Scan when started with
/// --pallet / --eol), so each station shows up in the stations collection whatever it runs.
/// </summary>
public static class StationRegistration
{
    public const string CartonLabels = "Carton Labels";
    public const string PalletScan   = "Pallet Scan";
    public const string EolScan      = "EOL Scan";

    public static StationInfo Build(AppSettings settings, CartonAppConfig config, string role) => new()
    {
        PlantName          = settings.PlantName,
        StationId          = settings.StationId,
        LineNumber         = config.LineNumber,
        PlcConnectionType  = settings.PlcConnectionType,
        PlcAddress         = settings.PlcAddress,
        PlcBaudRate        = settings.PlcBaudRate,
        PlcPort            = settings.PlcPort,
        PrinterModel       = settings.PrinterModel,
        ThermalPrinterType = settings.ThermalPrinterType,
        LabelOutputType    = settings.LabelOutputType,
        BackflushVehicle   = settings.BackflushVehicle,
        CartonPrintMode    = settings.CartonPrintMode,
        PalletLocation     = settings.PalletLocation,
        Role               = role,
        LastHeartbeatUtc   = DateTime.UtcNow,
    };
}
