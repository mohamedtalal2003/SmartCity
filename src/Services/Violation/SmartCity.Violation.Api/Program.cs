using MassTransit;
using Microsoft.EntityFrameworkCore;
using SmartCity.BuildingBlocks;
using SmartCity.Violation.Api.Consumers;
using SmartCity.Violation.Api.Data;

const string ServiceName = "Violation";

var builder = WebApplication.CreateBuilder(args);
builder.AddSmartCityLogging(ServiceName);
builder.ConfigureSmartCityKestrel();

builder.Services.AddDbContext<ViolationDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Db")));

builder.AddSmartCityBus(ServiceName, x =>
{
    x.AddConsumer<RawDetectionReceivedConsumer>();
    x.AddSmartCityOutbox<ViolationDbContext>();
});

builder.AddSmartCityHealthChecks();
builder.Services.AddGrpc();

var app = builder.Build();
app.MapSmartCityHealthChecks();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ViolationDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.Run();
