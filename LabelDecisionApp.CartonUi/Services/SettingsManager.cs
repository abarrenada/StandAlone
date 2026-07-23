using LabelDecisionApp.CartonUi.Models;
using System.Text.Json;

namespace LabelDecisionApp.CartonUi.Services;

/// <summary>
/// Manages persistent application settings stored in JSON.
/// </summary>
public static class SettingsManager
{
    private static string _configPath = Path.Combine(AppContext.BaseDirectory, "carton-settings.json");

    public static AppSettings Load()
    {
        if (File.Exists(_configPath))
        {
            try
            {
                var json = File.ReadAllText(_configPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                return settings ?? GetDefaults();
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
            LabelOutputType = "NicelabelXml",
            MasterPasswordHash = AppSettings.HashPassword("admin123"), // Default: "admin123"
        };
    }
}
