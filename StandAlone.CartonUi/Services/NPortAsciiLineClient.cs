using System.Net.Sockets;
using System.Text;

namespace StandAlone.CartonUi.Services;

/// <summary>
/// Persistent outbound TCP connection to a Moxa NPort (TCP Server mode) whose serial side
/// delivers plain ASCII. Reconnects every 2 s, splits the stream into lines (CR, LF or ETX
/// terminated, leading STX dropped) and, when the NPort has no delimiter configured, treats data
/// that sits unterminated for <see cref="IdleFlushMs"/> as a complete line. Every raw chunk is
/// written to the log as ASCII + hex so new devices can be diagnosed from the log alone.
/// </summary>
public sealed class NPortAsciiLineClient
{
    private const int IdleFlushMs = 300;

    private readonly string _host;
    private readonly int _port;
    private readonly string _logPath;
    private readonly string _logTag;

    public NPortAsciiLineClient(string host, int port, string logPath, string logTag)
    {
        _host = host;
        _port = port;
        _logPath = logPath;
        _logTag = logTag;
    }

    public Action<string>? OnStatus;

    /// <summary>Raw line (not trimmed, never containing a delimiter) and how it was terminated.</summary>
    public Action<string, string>? OnLine;

    public async Task RunAsync(CancellationToken ct)
    {
        EnsureLogDirectory();
        Log($"[{Now()}] {_logTag}_start host={_host} port={_port} mode=ascii-line");

        while (!ct.IsCancellationRequested)
        {
            TcpClient? client = null;
            NetworkStream? stream = null;

            try
            {
                client = new TcpClient();
                OnStatus?.Invoke($"Connecting {_host}:{_port}...");
                await client.ConnectAsync(_host, _port, ct);
                stream = client.GetStream();

                OnStatus?.Invoke($"Connected {_host}:{_port}");
                Log($"[{Now()}] connected");

                var chunkBuffer = new byte[1024];
                var pending = new List<byte>(64);
                Task<int>? readTask = null;

                while (!ct.IsCancellationRequested && client.Connected)
                {
                    readTask ??= stream.ReadAsync(chunkBuffer, 0, chunkBuffer.Length, ct);

                    // With unterminated data waiting, give the next chunk a short window to arrive;
                    // if it doesn't, the NPort has no delimiter configured, so flush what we have.
                    if (pending.Count > 0)
                    {
                        var winner = await Task.WhenAny(readTask, Task.Delay(IdleFlushMs, ct));
                        if (winner != readTask)
                        {
                            ct.ThrowIfCancellationRequested();
                            EmitLine(pending.ToArray(), "idle-flush");
                            pending.Clear();
                            continue;
                        }
                    }

                    var read = await readTask;
                    readTask = null;
                    if (read <= 0)
                        break;

                    var chunk = chunkBuffer.AsSpan(0, read).ToArray();
                    Log($"[{Now()}] chunk bytes={chunk.Length} ascii=[{ToPrintableAscii(chunk)}] hex=[{BitConverter.ToString(chunk)}]");

                    foreach (var b in chunk)
                    {
                        if (b == '\r' || b == '\n' || b == 0x03)
                        {
                            if (pending.Count > 0)
                                EmitLine(pending.ToArray(), "delimiter");
                            pending.Clear();
                        }
                        else if (b != 0x02)
                        {
                            pending.Add(b);
                        }
                    }
                }

                if (pending.Count > 0)
                    EmitLine(pending.ToArray(), "disconnect-flush");

                OnStatus?.Invoke($"Disconnected from {_host}:{_port}; retrying...");
                Log($"[{Now()}] disconnected");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                OnStatus?.Invoke($"Connection error {_host}:{_port}: {ex.Message}; retrying...");
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

        Log($"[{Now()}] {_logTag}_stop");
    }

    public void Log(string line)
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

    public static string ToPrintableAscii(byte[] bytes)
    {
        var chars = bytes.Select(b => b >= 32 && b <= 126 ? (char)b : '.').ToArray();
        return new string(chars);
    }

    public static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

    private void EmitLine(byte[] lineBytes, string reason)
    {
        try
        {
            OnLine?.Invoke(Encoding.ASCII.GetString(lineBytes), reason);
        }
        catch
        {
            // best-effort callback only
        }
    }

    private void EnsureLogDirectory()
    {
        var dir = Path.GetDirectoryName(_logPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
    }
}
