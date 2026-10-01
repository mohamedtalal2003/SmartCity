using MassTransit;
using Microsoft.EntityFrameworkCore;
using SmartCity.BuildingBlocks;
using SmartCity.CostCalculation.Api.Consumers;
using SmartCity.CostCalculation.Api.Data;
using SmartCity.CostCalculation.Api.Grpc;

const string ServiceName = "CostCalculation";

var builder = WebApplication.CreateBuilder(args);
builder.AddSmartCityLogging(ServiceName);
builder.ConfigureSmartCityKestrel();

builder.Services.AddDbContext<CostCalcDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Db")));

builder.AddSmartCityBus(ServiceName, x =>
{
    x.AddConsumer<PotholeSavedConsumer>();
    x.AddSmartCityOutbox<CostCalcDbContext>();
});

builder.AddSmartCityHealthChecks();
builder.Services.AddGrpc();

var app = builder.Build();
app.MapSmartCityHealthChecks();
app.MapGrpcService<CostQueryGrpcService>();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CostCalcDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();
