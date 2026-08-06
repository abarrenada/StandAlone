using StandAlone.CartonUi;
using StandAlone.CartonUi.Forms;
using StandAlone.CartonUi.Services;
using System.Windows.Forms;

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

if (args.Contains("--settings"))
    Application.Run(new SettingsForm());
else
    Application.Run(new MainForm(baseDir));
