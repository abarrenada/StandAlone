using StandAlone.CartonUi;
using StandAlone.CartonUi.Forms;
using StandAlone.CartonUi.Services;
using System.Windows.Forms;
using StandAlone.CartonUi.Models;

// Find the workspace root by looking for the "data" directory
// This allows flag files in the workspace root to be found when running from bin/Debug
var baseDir = AppContext.BaseDirectory;
while (!string.IsNullOrEmpty(baseDir) && baseDir != Path.GetPathRoot(baseDir))
{
    if (Directory.Exists(Path.Combine(baseDir, "data")))
    {
        break;
    }
    baseDir = Path.GetDirectoryName(baseDir)!;
}

// Initialize settings manager with the correct base directory
SettingsManager.Initialize(baseDir);

Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);

if (args.Contains("--pallet"))
{
    var settings = SettingsManager.Load();
    var config   = CartonConfigLoader.Load(baseDir);
    var boxRepo  = new FileBoxRepository(config.DataDirectory, settings.PalletsCsvPath);

    using var dlg = new PalletStartupDialog(settings);
    if (dlg.ShowDialog() != DialogResult.OK) return;

    Application.Run(new PalletScanForm(settings, config, boxRepo, dlg.Shift, dlg.Inspector));
}
else if (args.Contains("--eol"))
{
    var settings = SettingsManager.Load();
    var config   = CartonConfigLoader.Load(baseDir);
    var boxRepo  = new FileBoxRepository(config.DataDirectory, settings.PalletsCsvPath);

    using var dlg = new PalletStartupDialog(settings, "EOL Scan — Start Session");
    if (dlg.ShowDialog() != DialogResult.OK) return;

    Application.Run(new EolScanForm(settings, config, boxRepo, dlg.Shift, dlg.Inspector));
}
else if (args.Contains("--settings"))
    Application.Run(new SettingsForm());
else
    Application.Run(new MainForm(baseDir));
