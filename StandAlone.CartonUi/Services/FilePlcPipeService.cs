namespace StandAlone.CartonUi.Services;

/// <summary>
/// Writes PLC pipe messages by appending padded lines to the pipe file,
/// mirroring the Progress <c>unix silent echo '{msg}' >> ../data/plc##</c> pattern.
/// </summary>
public class FilePlcPipeService : IPlcPipeService
{
    private const int PipeWidth = 65;

    public void SendStartup(int shift, string inspector, string labelSize, string pipePath)
    {
        // Progress: p-plc-info = "50, ," + string(prt-shift,"9") + "," + prt-inspector + "," + prt-label-size
        var inspectorPadded = inspector.Length < 2 ? inspector.PadRight(2) : inspector;
        var message = $"50, ,{shift},{inspectorPadded},{labelSize}";
        AppendToPipe(pipePath, message);
    }

    public void SendReprint(string stackNum, string pipePath)
    {
        // Progress: p-plc-info = box.bx-stacknum + ",Y, ,  ,"
        var message = $"{stackNum},Y, ,  ,";
        AppendToPipe(pipePath, message);
    }

    public void SendReset(string matchFilePath)
    {
        // Progress: echo RESET >> ../data/VfyScn/Match##.txt
        var message = "RESET".PadRight(PipeWidth);
        EnsureDirectory(matchFilePath);
        File.AppendAllText(matchFilePath, message + Environment.NewLine);
    }

    private static void AppendToPipe(string pipePath, string message)
    {
        var padded = message.Length >= PipeWidth
            ? message[..PipeWidth]
            : message.PadRight(PipeWidth);
        EnsureDirectory(pipePath);
        File.AppendAllText(pipePath, padded + Environment.NewLine);
    }

    private static void EnsureDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }
}
