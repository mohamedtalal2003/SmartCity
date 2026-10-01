using MassTransit;
using Microsoft.EntityFrameworkCore;
using SmartCity.BuildingBlocks;
using SmartCity.Inventory.Api.Consumers;
using SmartCity.Inventory.Api.Data;

const string ServiceName = "Inventory";

var builder = WebApplication.CreateBuilder(args);
builder.AddSmartCityLogging(ServiceName);
builder.ConfigureSmartCityKestrel();

builder.Services.AddDbContext<InventoryDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Db")));

builder.AddSmartCityBus(ServiceName, x =>
{
    x.AddConsumer<RawDetectionReceivedConsumer>();
    x.AddSmartCityOutbox<InventoryDbContext>();
});

builder.AddSmartCityHealthChecks();
builder.Services.AddGrpc();

var app = builder.Build();
app.MapSmartCityHealthChecks();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.Run();
