namespace LabelDecisionApp.Integration.Services;

public class PlcPayload
{
    public string DeviceType { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string ItemNumber { get; set; } = string.Empty;
    public int Plant { get; set; }
    public string RawData { get; set; } = string.Empty;
}

public interface IPlcPayloadParser
{
    PlcPayload? Parse(string rawPayload);
}

public class PlcPayloadParser : IPlcPayloadParser
{
    public PlcPayload? Parse(string rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
            return null;

        rawPayload = rawPayload.Trim();
        var payload = new PlcPayload { RawData = rawPayload };

        if (rawPayload.StartsWith("RES", StringComparison.OrdinalIgnoreCase))
        {
            // PLC response format (from Progress cSlabPigmentPlcData)
            if (rawPayload.Length > 4)
            {
                payload.DeviceType = "PLC";
                payload.SerialNumber = rawPayload.Substring(0, 20).Trim();
            }
            return payload;
        }

        if (rawPayload.StartsWith("EOL", StringComparison.OrdinalIgnoreCase))
        {
            payload.DeviceType = "EOL";
            var parts = rawPayload.Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                payload.SerialNumber = parts[1].Trim();
            }

            if (parts.Length >= 3)
            {
                payload.ItemNumber = parts[2].Trim();
            }

            if (parts.Length >= 4)
            {
                int.TryParse(parts[3].Trim(), out var plant);
                payload.Plant = plant;
            }

            return payload;
        }

        if (rawPayload.Contains("|"))
        {
            // Pipe-delimited format
            var parts = rawPayload.Split('|');
            if (parts.Length >= 4)
            {
                payload.DeviceType = parts[0].Trim();
                payload.SerialNumber = parts[1].Trim();
                payload.ItemNumber = parts[2].Trim();
                int.TryParse(parts[3].Trim(), out var plant);
                payload.Plant = plant;
                return payload;
            }
        }

        if (rawPayload.Contains(","))
        {
            // CSV format
            var parts = rawPayload.Split(',');
            if (parts.Length >= 4)
            {
                payload.DeviceType = parts[0].Trim();
                payload.SerialNumber = parts[1].Trim();
                payload.ItemNumber = parts[2].Trim();
                int.TryParse(parts[3].Trim(), out var plant);
                payload.Plant = plant;
                return payload;
            }
        }

        // Support scanner and EOL payloads that contain only a serial number.
        if (rawPayload.Length >= 20)
        {
            payload.DeviceType = "SERIAL";
            payload.SerialNumber = rawPayload;
            return payload;
        }

        return null;
    }
}
