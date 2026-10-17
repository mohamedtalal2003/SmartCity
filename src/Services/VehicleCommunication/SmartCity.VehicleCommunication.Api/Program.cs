using Microsoft.EntityFrameworkCore;
using SmartCity.BuildingBlocks;
using SmartCity.VehicleCommunication.Api.Mqtt;
using SmartCity.VehicleCommunication.Application;
using SmartCity.VehicleCommunication.Infrastructure;
using SmartCity.VehicleCommunication.Infrastructure.Persistence;
using StackExchange.Redis;

const string ServiceName = "VehicleCommunication";

var builder = WebApplication.CreateBuilder(args);
builder.AddSmartCityLogging(ServiceName);
builder.ConfigureSmartCityKestrel();

builder.Services.AddVehicleCommApplication(builder.Configuration);
builder.Services.AddVehicleCommInfrastructure(builder.Configuration);

builder.AddSmartCityBus(ServiceName, x => x.AddSmartCityOutbox<VehicleCommDbContext>());
builder.AddSmartCityBlobStorage();
builder.AddSmartCityHealthChecks();

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379"));

builder.Services.AddHostedService<MqttSubscriberService>();

var app = builder.Build();
app.MapSmartCityHealthChecks();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<VehicleCommDbContext>().Database.MigrateAsync();
    if (app.Configuration.GetValue<bool>("Seed:DevVehicles"))
        await scope.ServiceProvider.GetRequiredService<DevVehicleSeeder>().SeedAsync(CancellationToken.None);
}

await app.EnsureBucketsAsync("frames-temp", "frames-permanent", "evidence");

app.Run();
