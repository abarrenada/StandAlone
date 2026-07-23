namespace LabelDecisionApp.CartonUi.Services;

/// <summary>
/// Abstraction over writing commands to the PLC pipe file.
/// Equivalent to the Progress <c>unix silent echo … >> ../data/plc##</c> pattern.
/// </summary>
public interface IPlcPipeService
{
    /// <summary>
    /// Sends startup information to the PLC pipe.
    /// Progress: <c>"50, ," + shift + "," + inspector + "," + labelSize</c>
    /// </summary>
    void SendStartup(int shift, string inspector, string labelSize, string pipePath);

    /// <summary>
    /// Sends a reprint request for the given stack number.
    /// Progress: <c>stackNum + ",Y, ,  ,"</c>
    /// </summary>
    void SendReprint(string stackNum, string pipePath);

    /// <summary>
    /// Sends a reset command to the Match file.
    /// Progress: <c>echo RESET >> ../data/VfyScn/Match##.txt</c>
    /// </summary>
    void SendReset(string matchFilePath);
}
