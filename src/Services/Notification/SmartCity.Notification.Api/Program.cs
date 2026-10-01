using MassTransit;
using Microsoft.EntityFrameworkCore;
using SmartCity.BuildingBlocks;
using SmartCity.Notification.Api.Consumers;
using SmartCity.Notification.Api.Data;
using SmartCity.Notification.Api.Grpc;
using SmartCity.Notification.Api.Hubs;

const string ServiceName = "Notification";

var builder = WebApplication.CreateBuilder(args);
builder.AddSmartCityLogging(ServiceName);
builder.ConfigureSmartCityKestrel();

builder.Services.AddDbContext<NotificationDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Db")));

builder.AddSmartCityBus(ServiceName, x =>
{
    x.AddConsumer<WorkOrderCreatedConsumer>();
    x.AddConsumer<WorkOrderStatusChangedConsumer>();
    x.AddConsumer<PotholeSavedConsumer>();
    x.AddSmartCityOutbox<NotificationDbContext>();
});

builder.AddSmartCityHealthChecks();
builder.Services.AddGrpc();
builder.Services.AddSignalR();

var app = builder.Build();
app.MapSmartCityHealthChecks();
app.MapGrpcService<NotificationQueryGrpcService>();
app.MapHub<NotificationHub>("/hubs/notifications");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();
