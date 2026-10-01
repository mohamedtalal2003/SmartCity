using SmartCity.BuildingBlocks;

const string ServiceName = "VehicleCommunication";

var builder = WebApplication.CreateBuilder(args);
builder.AddSmartCityLogging(ServiceName);
builder.ConfigureSmartCityKestrel();
builder.AddSmartCityBus(ServiceName);
builder.AddSmartCityBlobStorage();
builder.AddSmartCityHealthChecks();

var app = builder.Build();
app.MapSmartCityHealthChecks();
await app.EnsureBucketsAsync("frames-temp", "frames-permanent", "evidence");

app.Run();
