using MassTransit;
using SmartCity.AiDetection.Consumers;
using SmartCity.BuildingBlocks;

const string ServiceName = "AiDetection";

var builder = WebApplication.CreateBuilder(args);
builder.AddSmartCityLogging(ServiceName);
builder.ConfigureSmartCityKestrel();
builder.AddSmartCityBus(ServiceName, x =>
{
    x.AddConsumer<FrameReceivedConsumer>();
});
builder.AddSmartCityHealthChecks();

builder.Services.AddHttpClient("S3Download");

var app = builder.Build();
app.MapSmartCityHealthChecks();

app.Run();
