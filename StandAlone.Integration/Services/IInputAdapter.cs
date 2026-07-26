using StandAlone.Integration.Models;

namespace StandAlone.Integration.Services;

public interface IInputAdapter
{
    Task<InputEnvelope?> ReadNextAsync(CancellationToken cancellationToken);
}
