// Creates read-only MongoDB views over the StandAlone data lake for browsing in Compass.
// Views are saved queries: they store no data, always reflect the live collections, and can be
// dropped/recreated freely. Re-run any time:
//   mongosh "mongodb://localhost:27017/StandAloneDataLake" --file tools/create-datalake-views.js

const realPlant = { PlantName: { $nin: [null, ""] } };

function recreate(name, source, pipeline) {
  if (db.getCollectionNames().includes(name)) db.getCollection(name).drop();
  db.createView(name, source, pipeline);
  print(`created ${name} (${db.getCollection(name).countDocuments()} rows)`);
}

// 1. One row per station: identity, hardware and configuration, plus whether it is alive.
recreate("v_station_config", "stations", [
  { $match: realPlant },
  { $project: {
      _id: 0,
      Plant: "$PlantName",
      Station: "$StationId",
      Line: "$LineNumber",
      Roles: { $ifNull: ["$Roles", []] },
      PrinterModel: "$Hardware.PrinterModel",
      PrinterLanguage: "$Hardware.ThermalPrinterType",
      LabelOutput: "$Hardware.LabelOutputType",
      PlcConnection: "$Hardware.PlcConnectionType",
      PlcAddress: "$Hardware.PlcAddress",
      CartonPrintMode: "$Config.CartonPrintMode",
      BackflushVehicle: "$Config.BackflushVehicle",
      PalletLocation: "$Config.PalletLocation",
      LastHeartbeatUtc: 1,
      HoursSinceHeartbeat: { $round: [{ $divide: [{ $dateDiff: { startDate: "$LastHeartbeatUtc", endDate: "$$NOW", unit: "minute" } }, 60] }, 1] },
  } },
  { $addFields: { State: { $cond: [{ $lte: ["$HoursSinceHeartbeat", 12] }, "Active", "Idle"] } } },
  { $sort: { Plant: 1, Station: 1 } },
]);

// 2. Pallets still on the floor: printed or scanned, not yet backflushed.
recreate("v_pallet_inventory", "pallets", [
  { $match: { ...realPlant, Status: { $in: ["Printed", "Scanned", "BackflushFailed"] } } },
  { $project: {
      _id: 0,
      Pallet: "$_id",
      Plant: "$PlantName",
      Status: 1,
      Item: "$ItemNumber",
      Color: "$ColorDesc",
      Size: "$ShapeDesc",
      Series: "$SeriesDesc",
      Shade: 1,
      Grade: 1,
      Cartons: "$BoxesPerPallet",
      Pieces: { $multiply: ["$BoxesPerPallet", "$LisQty"] },
      PrintedAtStation: "$StationId",
      LastStation: { $ifNull: ["$LastStationId", "$StationId"] },
      CreatedUtc: 1,
      AgeHours: { $round: [{ $divide: [{ $dateDiff: { startDate: "$CreatedUtc", endDate: "$$NOW", unit: "minute" } }, 60] }, 1] },
  } },
  { $sort: { Plant: 1, CreatedUtc: -1 } },
]);

// 3. Inventory rolled up per plant / item / shade / status.
recreate("v_inventory_by_item", "pallets", [
  { $match: realPlant },
  { $group: {
      _id: { Plant: "$PlantName", Item: "$ItemNumber", Shade: "$Shade", Status: "$Status" },
      Color: { $first: "$ColorDesc" },
      Size: { $first: "$ShapeDesc" },
      Pallets: { $sum: 1 },
      Cartons: { $sum: "$BoxesPerPallet" },
      Pieces: { $sum: { $multiply: ["$BoxesPerPallet", "$LisQty"] } },
      LastActivityUtc: { $max: "$UpdatedUtc" },
  } },
  { $project: { _id: 0, Plant: "$_id.Plant", Item: "$_id.Item", Shade: "$_id.Shade", Status: "$_id.Status",
                Color: 1, Size: 1, Pallets: 1, Cartons: 1, Pieces: 1, LastActivityUtc: 1 } },
  { $sort: { Plant: 1, Item: 1, Status: 1 } },
]);

// 4. Carton activity. A carton barcode is shared by identical cartons printed the same day
//    (same formula as Progress dtlbl060b.i), so "TimesPrinted" is the carton count per barcode.
recreate("v_carton_activity", "cartons", [
  { $match: realPlant },
  { $project: {
      _id: 0,
      Barcode: "$_id",
      Plant: "$PlantName",
      Item: "$ItemNumber",
      Line: "$LineNumber",
      Stack: "$StackNumber",
      Status: 1,
      PrintedAtStation: "$StationId",
      LastStation: { $ifNull: ["$LastStationId", "$StationId"] },
      Pallet: { $ifNull: ["$PalletId", ""] },
      TimesPrinted: { $size: { $filter: { input: { $ifNull: ["$History", []] }, cond: { $eq: ["$$this.Status", "Printed"] } } } },
      TimesPalletized: { $size: { $filter: { input: { $ifNull: ["$History", []] }, cond: { $eq: ["$$this.Status", "Palletized"] } } } },
      FirstPrintedUtc: "$CreatedUtc",
      LastActivityUtc: "$UpdatedUtc",
  } },
  { $sort: { LastActivityUtc: -1 } },
]);

// 5. Each pallet's journey across stations in one row.
const stepAt = (status, field) => ({
  $let: { vars: { e: { $first: { $filter: { input: { $ifNull: ["$History", []] }, cond: { $eq: ["$$this.Status", status] } } } } },
          in: `$$e.${field}` } });
recreate("v_pallet_trail", "pallets", [
  { $match: realPlant },
  { $project: {
      _id: 0,
      Pallet: "$_id",
      Plant: "$PlantName",
      Item: "$ItemNumber",
      Status: 1,
      PrintedUtc: stepAt("Printed", "TimestampUtc"),
      PrintedBy: stepAt("Printed", "StationId"),
      ScannedUtc: stepAt("Scanned", "TimestampUtc"),
      ScannedBy: stepAt("Scanned", "StationId"),
      BackflushedUtc: stepAt("Backflushed", "TimestampUtc"),
      BackflushedBy: stepAt("Backflushed", "StationId"),
      Steps: { $size: { $ifNull: ["$History", []] } },
  } },
  { $addFields: { MinutesToBackflush: { $cond: [
      { $and: ["$PrintedUtc", "$BackflushedUtc"] },
      { $round: [{ $divide: [{ $subtract: ["$BackflushedUtc", "$PrintedUtc"] }, 60000] }, 1] },
      null] } } },
  { $sort: { PrintedUtc: -1 } },
]);

// 6. One row per plant.
recreate("v_plant_summary", "stations", [
  { $match: realPlant },
  { $group: { _id: "$PlantName", Stations: { $sum: 1 }, LastHeartbeatUtc: { $max: "$LastHeartbeatUtc" } } },
  { $lookup: { from: "cartons", localField: "_id", foreignField: "PlantName", as: "c",
               pipeline: [{ $project: { n: { $size: { $filter: { input: { $ifNull: ["$History", []] }, cond: { $eq: ["$$this.Status", "Printed"] } } } } } }] } },
  { $lookup: { from: "pallets", localField: "_id", foreignField: "PlantName", as: "p",
               pipeline: [{ $project: { Status: 1 } }] } },
  { $project: {
      _id: 0,
      Plant: "$_id",
      Stations: 1,
      CartonsPrinted: { $sum: "$c.n" },
      PalletsPrinted: { $size: "$p" },
      PalletsOnHand: { $size: { $filter: { input: "$p", cond: { $in: ["$$this.Status", ["Printed", "Scanned", "BackflushFailed"]] } } } },
      PalletsBackflushed: { $size: { $filter: { input: "$p", cond: { $eq: ["$$this.Status", "Backflushed"] } } } },
      LastHeartbeatUtc: 1,
  } },
  { $sort: { Plant: 1 } },
]);
