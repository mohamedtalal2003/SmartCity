using MassTransit;
using Microsoft.EntityFrameworkCore;
using SmartCity.BuildingBlocks;
using SmartCity.Pothole.Api.Consumers;
using SmartCity.Pothole.Api.Data;
using SmartCity.Pothole.Api.Grpc;

const string ServiceName = "Pothole";

var builder = WebApplication.CreateBuilder(args);
builder.AddSmartCityLogging(ServiceName);
builder.ConfigureSmartCityKestrel();

builder.Services.AddDbContext<PotholeDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Db"),
        npgsql => npgsql.UseNetTopologySuite()));

builder.AddSmartCityBus(ServiceName, x =>
{
    x.AddConsumer<RawDetectionReceivedConsumer>();
    x.AddSmartCityOutbox<PotholeDbContext>();
});

builder.AddSmartCityHealthChecks();
builder.Services.AddGrpc();

var app = builder.Build();
app.MapSmartCityHealthChecks();
app.MapGrpcService<RoadDamageQueryGrpcService>();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PotholeDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();
