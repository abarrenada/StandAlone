using MongoDB.Bson;
using MongoDB.Driver;

// Read-only browser UI over the StandAlone data lake. Serves wwwroot/index.html and a small JSON
// API backed by the v_* views from tools/create-datalake-views.js plus the raw collections for
// drill-down. Never writes to MongoDB.
var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration["DataLake:ConnectionString"] ?? "mongodb://localhost:27017";
var databaseName     = builder.Configuration["DataLake:DatabaseName"] ?? "StandAloneDataLake";
var db = new MongoClient(connectionString).GetDatabase(databaseName);

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

// Menu key → view name. Only these are reachable through /api/view.
var views = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["summary"]   = "v_plant_summary",
    ["stations"]  = "v_station_config",
    ["pallets"]   = "v_pallet_inventory",
    ["inventory"] = "v_inventory_by_item",
    ["trail"]     = "v_pallet_trail",
    ["cartons"]   = "v_carton_activity",
};

app.MapGet("/api/health", async () =>
{
    try
    {
        await db.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
        var names = await (await db.ListCollectionNamesAsync()).ToListAsync();
        var missing = views.Values.Where(v => !names.Contains(v)).ToList();
        return Results.Ok(new { ok = missing.Count == 0, database = databaseName, missingViews = missing });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { ok = false, database = databaseName, error = ex.Message });
    }
});

app.MapGet("/api/plants", async () =>
{
    var plants = await db.GetCollection<BsonDocument>("stations").DistinctAsync<string>("PlantName", FilterDefinition<BsonDocument>.Empty);
    return (await plants.ToListAsync()).Where(p => !string.IsNullOrWhiteSpace(p)).OrderBy(p => p).ToList();
});

app.MapGet("/api/view/{key}", async (string key, string? plant) =>
{
    if (!views.TryGetValue(key, out var viewName))
        return Results.NotFound(new { error = $"Unknown view '{key}'." });

    var filter = string.IsNullOrWhiteSpace(plant)
        ? FilterDefinition<BsonDocument>.Empty
        : Builders<BsonDocument>.Filter.Eq("Plant", plant);
    try
    {
        var rows = await db.GetCollection<BsonDocument>(viewName).Find(filter).Limit(2000).ToListAsync();
        return Results.Ok(rows.Select(ToPlain));
    }
    catch (MongoCommandException ex) when (ex.Code == 26) // NamespaceNotFound
    {
        return Results.Problem($"View {viewName} is missing — run tools/create-datalake-views.js.");
    }
});

// Drill-downs return the raw document (with History) and, for pallets, the cartons linked to it.
app.MapGet("/api/pallet", async (string id) =>
{
    var pallet = await db.GetCollection<BsonDocument>("pallets").Find(Builders<BsonDocument>.Filter.Eq("_id", id)).FirstOrDefaultAsync();
    if (pallet is null) return Results.NotFound();
    var cartons = await db.GetCollection<BsonDocument>("cartons").Find(Builders<BsonDocument>.Filter.Eq("PalletId", id)).ToListAsync();
    return Results.Ok(new { pallet = ToPlain(pallet), cartons = cartons.Select(ToPlain) });
});

app.MapGet("/api/carton", async (string id) =>
{
    var carton = await db.GetCollection<BsonDocument>("cartons").Find(Builders<BsonDocument>.Filter.Eq("_id", id)).FirstOrDefaultAsync();
    return carton is null ? Results.NotFound() : Results.Ok(ToPlain(carton));
});

app.MapGet("/api/station", async (string plant, string station) =>
{
    var doc = await db.GetCollection<BsonDocument>("stations").Find(Builders<BsonDocument>.Filter.Eq("_id", $"{plant}-{station}")).FirstOrDefaultAsync();
    return doc is null ? Results.NotFound() : Results.Ok(ToPlain(doc));
});

app.Run();

// BSON → plain .NET values so System.Text.Json emits normal JSON (ISO dates, arrays, objects).
static object? ToPlain(BsonValue value) => value.BsonType switch
{
    BsonType.Document => value.AsBsonDocument.Elements.ToDictionary(e => e.Name, e => ToPlain(e.Value)),
    BsonType.Array    => value.AsBsonArray.Select(ToPlain).ToList(),
    BsonType.DateTime => value.ToUniversalTime(),
    BsonType.Null     => null,
    BsonType.ObjectId => value.AsObjectId.ToString(),
    _                 => BsonTypeMapper.MapToDotNetValue(value),
};
