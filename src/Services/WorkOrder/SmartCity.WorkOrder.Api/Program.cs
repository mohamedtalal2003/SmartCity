using MassTransit;
using Microsoft.EntityFrameworkCore;
using SmartCity.BuildingBlocks;
using SmartCity.WorkOrder.Api.Consumers;
using SmartCity.WorkOrder.Api.Data;
using SmartCity.WorkOrder.Api.Grpc;

const string ServiceName = "WorkOrder";

var builder = WebApplication.CreateBuilder(args);
builder.AddSmartCityLogging(ServiceName);
builder.ConfigureSmartCityKestrel();

builder.Services.AddDbContext<WorkOrderDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Db")));

builder.AddSmartCityBus(ServiceName, x =>
{
    x.AddConsumer<PotholeSavedConsumer>();
    x.AddConsumer<CostEstimateGeneratedConsumer>();
    x.AddSmartCityOutbox<WorkOrderDbContext>();
});

builder.AddSmartCityHealthChecks();
builder.Services.AddGrpc();

var app = builder.Build();
app.MapSmartCityHealthChecks();
app.MapGrpcService<WorkOrderQueryGrpcService>();
app.MapGrpcService<WorkOrderCommandGrpcService>();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WorkOrderDbContext>();
    await db.Database.MigrateAsync();

    await db.Database.ExecuteSqlRawAsync(
        "CREATE SEQUENCE IF NOT EXISTS work_order_ticket_seq START 1");
}

app.Run();
