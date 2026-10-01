using SmartCity.BuildingBlocks;
using SmartCity.VehicleCommunication.Mqtt;
using StackExchange.Redis;

const string ServiceName = "VehicleCommunication";

var builder = WebApplication.CreateBuilder(args);
builder.AddSmartCityLogging(ServiceName);
builder.ConfigureSmartCityKestrel();
builder.AddSmartCityBus(ServiceName);
builder.AddSmartCityBlobStorage();
builder.AddSmartCityHealthChecks();

builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379"));

builder.Services.AddHostedService<MqttSubscriberService>();

var app = builder.Build();
app.MapSmartCityHealthChecks();
await app.EnsureBucketsAsync("frames-temp", "frames-permanent", "evidence");

app.Run();
