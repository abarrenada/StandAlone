using System.Text;

namespace StandAlone.CartonUi.Services;

/// <summary>
/// Persistent TCP monitor for the stacker PLC through a Moxa NPort, which forwards the PLC's
/// output as plain ASCII lines (connection, line splitting and raw logging live in
/// <see cref="NPortAsciiLineClient"/>). Each line is interpreted the same way dtplc066.p's
/// SeparateDataString does: the first two characters are the stacker number.
/// </summary>
public sealed class OmronNPortMonitorService
{
    private readonly NPortAsciiLineClient _client;

    public OmronNPortMonitorService(string host, int port, string logPath)
    {
        _client = new NPortAsciiLineClient(host, port, logPath, "monitor");
        _client.OnStatus = msg => OnStatus?.Invoke($"PLC monitor: {msg}");
        _client.OnLine = HandleLine;
    }

    public Action<string>? OnStatus;
    public Action<PlcDecodedFrame>? OnFrame;

    public Task RunAsync(CancellationToken ct) => _client.RunAsync(ct);

    private void HandleLine(string raw, string reason)
    {
        var bytes = Encoding.ASCII.GetBytes(raw);
        var decoded = DecodePlcLine(raw);
        _client.Log($"[{NPortAsciiLineClient.Now()}] line reason={reason} ascii=[{NPortAsciiLineClient.ToPrintableAscii(bytes)}] decoded={decoded}");

        // Blank lines are the PLC's keep-alive; dtplc066.p skips them silently too.
        if (raw.Trim().Length == 0)
            return;

        try
        {
            OnFrame?.Invoke(new PlcDecodedFrame(DateTime.Now, BitConverter.ToString(bytes), raw.Trim(), decoded));
        }
        catch
        {
            // best-effort callback only
        }
    }

    /// <summary>
    /// Mirrors dtplc066.p SeparateDataString. Returns the stacker number (1-12) as a plain
    /// integer string only for a "new box drop" — the one case that should record a box and
    /// print. Every other message comes back as a non-numeric description so callers ignore it.
    /// </summary>
    public static string DecodePlcLine(string line)
    {
        var instr = line.Trim();
        if (instr.Length == 0)
            return "blank";

        var stackText = instr.Length >= 2 ? instr[..2] : instr;
        if (!int.TryParse(stackText, out var stacker))
            return $"error(stack# bad:{stackText})";

        if (stacker < 1 || stacker > 12)
        {
            return stacker switch
            {
                0  => "error(stacker 00)",
                99 => "stop(99)",
                50 => "rebuild(50)",
                _  => $"error(stack# bad:{stackText})",
            };
        }

        // The real system requires exactly 2 characters ("01"); a single digit is accepted
        // here too because the PLC emulation used on the bench sends unpadded numbers.
        if (instr.Length <= 2)
            return stacker.ToString();

        if (instr.Length >= 4 && instr.Substring(2, 2) == ",Y")
            return $"reprint({stacker})";

        return "error(PLC message length)";
    }

    public static bool TryParseEndpoint(string input, out string host, out int port)
    {
        host = string.Empty;
        port = 4001;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        var value = input.Trim();

        if (value.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
            value = value[6..];

        var idx = value.LastIndexOf(':');
        if (idx > 0 && idx < value.Length - 1)
        {
            host = value[..idx].Trim();
            if (!int.TryParse(value[(idx + 1)..].Trim(), out port))
                return false;
            return !string.IsNullOrWhiteSpace(host) && port >= 1 && port <= 65535;
        }

        host = value;
        return !string.IsNullOrWhiteSpace(host);
    }
}

/// <param name="HexFrame">Hex dump of the raw line bytes, for diagnostics.</param>
/// <param name="Signature">The trimmed ASCII line as received.</param>
/// <param name="DecodedValue">Stacker number for a box drop, otherwise a description.</param>
public sealed record PlcDecodedFrame(DateTime Timestamp, string HexFrame, string Signature, string DecodedValue);
