namespace LabelDecisionApp.Integration.Models;

public class InputEnvelope
{
    public string Source { get; set; } = "file";
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
}
