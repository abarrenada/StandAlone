using LabelDecisionApp.Integration.Models;

namespace LabelDecisionApp.Integration.Services;

public interface IInputAdapter
{
    Task<InputEnvelope?> ReadNextAsync(CancellationToken cancellationToken);
}
