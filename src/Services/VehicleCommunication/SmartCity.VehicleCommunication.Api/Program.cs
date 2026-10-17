using Microsoft.EntityFrameworkCore;
using SmartCity.BuildingBlocks;
using SmartCity.VehicleCommunication.Api.Grpc;
using SmartCity.VehicleCommunication.Api.Jobs;
using SmartCity.VehicleCommunication.Api.Mqtt;
using SmartCity.VehicleCommunication.Application;
using SmartCity.VehicleCommunication.Infrastructure;
using SmartCity.VehicleCommunication.Infrastructure.Persistence;

const string ServiceName = "VehicleCommunication";

var builder = WebApplication.CreateBuilder(args);
builder.AddSmartCityLogging(ServiceName);
builder.ConfigureSmartCityKestrel();

builder.Services.AddVehicleCommApplication(builder.Configuration);
builder.Services.AddVehicleCommInfrastructure(builder.Configuration);

builder.AddSmartCityBus(ServiceName, x => x.AddSmartCityOutbox<VehicleCommDbContext>());
builder.AddSmartCityBlobStorage();
builder.AddSmartCityHealthChecks();

// SKELETON: all gRPC services share the gRPC port (Ports:Grpc). With mTLS, VehicleIngestService moves to its own
// port that requires a vehicle certificate, so vehicles cannot reach the admin services.
builder.Services.AddGrpc()
    .AddServiceOptions<VehicleIngestGrpcService>(o => o.MaxReceiveMessageSize = VehicleIngestGrpcService.MaxMessageBytes);

// MQTT (telemetry and status in; fleet/status and config out) and the background jobs.
builder.Services.AddHostedService<MqttListener>();
builder.Services.AddHostedService<OfflineSweepJob>();
builder.Services.AddHostedService<StorageWatchJob>();

var app = builder.Build();
app.MapSmartCityHealthChecks();
app.MapGrpcService<VehicleIngestGrpcService>();
app.MapGrpcService<VehicleAdminGrpcService>();
app.MapGrpcService<VehicleQueryGrpcService>();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<VehicleCommDbContext>().Database.MigrateAsync();
    if (app.Configuration.GetValue<bool>("Seed:DevVehicles"))
        await scope.ServiceProvider.GetRequiredService<DevVehicleSeeder>().SeedAsync(CancellationToken.None);
}

await app.EnsureBucketsAsync("frames-temp", "frames-permanent", "evidence");

app.Run();
