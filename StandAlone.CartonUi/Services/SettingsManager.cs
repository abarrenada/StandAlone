using StandAlone.CartonUi.Models;
using System.Text.Json;

namespace StandAlone.CartonUi.Services;

/// <summary>
/// Manages persistent application settings stored in JSON.
/// </summary>
public static class SettingsManager
{
    private static string _configPath = Path.Combine(AppContext.BaseDirectory, "carton-settings.json");

    /// <summary>Initialize SettingsManager with the correct base directory.</summary>
    public static void Initialize(string baseDirectory)
    {
        _configPath = Path.Combine(baseDirectory, "carton-settings.json");
    }

    public static AppSettings Load()
    {
        if (File.Exists(_configPath))
        {
            try
            {
                var json = File.ReadAllText(_configPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings is null)
                    return GetDefaults();

                settings.PlcConnectionType = NormalizePlcConnectionType(settings.PlcConnectionType);
                settings.LabelOutputType = NormalizeLabelOutputType(settings.LabelOutputType);
                settings.CartonPrintMode = NormalizeCartonPrintMode(settings.CartonPrintMode);
                settings.ThermalPrinterType = NormalizeThermalPrinterType(settings.ThermalPrinterType);
                return settings;
            }
            catch { /* parsing error, return defaults */ }
        }
        return GetDefaults();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            System.Windows.Forms.MessageBox.Show($"Failed to save settings: {ex.Message}", "Save Error");
        }
    }

    /// <summary>
    /// Reloads the settings file fresh, applies <paramref name="apply"/>, and saves the result.
    /// Use this instead of Save(_settings) when only touching a couple of fields (e.g. "remember
    /// last item"), so it doesn't clobber other fields a separate Settings session may have
    /// changed since this process's in-memory settings were loaded.
    /// </summary>
    public static AppSettings SaveField(Action<AppSettings> apply)
    {
        var current = Load();
        apply(current);
        Save(current);
        return current;
    }

    private static AppSettings GetDefaults()
    {
        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        return new AppSettings
        {
            BoxesCsvPath = Path.Combine(dataDir, "boxes01.csv"),
            StackersCsvPath = Path.Combine(dataDir, "stackers01.csv"),
            ItemdetCsvPath = Path.Combine(dataDir, "itemdet.csv"),
            MitemdetCsvPath = Path.Combine(dataDir, "mitemdet.csv"),
            ProductionLineNumber = 1,
            PlcConnectionType = "SerialPort",
            PlcAddress = "COM1",
            PlcBaudRate = 9600,
            LabelOutputType = "NiceLabel Xml",
            ThermalPrinterType = "SATO",
            CartonPrintMode = "PLC Signal",
            MasterPasswordHash = AppSettings.HashPassword("admin123"), // Default: "admin123"
        };
    }

    private static string NormalizeLabelOutputType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "NiceLabel Xml";

        return value.Trim().ToLowerInvariant() switch
        {
            "nicelabel xml" or "nicelabelxml" => "NiceLabel Xml",
            "serialport" => "SerialPort",
            "networkprinter" or "network printer" => "NetworkPrinter",
            "httppost" or "http post" => "HttpPost",
            _ => value,
        };
    }

    private static string NormalizePlcConnectionType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "SerialPort";

        return value.Trim().ToLowerInvariant() switch
        {
            "ip" => "IP",
            "serialport" or "serial" => "SerialPort",
            _ => "SerialPort",
        };
    }

    private static string NormalizeCartonPrintMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "PLC Signal";

        return value.Trim().ToLowerInvariant() switch
        {
            "plc" or "plc signal" or "signals" => "PLC Signal",
            "manual" or "manual qty" or "manual quantity" => "Manual Qty",
            _ => value,
        };
    }

    private static string NormalizeThermalPrinterType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "SATO";

        return value.Trim().ToLowerInvariant() switch
        {
            "sato" => "SATO",
            "ipl" => "IPL",
            "zpl" => "ZPL",
            "fingerprint" => "Fingerprint",
            _ => value,
        };
    }
}
