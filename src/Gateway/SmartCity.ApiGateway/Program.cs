using System.Text.Json;
using System.Text.Json.Serialization;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using SmartCity.BuildingBlocks;
using Smartcity.Commands.V1;
using Smartcity.Common.V1;
using Smartcity.Queries.V1;
using StackExchange.Redis;

const string ServiceName = "ApiGateway";

var builder = WebApplication.CreateBuilder(args);
builder.AddSmartCityLogging(ServiceName);
builder.ConfigureSmartCityKestrel();
builder.AddSmartCityHealthChecks();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var grpcSection = builder.Configuration.GetSection("GrpcServices");

RegisterGrpcClient<RoadDamageQueryService.RoadDamageQueryServiceClient>(builder, grpcSection["Pothole"]!);
RegisterGrpcClient<CostQueryService.CostQueryServiceClient>(builder, grpcSection["CostCalculation"]!);
RegisterGrpcClient<WorkOrderQueryService.WorkOrderQueryServiceClient>(builder, grpcSection["WorkOrder"]!);
RegisterGrpcClient<WorkOrderCommandService.WorkOrderCommandServiceClient>(builder, grpcSection["WorkOrder"]!);
RegisterGrpcClient<NotificationQueryService.NotificationQueryServiceClient>(builder, grpcSection["Notification"]!);

var redisCs = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisCs));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapSmartCityHealthChecks();

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Converters = { new JsonStringEnumConverter() },
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};

// ─── Work Orders (queries) ────────────────────────────────────
app.MapGet("/api/work-orders", async (
    [FromQuery] string? status,
    [FromQuery] string? district,
    [FromQuery] bool? overdueOnly,
    [FromQuery] int? page,
    [FromQuery] int? pageSize,
    WorkOrderQueryService.WorkOrderQueryServiceClient client) =>
{
    var request = new ListWorkOrdersRequest
    {
        OverdueOnly = overdueOnly ?? false,
        Pagination = new PaginationRequest
        {
            PageSize = pageSize ?? 20,
            PageToken = page?.ToString() ?? ""
        }
    };

    if (!string.IsNullOrEmpty(status))
    {
        if (Enum.TryParse<Smartcity.Enums.V1.WorkOrderStatus>(status, true, out var s))
            request.Statuses.Add(s);
    }

    if (!string.IsNullOrEmpty(district))
    {
        if (Enum.TryParse<Smartcity.Enums.V1.District>(district, true, out var d))
            request.Districts.Add(d);
    }

    return await CallGrpc(async () =>
    {
        var response = await client.ListWorkOrdersAsync(request);
        return Results.Ok(response);
    });
});

app.MapGet("/api/work-orders/{id}", async (string id, WorkOrderQueryService.WorkOrderQueryServiceClient client) =>
{
    return await CallGrpc(async () =>
    {
        var response = await client.GetWorkOrderByIdAsync(new GetWorkOrderRequest { WorkOrderId = id });
        return Results.Ok(response);
    });
});

// ─── Work Orders (commands) ───────────────────────────────────
app.MapPost("/api/work-orders/{id}/assign", async (
    string id,
    HttpContext ctx,
    WorkOrderCommandService.WorkOrderCommandServiceClient client) =>
{
    if (!TryGetIdempotencyKey(ctx, out var key))
        return Results.BadRequest(new { error = "Missing Idempotency-Key header" });

    return await CallGrpcCommand(async () =>
    {
        var body = await ctx.Request.ReadFromJsonAsync<AssignCrewBody>(jsonOptions);
        var response = await client.AssignCrewAsync(new AssignCrewRequest
        {
            WorkOrderId = id,
            CrewId = body?.CrewId ?? "",
            Metadata = BuildMetadata(ctx, key),
        });
        return MapCommandResponse(response);
    });
});

app.MapPost("/api/work-orders/{id}/en-route", async (string id, HttpContext ctx,
    WorkOrderCommandService.WorkOrderCommandServiceClient client) =>
{
    if (!TryGetIdempotencyKey(ctx, out var key))
        return Results.BadRequest(new { error = "Missing Idempotency-Key header" });

    return await CallGrpcCommand(async () =>
    {
        var response = await client.MarkEnRouteAsync(new WorkOrderActionRequest
        {
            WorkOrderId = id,
            Metadata = BuildMetadata(ctx, key),
        });
        return MapCommandResponse(response);
    });
});

app.MapPost("/api/work-orders/{id}/on-site", async (string id, HttpContext ctx,
    WorkOrderCommandService.WorkOrderCommandServiceClient client) =>
{
    if (!TryGetIdempotencyKey(ctx, out var key))
        return Results.BadRequest(new { error = "Missing Idempotency-Key header" });

    return await CallGrpcCommand(async () =>
    {
        var response = await client.MarkOnSiteAsync(new WorkOrderActionRequest
        {
            WorkOrderId = id,
            Metadata = BuildMetadata(ctx, key),
        });
        return MapCommandResponse(response);
    });
});

app.MapPost("/api/work-orders/{id}/pause", async (string id, HttpContext ctx,
    WorkOrderCommandService.WorkOrderCommandServiceClient client) =>
{
    if (!TryGetIdempotencyKey(ctx, out var key))
        return Results.BadRequest(new { error = "Missing Idempotency-Key header" });

    return await CallGrpcCommand(async () =>
    {
        var body = await ctx.Request.ReadFromJsonAsync<PauseBody>(jsonOptions);
        var reason = Enum.TryParse<Smartcity.Enums.V1.PauseReason>(body?.Reason ?? "Other", true, out var r)
            ? r : Smartcity.Enums.V1.PauseReason.Other;

        var response = await client.PauseWorkAsync(new PauseWorkRequest
        {
            WorkOrderId = id,
            Reason = reason,
            Metadata = BuildMetadata(ctx, key),
        });
        return MapCommandResponse(response);
    });
});

app.MapPost("/api/work-orders/{id}/resume", async (string id, HttpContext ctx,
    WorkOrderCommandService.WorkOrderCommandServiceClient client) =>
{
    if (!TryGetIdempotencyKey(ctx, out var key))
        return Results.BadRequest(new { error = "Missing Idempotency-Key header" });

    return await CallGrpcCommand(async () =>
    {
        var response = await client.ResumeWorkAsync(new WorkOrderActionRequest
        {
            WorkOrderId = id,
            Metadata = BuildMetadata(ctx, key),
        });
        return MapCommandResponse(response);
    });
});

app.MapPost("/api/work-orders/{id}/complete", async (string id, HttpContext ctx,
    WorkOrderCommandService.WorkOrderCommandServiceClient client) =>
{
    if (!TryGetIdempotencyKey(ctx, out var key))
        return Results.BadRequest(new { error = "Missing Idempotency-Key header" });

    return await CallGrpcCommand(async () =>
    {
        var body = await ctx.Request.ReadFromJsonAsync<CompleteBody>(jsonOptions);
        var response = await client.SubmitCompletionProofAsync(new SubmitCompletionProofRequest
        {
            WorkOrderId = id,
            AfterImageUrl = body?.ProofImageUrl ?? "",
            Metadata = BuildMetadata(ctx, key),
        });
        return MapCommandResponse(response);
    });
});

// ─── Potholes ─────────────────────────────────────────────────
app.MapGet("/api/potholes/{id}", async (string id, RoadDamageQueryService.RoadDamageQueryServiceClient client) =>
{
    return await CallGrpc(async () =>
    {
        var response = await client.GetDamageByIdAsync(new GetDamageRequest { DamageId = id });
        return Results.Ok(response);
    });
});

app.MapGet("/api/potholes", async ([FromQuery] string? bbox, RoadDamageQueryService.RoadDamageQueryServiceClient client) =>
{
    return await CallGrpc(async () =>
    {
        var request = new ListDamagesRequest();
        if (!string.IsNullOrEmpty(bbox))
        {
            var parts = bbox.Split(',');
            if (parts.Length == 4 &&
                double.TryParse(parts[0], out var minLon) &&
                double.TryParse(parts[1], out var minLat) &&
                double.TryParse(parts[2], out var maxLon) &&
                double.TryParse(parts[3], out var maxLat))
            {
                request.MapArea = new BoundingBox
                {
                    SouthWest = new GeoCoordinates { Latitude = minLat, Longitude = minLon },
                    NorthEast = new GeoCoordinates { Latitude = maxLat, Longitude = maxLon }
                };
            }
        }

        var response = await client.ListDamagesAsync(request);

        var features = response.Records.Select(r => new
        {
            type = "Feature",
            geometry = new
            {
                type = "Point",
                coordinates = new[] { r.Location.Longitude, r.Location.Latitude }
            },
            properties = new
            {
                damageId = r.DamageId,
                stage = r.Stage.ToString(),
                district = r.District.ToString(),
                roadName = r.RoadName,
                latestImageUrl = r.LatestImageUrl,
                detectionCount = r.DetectionCount,
            }
        });

        return Results.Json(new
        {
            type = "FeatureCollection",
            features
        }, jsonOptions);
    });
});

// ─── Vehicles (live from Redis) ───────────────────────────────
app.MapGet("/api/vehicles/live", async (IConnectionMultiplexer redis) =>
{
    var server = redis.GetServers()[0];
    var db = redis.GetDatabase();
    var vehicles = new List<object>();

    await foreach (var key in server.KeysAsync(pattern: "vehicle:pos:*"))
    {
        var entries = await db.HashGetAllAsync(key);
        if (entries.Length == 0) continue;

        var dict = entries.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
        var vehicleId = key.ToString().Replace("vehicle:pos:", "");
        vehicles.Add(new
        {
            vehicleId,
            latitude = double.TryParse(dict.GetValueOrDefault("lat"), out var lat) ? lat : 0,
            longitude = double.TryParse(dict.GetValueOrDefault("lon"), out var lon) ? lon : 0,
            speed = double.TryParse(dict.GetValueOrDefault("speed"), out var spd) ? spd : 0,
            heading = double.TryParse(dict.GetValueOrDefault("heading"), out var hdg) ? hdg : 0,
            capturedAt = dict.GetValueOrDefault("at"),
        });
    }

    return Results.Ok(vehicles);
});

// ─── Cost Estimates ───────────────────────────────────────────
app.MapGet("/api/estimates/by-entity/{entityId}", async (
    string entityId,
    CostQueryService.CostQueryServiceClient client) =>
{
    return await CallGrpc(async () =>
    {
        var response = await client.GetEstimateForEntityAsync(new GetEstimateRequest
        {
            SourceEntityId = entityId,
        });
        return Results.Ok(response);
    });
});

app.Run();

// ─── Helpers ──────────────────────────────────────────────────
static void RegisterGrpcClient<TClient>(WebApplicationBuilder builder, string address)
    where TClient : class
{
    builder.Services
        .AddGrpcClient<TClient>(o =>
        {
            o.Address = new Uri(address);
        })
        .ConfigureChannel(ch =>
        {
            ch.HttpHandler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true,
            };
        })
        .AddResilienceHandler("grpc", pipeline =>
        {
            pipeline.AddTimeout(TimeSpan.FromSeconds(10));
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(500),
                ShouldHandle = static args => ValueTask.FromResult(
                    args.Outcome.Exception is RpcException rpc
                        && rpc.StatusCode == StatusCode.Unavailable),
            });
            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(30),
            });
        });
}

static bool TryGetIdempotencyKey(HttpContext ctx, out string key)
{
    key = ctx.Request.Headers["Idempotency-Key"].ToString();
    return !string.IsNullOrWhiteSpace(key);
}

static CommandMetadata BuildMetadata(HttpContext ctx, string idempotencyKey) => new()
{
    IdempotencyKey = idempotencyKey,
    SubmittedBy = ctx.Request.Headers["X-User-Id"].ToString() is { Length: > 0 } uid
        ? uid : "dev-officer",
    SubmittedAtUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    CorrelationId = Guid.NewGuid().ToString(),
};

static IResult MapCommandResponse(CommandResponse response)
{
    if (response.Success)
        return Results.Ok(response);

    return response.ErrorCode switch
    {
        ErrorCode.Duplicate => Results.Ok(response),
        ErrorCode.NotFound => Results.NotFound(new { error = response.Message, response.EntityId }),
        ErrorCode.InvalidState => Results.Conflict(new { error = response.Message, response.EntityId }),
        ErrorCode.Validation => Results.BadRequest(new { error = response.Message }),
        ErrorCode.AuthFailed => Results.Json(new { error = response.Message }, statusCode: 403),
        _ => Results.Json(new { error = response.Message ?? "Internal server error" }, statusCode: 500),
    };
}

static async Task<IResult> CallGrpc(Func<Task<IResult>> call)
{
    try
    {
        return await call();
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
    {
        return Results.NotFound(new { error = ex.Status.Detail });
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
    {
        return Results.BadRequest(new { error = ex.Status.Detail });
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
    {
        return Results.Json(new { error = "Service unavailable" }, statusCode: 503);
    }
    catch (Polly.CircuitBreaker.BrokenCircuitException)
    {
        return Results.Json(new { error = "Service unavailable (circuit open)" }, statusCode: 503);
    }
    catch (HttpRequestException ex) when (ex.InnerException is Polly.CircuitBreaker.BrokenCircuitException)
    {
        return Results.Json(new { error = "Service unavailable (circuit open)" }, statusCode: 503);
    }
}

static async Task<IResult> CallGrpcCommand(Func<Task<IResult>> call)
{
    try
    {
        return await call();
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
    {
        return Results.NotFound(new { error = ex.Status.Detail });
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
    {
        return Results.BadRequest(new { error = ex.Status.Detail });
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
    {
        return Results.Json(new { error = "Service unavailable" }, statusCode: 503);
    }
    catch (RpcException ex)
    {
        return Results.Json(new { error = ex.Status.Detail ?? "Internal server error" }, statusCode: 500);
    }
    catch (Polly.CircuitBreaker.BrokenCircuitException)
    {
        return Results.Json(new { error = "Service unavailable (circuit open)" }, statusCode: 503);
    }
    catch (HttpRequestException ex) when (ex.InnerException is Polly.CircuitBreaker.BrokenCircuitException)
    {
        return Results.Json(new { error = "Service unavailable (circuit open)" }, statusCode: 503);
    }
}

// ─── Request bodies ───────────────────────────────────────────
record AssignCrewBody(string CrewId);
record PauseBody(string Reason);
record CompleteBody(string ProofImageUrl);
