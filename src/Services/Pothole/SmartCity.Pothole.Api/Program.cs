using Microsoft.EntityFrameworkCore;
using SmartCity.BuildingBlocks;
using SmartCity.Pothole.Api.Consumers;
using SmartCity.Pothole.Api.Grpc;
using SmartCity.Pothole.Application;
using SmartCity.Pothole.Infrastructure;
using SmartCity.Pothole.Infrastructure.Persistence;

const string ServiceName = "Pothole";

var builder = WebApplication.CreateBuilder(args);
builder.AddSmartCityLogging(ServiceName);
builder.ConfigureSmartCityKestrel();

builder.Services.AddPotholeApplication(builder.Configuration);
builder.Services.AddPotholeInfrastructure(builder.Configuration);

builder.AddSmartCityBus(ServiceName, x =>
{
    x.AddConsumer<RawDetectionReceivedConsumer>();
    x.AddConsumer<WorkOrderCreatedConsumer>();
    x.AddConsumer<WorkOrderStatusChangedConsumer>();
    x.AddSmartCityOutbox<PotholeDbContext>();
});

builder.AddSmartCityHealthChecks();
builder.Services.AddGrpc();

var app = builder.Build();
app.MapSmartCityHealthChecks();
app.MapGrpcService<RoadDamageQueryGrpcService>();
app.MapGrpcService<AreaQueryGrpcService>();
app.MapGrpcService<PotholeCommandGrpcService>();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PotholeDbContext>();
    await db.Database.MigrateAsync();

    var seeder = scope.ServiceProvider.GetRequiredService<AdminAreaSeeder>();
    await seeder.SeedAsync(CancellationToken.None);
}

app.Run();
