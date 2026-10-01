using SmartCity.BuildingBlocks;

const string ServiceName = "Building";

var builder = WebApplication.CreateBuilder(args);
builder.AddSmartCityLogging(ServiceName);
builder.ConfigureSmartCityKestrel();
builder.AddSmartCityBus(ServiceName);
builder.AddSmartCityHealthChecks();
builder.Services.AddGrpc();

var app = builder.Build();
app.MapSmartCityHealthChecks();

app.Run();
