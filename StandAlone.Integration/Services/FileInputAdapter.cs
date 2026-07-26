using StandAlone.Integration.Models;

namespace StandAlone.Integration.Services;

public class FileInputAdapter : IInputAdapter
{
    private readonly string _directory;
    private readonly Queue<string> _queue = new();

    public FileInputAdapter(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public Task<InputEnvelope?> ReadNextAsync(CancellationToken cancellationToken)
    {
        if (_queue.Count == 0)
        {
            foreach (var file in Directory.GetFiles(_directory).OrderBy(f => f))
            {
                _queue.Enqueue(file);
            }
        }

        if (_queue.Count == 0)
        {
            return Task.FromResult<InputEnvelope?>(null);
        }

        var filePath = _queue.Dequeue();
        var payload = File.ReadAllText(filePath);
        var envelope = new InputEnvelope
        {
            Source = "file",
            Payload = payload,
            ReceivedAt = DateTimeOffset.UtcNow
        };

        File.Delete(filePath);
        return Task.FromResult<InputEnvelope?>(envelope);
    }
}
