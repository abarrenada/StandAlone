using MongoDB.Bson;
using MongoDB.Driver;

namespace StandAlone.Integration.Services;

/// <summary>
/// Configuration for the centralized MongoDB data lake — see the "StandAlone Rollout Map"
/// architecture: every plant/station writes one-way into one shared MongoDB, which is a
/// results store, not a source of truth (nothing in this port ever reads from it back).
/// </summary>
public class DataLakeSettings
{
    public string ConnectionString { get; set; } = "mongodb://localhost:27017";
    public string DatabaseName { get; set; } = "StandAloneDataLake";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString) && !string.IsNullOrWhiteSpace(DatabaseName);
}

/// <summary>One plant/station's identity, hardware, and configuration snapshot — the
/// data-lake analogue of "which plants, which stations, what hardware, what config"
/// the rollout map calls for. Upserted at startup and whenever settings are saved.</summary>
public class StationInfo
{
    public string PlantName { get; set; } = string.Empty;
    public int Plant { get; set; }
    public string StationId { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public string PlcConnectionType { get; set; } = string.Empty;
    public string PlcAddress { get; set; } = string.Empty;
    public int PlcBaudRate { get; set; }
    public int PlcPort { get; set; }
    public string PrinterModel { get; set; } = string.Empty;
    public string ThermalPrinterType { get; set; } = string.Empty;
    public string LabelOutputType { get; set; } = string.Empty;
    public string BackflushVehicle { get; set; } = string.Empty;
    public string CartonPrintMode { get; set; } = string.Empty;
    public string PalletLocation { get; set; } = string.Empty;

    /// <summary>What this station is doing: "Carton Labels", "Pallet Scan" or "EOL Scan".
    /// Accumulated into a Roles set, since one station can open more than one screen.</summary>
    public string Role { get; set; } = string.Empty;
    public DateTime LastHeartbeatUtc { get; set; }
}

/// <summary>One carton or pallet's current status, for the History array on both the
/// cartons and pallets collections.</summary>
public class LakeStatusEvent
{
    public string Status { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; }
    public string Detail { get; set; } = string.Empty;
}

public class CartonLakeRecord
{
    public string BarcodeSerial { get; set; } = string.Empty;
    public string PlantName { get; set; } = string.Empty;
    public string StationId { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public string ItemNumber { get; set; } = string.Empty;
    public int StackNumber { get; set; }

    /// <summary>Pallet this carton was scanned onto (set by Pallet Scan's "Palletized" event).</summary>
    public string PalletId { get; set; } = string.Empty;
}

public class PalletLakeRecord
{
    public string PalletId { get; set; } = string.Empty;
    public string PlantName { get; set; } = string.Empty;
    public string StationId { get; set; } = string.Empty;
    public int Plant { get; set; }
    public string ItemNumber { get; set; } = string.Empty;
    public string ColorDesc { get; set; } = string.Empty;
    public string ShapeDesc { get; set; } = string.Empty;
    public string SeriesDesc { get; set; } = string.Empty;
    public int LisQty { get; set; }
    public int BoxesPerPallet { get; set; }
    public string Shade { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string ShopOrder { get; set; } = string.Empty;
    public int Grade { get; set; }
    public int LineNumber { get; set; }
    public int Shift { get; set; }
    public string Inspector { get; set; } = string.Empty;
}

/// <summary>
/// Writes to the centralized data lake. Deliberately best-effort: every method swallows
/// its own exceptions (returns without throwing) so a MongoDB outage or misconfiguration
/// can never block or fail a print/scan on the shop floor — this is a write-only telemetry
/// sink, not a dependency of the core workflow, matching the "not a data source" framing in
/// the rollout map. Nothing in this port ever reads from it.
/// </summary>
public interface IDataLakeService
{
    Task UpsertStationAsync(StationInfo station, CancellationToken cancellationToken = default);
    Task RecordCartonEventAsync(CartonLakeRecord carton, string status, string detail = "", CancellationToken cancellationToken = default);
    Task RecordPalletEventAsync(PalletLakeRecord pallet, string status, string detail = "", CancellationToken cancellationToken = default);
}

public class MongoDataLakeService : IDataLakeService
{
    private readonly IMongoCollection<BsonDocument> _stations;
    private readonly IMongoCollection<BsonDocument> _cartons;
    private readonly IMongoCollection<BsonDocument> _pallets;

    public MongoDataLakeService(DataLakeSettings settings)
    {
        var client = new MongoClient(settings.ConnectionString);
        var db = client.GetDatabase(settings.DatabaseName);
        _stations = db.GetCollection<BsonDocument>("stations");
        _cartons = db.GetCollection<BsonDocument>("cartons");
        _pallets = db.GetCollection<BsonDocument>("pallets");
    }

    public async Task UpsertStationAsync(StationInfo station, CancellationToken cancellationToken = default)
    {
        try
        {
            var id = $"{station.PlantName}-{station.StationId}";
            var filter = Builders<BsonDocument>.Filter.Eq("_id", id);
            var update = Builders<BsonDocument>.Update
                .Set("PlantName", station.PlantName)
                .Set("Plant", station.Plant)
                .Set("StationId", station.StationId)
                .Set("LineNumber", station.LineNumber)
                .Set("Hardware", new BsonDocument
                {
                    { "PlcConnectionType", station.PlcConnectionType },
                    { "PlcAddress", station.PlcAddress },
                    { "PlcBaudRate", station.PlcBaudRate },
                    { "PlcPort", station.PlcPort },
                    { "PrinterModel", station.PrinterModel },
                    { "ThermalPrinterType", station.ThermalPrinterType },
                    { "LabelOutputType", station.LabelOutputType },
                })
                .Set("Config", new BsonDocument
                {
                    { "BackflushVehicle", station.BackflushVehicle },
                    { "CartonPrintMode", station.CartonPrintMode },
                    { "PalletLocation", station.PalletLocation },
                })
                .Set("LastHeartbeatUtc", station.LastHeartbeatUtc);
            if (!string.IsNullOrWhiteSpace(station.Role))
                update = update.AddToSet("Roles", station.Role);

            await _stations.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true }, cancellationToken);
        }
        catch { /* best-effort telemetry — never block the shop floor on a lake outage */ }
    }

    public async Task RecordCartonEventAsync(CartonLakeRecord carton, string status, string detail = "", CancellationToken cancellationToken = default)
    {
        try
        {
            var now = DateTime.UtcNow;
            var filter = Builders<BsonDocument>.Filter.Eq("_id", carton.BarcodeSerial);
            var historyEntry = new BsonDocument
            {
                { "Status", status },
                { "StationId", carton.StationId },
                { "TimestampUtc", now },
                { "Detail", detail },
            };
            var update = Builders<BsonDocument>.Update
                .Set("PlantName", carton.PlantName)
                .Set("ItemNumber", carton.ItemNumber)
                .Set("Status", status)
                .Set("LastStationId", carton.StationId)
                .Set("UpdatedUtc", now)
                .SetOnInsert("CreatedUtc", now)
                .Push("History", historyEntry);
            update = SetOrigin(update, status,
                ("StationId", carton.StationId), ("LineNumber", carton.LineNumber), ("StackNumber", carton.StackNumber));
            if (!string.IsNullOrWhiteSpace(carton.PalletId))
                update = update.Set("PalletId", carton.PalletId);

            await _cartons.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true }, cancellationToken);
        }
        catch { /* best-effort telemetry */ }
    }

    public async Task RecordPalletEventAsync(PalletLakeRecord pallet, string status, string detail = "", CancellationToken cancellationToken = default)
    {
        try
        {
            var now = DateTime.UtcNow;
            var filter = Builders<BsonDocument>.Filter.Eq("_id", pallet.PalletId);
            var historyEntry = new BsonDocument
            {
                { "Status", status },
                { "StationId", pallet.StationId },
                { "TimestampUtc", now },
                { "Detail", detail },
            };
            var update = Builders<BsonDocument>.Update
                .Set("PlantName", pallet.PlantName)
                .Set("LastStationId", pallet.StationId)
                .Set("Plant", pallet.Plant)
                .Set("ItemNumber", pallet.ItemNumber)
                .Set("ColorDesc", pallet.ColorDesc)
                .Set("ShapeDesc", pallet.ShapeDesc)
                .Set("SeriesDesc", pallet.SeriesDesc)
                .Set("LisQty", pallet.LisQty)
                .Set("BoxesPerPallet", pallet.BoxesPerPallet)
                .Set("Shade", pallet.Shade)
                .Set("Size", pallet.Size)
                .Set("ShopOrder", pallet.ShopOrder)
                .Set("Grade", pallet.Grade)
                .Set("LineNumber", pallet.LineNumber)
                .Set("Shift", pallet.Shift)
                .Set("Inspector", pallet.Inspector)
                .Set("Status", status)
                .Set("UpdatedUtc", now)
                .SetOnInsert("CreatedUtc", now)
                .Push("History", historyEntry);
            update = SetOrigin(update, status, ("StationId", pallet.StationId));

            await _pallets.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true }, cancellationToken);
        }
        catch { /* best-effort telemetry */ }
    }

    /// <summary>
    /// Top-level StationId/LineNumber/StackNumber describe where a carton or pallet was made, so
    /// only the "Printed" event may overwrite them; later events from other stations (Palletized,
    /// Scanned, Backflushed) fill them only if the record doesn't exist yet. Who did each step is
    /// kept per History entry and in LastStationId.
    /// </summary>
    private static UpdateDefinition<BsonDocument> SetOrigin(
        UpdateDefinition<BsonDocument> update, string status, params (string Field, BsonValue Value)[] fields)
    {
        var isOrigin = string.Equals(status, "Printed", StringComparison.OrdinalIgnoreCase);
        foreach (var (field, value) in fields)
            update = isOrigin ? update.Set(field, value) : update.SetOnInsert(field, value);
        return update;
    }
}
