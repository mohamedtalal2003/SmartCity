using SmartCity.BuildingBlocks;

const string ServiceName = "ApiGateway";

var builder = WebApplication.CreateBuilder(args);
builder.AddSmartCityLogging(ServiceName);
builder.ConfigureSmartCityKestrel();
// No message bus here: the Gateway talks to services over gRPC only (CLAUDE.md, invariant 2).
builder.AddSmartCityHealthChecks();

var app = builder.Build();
app.MapSmartCityHealthChecks();

app.Run();
