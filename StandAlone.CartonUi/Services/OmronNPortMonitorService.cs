using System.Net.Sockets;
using System.Text;

namespace StandAlone.CartonUi.Services;

/// <summary>
/// Persistent TCP monitor for Omron CP1E through Moxa NPort.
/// Captures raw chunks, reassembles fixed-size frames, and emits decoded diagnostics.
/// </summary>
public sealed class OmronNPortMonitorService
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _logPath;
    private readonly int _frameLength;

    public OmronNPortMonitorService(string host, int port, string logPath, int frameLength = 8)
    {
        _host = host;
        _port = port;
        _logPath = logPath;
        _frameLength = frameLength <= 0 ? 8 : frameLength;
    }

    public Action<string>? OnStatus;
    public Action<PlcDecodedFrame>? OnFrame;

    public async Task RunAsync(CancellationToken ct)
    {
        EnsureLogDirectory();
        Log($"[{Now()}] monitor_start host={_host} port={_port} frameLen={_frameLength}");

        while (!ct.IsCancellationRequested)
        {
            TcpClient? client = null;
            NetworkStream? stream = null;

            try
            {
                client = new TcpClient();
                OnStatus?.Invoke($"PLC monitor connecting {_host}:{_port}...");
                await client.ConnectAsync(_host, _port, ct);
                stream = client.GetStream();
                stream.ReadTimeout = 2000;

                OnStatus?.Invoke($"PLC monitor connected {_host}:{_port}");
                Log($"[{Now()}] connected");

                var chunkBuffer = new byte[1024];
                var pending = new List<byte>(_frameLength * 4);

                while (!ct.IsCancellationRequested && client.Connected)
                {
                    int read;
                    try
                    {
                        read = await stream.ReadAsync(chunkBuffer, 0, chunkBuffer.Length, ct);
                    }
                    catch (IOException)
                    {
                        // Keep connection alive through read timeouts and continue listening.
                        continue;
                    }

                    if (read <= 0)
                        break;

                    var chunk = chunkBuffer.AsSpan(0, read).ToArray();
                    pending.AddRange(chunk);
                    LogChunk(chunk, pending.Count);

                    while (pending.Count >= _frameLength)
                    {
                        var frame = pending.Take(_frameLength).ToArray();
                        pending.RemoveRange(0, _frameLength);
                        LogFrame(frame, pending.Count);
                    }
                }

                OnStatus?.Invoke("PLC monitor disconnected; retrying...");
                Log($"[{Now()}] disconnected");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                OnStatus?.Invoke($"PLC monitor error: {ex.Message}; retrying...");
                Log($"[{Now()}] error {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                try { stream?.Dispose(); } catch { }
                try { client?.Dispose(); } catch { }
            }

            try
            {
                await Task.Delay(2000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Log($"[{Now()}] monitor_stop");
    }

    private void LogChunk(byte[] chunk, int pendingBytes)
    {
        var hex = BitConverter.ToString(chunk);
        var ascii = ToPrintableAscii(chunk);
        Log($"[{Now()}] chunk bytes={chunk.Length} pending={pendingBytes} ascii=[{ascii}] hex=[{hex}]");
    }

    private void LogFrame(byte[] frame, int pendingAfter)
    {
        var hex = BitConverter.ToString(frame);
        var ascii = ToPrintableAscii(frame);
        var decoded = DecodeEmulatedValue(frame);
        var signature = frame.Length >= 4 ? BitConverter.ToString(frame, 0, 4) : string.Empty;

        var u16be = ReadU16BeWords(frame);
        var u16le = ReadU16LeWords(frame);

        Log($"[{Now()}] frame bytes={frame.Length} pendingAfter={pendingAfter} ascii=[{ascii}] hex=[{hex}] " +
            $"u16be=[{string.Join(',', u16be)}] u16le=[{string.Join(',', u16le)}] guesses=[{BuildGuessSummary(frame)}]");

        try
        {
            OnFrame?.Invoke(new PlcDecodedFrame(DateTime.Now, hex, signature, decoded));
        }
        catch
        {
            // best-effort callback only
        }
    }

    private static int[] ReadU16BeWords(byte[] frame)
    {
        var words = new List<int>();
        for (int i = 0; i + 1 < frame.Length; i += 2)
            words.Add((frame[i] << 8) | frame[i + 1]);
        return words.ToArray();
    }

    private static int[] ReadU16LeWords(byte[] frame)
    {
        var words = new List<int>();
        for (int i = 0; i + 1 < frame.Length; i += 2)
            words.Add((frame[i + 1] << 8) | frame[i]);
        return words.ToArray();
    }

    private static string BuildGuessSummary(byte[] frame)
    {
        var digits = frame.Where(b => b >= (byte)'0' && b <= (byte)'9').Select(b => (char)b).ToArray();
        var digitText = digits.Length == 0 ? "none" : new string(digits);
        var decoded = DecodeEmulatedValue(frame);

        if (frame.Length >= 8)
        {
            uint u32be0 = ((uint)frame[0] << 24) | ((uint)frame[1] << 16) | ((uint)frame[2] << 8) | frame[3];
            uint u32be1 = ((uint)frame[4] << 24) | ((uint)frame[5] << 16) | ((uint)frame[6] << 8) | frame[7];
            return $"decoded={decoded};digits={digitText};u32be0={u32be0};u32be1={u32be1}";
        }

        return $"decoded={decoded};digits={digitText}";
    }

    private static string DecodeEmulatedValue(byte[] frame)
    {
        // First-pass decoder derived from observed CP1E emulation frames.
        // Pattern is stable in bytes[4..7], while bytes[0..3] vary by emulated number.
        if (frame.Length < 8)
            return "unknown(frame-too-short)";

        string signature = BitConverter.ToString(frame, 0, 4);
        return signature switch
        {
            "00-98-E0-98" => "1",
            "00-18-C3-98" => "2",
            "00-98-78-CC" => "3",
            "00-18-CC-98" => "4",
            "00-18-F0-98" => "8",
            "00-98-F8-98" => "9",
            _ => $"unknown(sig={signature})",
        };
    }

    private void EnsureLogDirectory()
    {
        var dir = Path.GetDirectoryName(_logPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
    }

    private void Log(string line)
    {
        try
        {
            File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.ASCII);
        }
        catch
        {
            // best-effort logging only
        }
    }

    private static string ToPrintableAscii(byte[] bytes)
    {
        var chars = bytes.Select(b => b >= 32 && b <= 126 ? (char)b : '.').ToArray();
        return new string(chars);
    }

    private static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

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

public sealed record PlcDecodedFrame(DateTime Timestamp, string HexFrame, string Signature, string DecodedValue);
