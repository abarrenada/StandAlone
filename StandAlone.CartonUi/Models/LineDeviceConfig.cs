namespace StandAlone.CartonUi.Models;

/// <summary>
/// One line's device wiring for a given role — mirrors Progress's dev-detail table
/// (dtmnt036.p) and its packed "IFR:&lt;...&gt;;DEV:&lt;...&gt;;STTY:&lt;...&gt;;CMD:&lt;...&gt;;" dd-data-string
/// encoding, decoded here from a plain CSV export instead (see LineDeviceCatalog / data/devices.csv).
/// </summary>
public class LineDeviceConfig
{
    public int LineNumber { get; set; }

    /// <summary>"TERM", "S-SCAN", or "A-PTR" (matches Progress dd-device-type).</summary>
    public string DeviceType { get; set; } = string.Empty;

    /// <summary>"SER" (serial port), "IP" (host:port in Dev), or "CMD" (shell print command in Cmd).</summary>
    public string Ifr { get; set; } = "SER";

    /// <summary>COM port / tty path for SER, or "host:port" (or bare port) for IP.</summary>
    public string Dev { get; set; } = string.Empty;

    /// <summary>Serial line settings, e.g. " 9600 -parity icanon icrnl -echo" — first numeric token is the baud rate.</summary>
    public string Stty { get; set; } = string.Empty;

    /// <summary>Shell print command (only meaningful when Ifr = "CMD").</summary>
    public string Cmd { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public bool IsIp  => string.Equals(Ifr, "IP",  StringComparison.OrdinalIgnoreCase);
    public bool IsSer => string.Equals(Ifr, "SER", StringComparison.OrdinalIgnoreCase);
    public bool IsCmd => string.Equals(Ifr, "CMD", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses Dev as "host:port" or a bare port number (host is then empty — used when only a
    /// listening port matters, e.g. a scanner dialing in). Falls back to defaultPort when Dev has
    /// no port component. Returns false only when Dev is blank.
    /// </summary>
    public bool TryParseHostPort(int defaultPort, out string host, out int port)
    {
        host = string.Empty;
        port = defaultPort;

        var value = Dev.Trim();
        if (value.Length == 0)
            return false;

        var colonIndex = value.LastIndexOf(':');
        if (colonIndex > 0 && colonIndex < value.Length - 1)
        {
            host = value[..colonIndex].Trim();
            return int.TryParse(value[(colonIndex + 1)..].Trim(), out port) && port is > 0 and <= 65535;
        }

        if (int.TryParse(value, out port))
            return port is > 0 and <= 65535;

        host = value;
        port = defaultPort;
        return true;
    }

    /// <summary>First numeric token in Stty (e.g. " 9600 -parity icanon..." → 9600); falls back when absent.</summary>
    public int ParseBaudRate(int fallback)
    {
        if (string.IsNullOrWhiteSpace(Stty))
            return fallback;

        foreach (var token in Stty.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(token, out var baud) && baud > 0)
                return baud;
        }
        return fallback;
    }
}
