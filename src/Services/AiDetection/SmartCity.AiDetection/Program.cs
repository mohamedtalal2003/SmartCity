using SmartCity.BuildingBlocks;

const string ServiceName = "AiDetection";

var builder = WebApplication.CreateBuilder(args);
builder.AddSmartCityLogging(ServiceName);
builder.ConfigureSmartCityKestrel();
builder.AddSmartCityBus(ServiceName);
builder.AddSmartCityHealthChecks();

var app = builder.Build();
app.MapSmartCityHealthChecks();

app.Run();
