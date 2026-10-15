using System.Globalization;
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
RegisterGrpcClient<PotholeCommandService.PotholeCommandServiceClient>(builder, grpcSection["Pothole"]!);
RegisterGrpcClient<AreaQueryService.AreaQueryServiceClient>(builder, grpcSection["Pothole"]!);
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

    // district = an Istanbul district id (from the Pothole service's admin areas).
    if (!string.IsNullOrEmpty(district))
    {
        if (int.TryParse(district, out var districtId))
            request.DistrictIds.Add(districtId);
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

// GET /api/potholes?district=13&minSeverity=High&sort=mostSevere&page=<nextPage>&pageSize=20
// Filter names as on the Pothole design page (P12). A list filter takes several values:
// district=13&district=14 or district=13,14.
app.MapGet("/api/potholes", async (HttpRequest http, RoadDamageQueryService.RoadDamageQueryServiceClient client) =>
{
    return await CallGrpc(async () =>
    {
        var request = BuildDamageFilters(http.Query);
        request.Pagination = new PaginationRequest
        {
            PageSize = int.TryParse(http.Query["pageSize"], out var pageSize) ? pageSize : 20,
            PageToken = http.Query["page"].ToString()
        };
        if (Enum.TryParse<Smartcity.Enums.V1.DamageSort>(http.Query["sort"], true, out var sort))
            request.Sort = sort;

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
                district = r.DistrictName,
                roadName = r.RoadName,
                latestImageUrl = r.LatestImageUrl,
                detectionCount = r.DetectionCount,
                districtId = r.DistrictId,
                neighborhoodId = r.NeighborhoodId,
                neighborhood = r.NeighborhoodName,
                status = r.Status.ToString(),
                closedReason = r.ClosedReason.ToString(),
                closedAtUtc = r.ClosedAtUtc,
                duplicateOfId = r.DuplicateOfId,
                severity = r.Severity.ToString(),
                damageType = r.DamageType.ToString(),
                areaM2 = r.AreaM2,
                depthCm = r.DepthCm,
                confidence = r.Confidence,
                source = r.Source.ToString(),
                estimatedTotal = r.EstimatedTotal,
                estimatedMaterials = r.EstimatedMaterials,
                estimatedLabor = r.EstimatedLabor,
                estimatedEquipment = r.EstimatedEquipment,
                currency = r.Currency,
                workOrderId = r.WorkOrderId,
                activeWorkOrderId = r.ActiveWorkOrderId,
                repairStatus = r.RepairStatus,
                firstSeenUtc = r.DetectionTimestampUtc,
                lastSeenUtc = r.LastSeenTimestampUtc,
            }
        });

        return Results.Json(new
        {
            type = "FeatureCollection",
            features,
            totalCount = response.Pagination.TotalCount,
            nextPage = response.Pagination.NextPageToken,
        }, jsonOptions);
    });
});

// GET /api/potholes/map?bbox=minLon,minLat,maxLon,maxLat&zoom=12 (+ the list filters)
app.MapGet("/api/potholes/map", async (HttpRequest http, RoadDamageQueryService.RoadDamageQueryServiceClient client) =>
{
    return await CallGrpc(async () =>
    {
        var filters = BuildDamageFilters(http.Query);
        var response = await client.GetDamagesOnMapAsync(new DamagesOnMapRequest
        {
            MapArea = filters.MapArea,
            Zoom = int.TryParse(http.Query["zoom"], out var zoom) ? zoom : 12,
            Filters = filters,
        });
        return Results.Json(response, jsonOptions);
    });
});

app.MapGet("/api/potholes/{id}/timeline", async (
    string id,
    [FromQuery] string? page,
    [FromQuery] int? pageSize,
    RoadDamageQueryService.RoadDamageQueryServiceClient client) =>
{
    return await CallGrpc(async () =>
    {
        var response = await client.GetDamageTimelineAsync(new GetDamageTimelineRequest
        {
            DamageId = id,
            Pagination = new PaginationRequest { PageSize = pageSize ?? 20, PageToken = page ?? "" },
        });
        return Results.Json(response, jsonOptions);
    });
});

// ─── Potholes (commands) ──────────────────────────────────────
// 201 when a new pothole was made, 200 when the report was added to an open pothole within 3 m.
app.MapPost("/api/potholes", async (HttpContext ctx, PotholeCommandService.PotholeCommandServiceClient client) =>
{
    if (!TryGetIdempotencyKey(ctx, out var key))
        return Results.BadRequest(new { error = "Missing Idempotency-Key header" });

    return await CallGrpcCommand(async () =>
    {
        var body = await ctx.Request.ReadFromJsonAsync<AddPotholeBody>(jsonOptions);
        if (body is null)
            return Results.BadRequest(new { error = "Missing body" });

        var request = new AddPotholeRequest
        {
            Location = new GeoCoordinates { Latitude = body.Latitude, Longitude = body.Longitude },
            ImageUrl = body.ImageUrl ?? "",
            AreaM2 = body.AreaM2 ?? 0,
            DepthCm = body.DepthCm ?? 0,
            Source = Enum.TryParse<Smartcity.Enums.V1.PotholeSource>(body.Source, true, out var source)
                ? source : Smartcity.Enums.V1.PotholeSource.Officer,
            Notes = body.Notes ?? "",
            Metadata = BuildMetadata(ctx, key),
        };
        if (!string.IsNullOrEmpty(body.Severity))
        {
            if (!Enum.TryParse<Smartcity.Enums.V1.SeverityLevel>(body.Severity, true, out var severity))
                return Results.BadRequest(new { error = $"Unknown severity '{body.Severity}'" });
            request.Severity = severity;
        }

        var response = await client.AddPotholeAsync(request);
        return MapPotholeCommandResponse(response);
    });
});

app.MapPost("/api/potholes/{id}/corrections", async (string id, HttpContext ctx,
    PotholeCommandService.PotholeCommandServiceClient client) =>
{
    if (!TryGetIdempotencyKey(ctx, out var key))
        return Results.BadRequest(new { error = "Missing Idempotency-Key header" });

    return await CallGrpcCommand(async () =>
    {
        var body = await ctx.Request.ReadFromJsonAsync<CorrectionBody>(jsonOptions);
        if (body is null)
            return Results.BadRequest(new { error = "Missing body" });

        var request = new CorrectPotholeRequest
        {
            PotholeId = id,
            Reason = body.Reason ?? "",
            Metadata = BuildMetadata(ctx, key),
        };
        if (!string.IsNullOrEmpty(body.Severity))
        {
            if (!Enum.TryParse<Smartcity.Enums.V1.SeverityLevel>(body.Severity, true, out var severity))
                return Results.BadRequest(new { error = $"Unknown severity '{body.Severity}'" });
            request.Severity = severity;
        }
        if (!string.IsNullOrEmpty(body.Stage))
        {
            if (!Enum.TryParse<Smartcity.Enums.V1.DamageStage>(body.Stage, true, out var stage))
                return Results.BadRequest(new { error = $"Unknown stage '{body.Stage}'" });
            request.Stage = stage;
        }
        if (body.AreaM2 is not null) request.AreaM2 = body.AreaM2.Value;
        if (body.DepthCm is not null) request.DepthCm = body.DepthCm.Value;

        var response = await client.CorrectPotholeAsync(request);
        return MapPotholeCommandResponse(response);
    });
});

// ─── Areas (Istanbul districts and neighborhoods) ─────────────
// GET /api/areas = the districts; GET /api/areas?parentId=13 = the neighborhoods of district 13.
app.MapGet("/api/areas", async ([FromQuery] int? parentId, AreaQueryService.AreaQueryServiceClient client) =>
{
    return await CallGrpc(async () =>
    {
        var request = new ListAreasRequest();
        if (parentId is not null)
            request.ParentId = parentId.Value;

        var response = await client.ListAreasAsync(request);
        return Results.Json(response.Areas, jsonOptions);
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

// Only the pothole command routes look at `created`; Work Order keeps MapCommandResponse as it is.
static IResult MapPotholeCommandResponse(CommandResponse response)
{
    if (response.Success && response.Created)
        return Results.Created($"/api/potholes/{response.EntityId}", response);
    return MapCommandResponse(response);
}

// The pothole list and map filters from the query string (names from the Pothole design page, P12).
static ListDamagesRequest BuildDamageFilters(IQueryCollection query)
{
    var request = new ListDamagesRequest();

    // Place
    request.DistrictIds.AddRange(QueryInts(query, "district"));
    request.NeighborhoodIds.AddRange(QueryInts(query, "neighborhood"));
    request.RoadName = query["road"].ToString();
    var bbox = query["bbox"].ToString().Split(',');
    if (bbox.Length == 4 &&
        double.TryParse(bbox[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var minLon) &&
        double.TryParse(bbox[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var minLat) &&
        double.TryParse(bbox[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var maxLon) &&
        double.TryParse(bbox[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var maxLat))
    {
        request.MapArea = new BoundingBox
        {
            SouthWest = new GeoCoordinates { Latitude = minLat, Longitude = minLon },
            NorthEast = new GeoCoordinates { Latitude = maxLat, Longitude = maxLon }
        };
    }

    // State
    request.Statuses.AddRange(QueryEnums<Smartcity.Enums.V1.PotholeStatus>(query, "status"));
    request.ClosedReasons.AddRange(QueryEnums<Smartcity.Enums.V1.ClosedReason>(query, "closedReason"));
    request.RepairStatuses.AddRange(QueryList(query, "repairStatus"));
    // SKELETON: the proto only has "without an active work order", so hasOpenWorkOrder=true is not applied.
    if (bool.TryParse(query["hasOpenWorkOrder"], out var hasOpenWorkOrder) && !hasOpenWorkOrder)
        request.WithoutActiveWorkOrder = true;

    // The pothole
    request.Stages.AddRange(QueryEnums<Smartcity.Enums.V1.DamageStage>(query, "stage"));
    if (Enum.TryParse<Smartcity.Enums.V1.DamageStage>(query["minStage"], true, out var minStage))
        request.MinStage = minStage;
    if (Enum.TryParse<Smartcity.Enums.V1.SeverityLevel>(query["minSeverity"], true, out var minSeverity))
        request.MinSeverity = minSeverity;
    request.MinAreaM2 = QueryDouble(query, "minArea");
    request.MaxAreaM2 = QueryDouble(query, "maxArea");
    request.MinDepthCm = QueryDouble(query, "minDepth");
    request.MaxDepthCm = QueryDouble(query, "maxDepth");
    request.MinConfidence = QueryDouble(query, "minConfidence");

    // Money and source
    request.MinCost = QueryDouble(query, "minCost");
    request.MaxCost = QueryDouble(query, "maxCost");
    request.Sources.AddRange(QueryEnums<Smartcity.Enums.V1.PotholeSource>(query, "source"));
    request.MinSightings = int.TryParse(query["minSightings"], out var minSightings) ? minSightings : 0;

    // Time (ISO dates, UTC when no offset is given)
    request.TimeRange = QueryTimeRange(query, "firstSeenFrom", "firstSeenTo");
    request.LastSeen = QueryTimeRange(query, "lastSeenFrom", "lastSeenTo");
    request.Closed = QueryTimeRange(query, "closedFrom", "closedTo");

    return request;
}

// "a,b" and "x=a&x=b" both give [a, b].
static IEnumerable<string> QueryList(IQueryCollection query, string name) =>
    query[name].SelectMany(v => (v ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

static IEnumerable<int> QueryInts(IQueryCollection query, string name) =>
    QueryList(query, name).Select(v => int.TryParse(v, out var x) ? x : 0).Where(x => x > 0);

static IEnumerable<TEnum> QueryEnums<TEnum>(IQueryCollection query, string name) where TEnum : struct, Enum =>
    QueryList(query, name)
        .Select(v => Enum.TryParse<TEnum>(v, true, out var e) ? e : default)
        .Where(e => !e.Equals(default(TEnum)));

static double QueryDouble(IQueryCollection query, string name) =>
    double.TryParse(query[name], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;

static TimeRange? QueryTimeRange(IQueryCollection query, string fromName, string toName)
{
    var from = QueryEpochMs(query, fromName);
    var to = QueryEpochMs(query, toName);
    return from == 0 && to == 0 ? null : new TimeRange { FromUtc = from, ToUtc = to };
}

static long QueryEpochMs(IQueryCollection query, string name) =>
    DateTimeOffset.TryParse(query[name], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
        ? at.ToUnixTimeMilliseconds() : 0;

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
record AddPotholeBody(double Latitude, double Longitude, string? ImageUrl, string? Severity, double? AreaM2,
    double? DepthCm, string? Source, string? Notes);
record CorrectionBody(string? Severity, string? Stage, double? AreaM2, double? DepthCm, string? Reason);
