using System.IO.Ports;
using LabelDecisionApp.Integration.Models;

namespace LabelDecisionApp.Integration.Services;

public class SerialInputAdapter : IInputAdapter
{
    private readonly string _portName;
    private readonly int _baudRate;
    private SerialPort? _port;

    public SerialInputAdapter(string portName, int baudRate = 9600)
    {
        _portName = portName;
        _baudRate = baudRate;
    }

    public Task<InputEnvelope?> ReadNextAsync(CancellationToken cancellationToken)
    {
        if (_port is null)
        {
            _port = new SerialPort(_portName, _baudRate)
            {
                ReadTimeout = 1000,
                WriteTimeout = 1000
            };
            _port.Open();
        }

        try
        {
            var payload = _port.ReadLine();
            return Task.FromResult<InputEnvelope?>(new InputEnvelope
            {
                Source = "serial",
                Payload = payload,
                ReceivedAt = DateTimeOffset.UtcNow
            });
        }
        catch (TimeoutException)
        {
            return Task.FromResult<InputEnvelope?>(null);
        }
    }
}
