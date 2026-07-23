namespace LabelDecisionApp.Console.Services;

public interface ILabelDecisionService
{
    List<ItemLabelDecision> ReadInputCsv(string csvPath);
    List<ItemRunningEntry> ReadItemRunningCsv(string csvPath);
    void WriteDecisionCsv(IEnumerable<ItemLabelDecision> decisions, string outputPath);
}
