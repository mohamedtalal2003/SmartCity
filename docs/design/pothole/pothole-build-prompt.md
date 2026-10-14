# Task: rebuild the Pothole service as a simple Onion service (first working slice)

You are working in the SmartCity repo (github.com/mohamedtalal2003/SmartCity), a .NET 8 walking skeleton.
Today `src/Services/Pothole/SmartCity.Pothole.Api` is one project with a CRUD-style stub: the consumer
writes the table directly and every detection makes a new row. Replace it with a small, clean Onion
service that does the real matching. Keep the rest of the system running end to end.

**The city is Istanbul, and it always was.** Konya was only an example in the early notes, by mistake.
Many files still say Konya (CLAUDE.md, README, docs, the simulator, tests, the coordinate check in
VehicleCommunication, the `District` enum). Step 0 below fixes all of it, except the Contracts.

## Where the attached files are
The owner put five files in `docs/design/pothole/`: this prompt, `pothole-design.en.html`,
`pothole-data-model.en.html`, `istanbul-districts.geojson` and `istanbul-neighborhoods.geojson`.
**Move** (not copy) the two `.geojson` files to `Infrastructure/Persistence/SeedData/` when you create
that project. Leave the prompt and the two pages where they are.

## Read first
1. `CLAUDE.md` (stack, invariants, ports, commands). It wins over everything else, except the city
   (Istanbul) and the decisions written in this prompt.
2. The two attached design pages: **Pothole Service Design** and **Pothole Service Data**
   (classes, tables, indexes). Follow them, except where this prompt narrows the scope.
3. The current code: `src/Services/Pothole/`, `src/BuildingBlocks/`, and
   `src/Services/WorkOrder/SmartCity.WorkOrder.Domain/` (an example of a domain project).

## Hard rules
- **Do not change anything in `src/SmartCity.Contracts` or the protos.** The team will change all
  contracts together later in one PR. Use the events and gRPC messages exactly as they exist today.
  If something is missing, write it in the final report under "Contract changes needed".
- Stack from `CLAUDE.md` only (MassTransit 8.x with the EF Core outbox and inbox, EF Core 8, Npgsql,
  NetTopologySuite, xUnit). The only new package allowed is `NetTopologySuite.IO.GeoJSON4STJ`
  (to read the area files). Do not add MediatR, AutoMapper, FluentValidation, a generic repository,
  the specification pattern, or any other framework.
- **Keep the code simple and easy to read.** The owner of this service is a student and must
  understand every file. No clever tricks, no extra layers, no patterns that are not asked for.
  If you think something extra is needed, write it in the report instead of building it.
- `dotnet build SmartCity.sln` must pass after every step. Work in small steps.
- Mark anything temporary with `// SKELETON:` and say what the real version will do.

## Step 0: Konya → Istanbul everywhere (do this first, as its own commit)
Find every "Konya" and every Konya place or coordinate in the repo (`grep -ri "konya\|meram\|selcuklu\|selçuklu\|karatay\|37\.8\|32\.4"`)
and switch it to Istanbul. Change only the city, not the meaning of the text.
- **Code:**
  - `tools/VehicleSimulator/Program.cs`: replace the three Konya loops with three Istanbul loops of
    6 points each (about 0.001° apart), centred on Kadıköy (40.985, 29.030), Beşiktaş (41.043, 29.006)
    and Fatih (41.012, 28.955). Rename the comments.
  - `src/Services/VehicleCommunication/.../Mqtt/MqttSubscriberService.cs`: the frame coordinate check
    uses the Konya box, so Istanbul frames are dropped. Change it to the Istanbul box
    (lat 40.80–41.60, lon 27.95–29.98) and the log text to "Istanbul". Nothing else in that service.
- **Tests:** coordinates in the tests become Istanbul ones (Kadıköy 40.985, 29.030). Important for
  `tests/SmartCity.EndToEnd.Tests/FrameToWorkOrderTests.cs`: its Konya point would now be dropped
  by the new box. Code that uses the enum value `District.Meram` stays as it is for now (the enum is
  in the Contracts; it changes in the contract PR).
- **Text:** `CLAUDE.md`, `README.md` and everything under `docs/`: Konya → Istanbul; the example
  districts Meram / Selçuklu / Karatay → Kadıköy / Beşiktaş / Fatih, with the coordinates above;
  example street names → real Istanbul ones (for example Bağdat Caddesi, Barbaros Bulvarı).
  Where a doc describes the old Konya bounding box, write the Istanbul box.
- **Do not touch** `src/SmartCity.Contracts` (the `District` enum, `enums.proto`, `mock-events.json`).
  List them under "Contract changes needed".
- When done, run the grep again: the only Konya names left must be inside `src/SmartCity.Contracts`
  and code that uses the `District` enum values.

## Target structure (4 projects + 1 test project)

```
src/Services/Pothole/
  SmartCity.Pothole.Domain/          # entities, rules. No EF, no MassTransit, no ASP.NET.
    Potholes/
      Pothole.cs                     # aggregate root
      Observation.cs                 # entity, append-only
      Detection.cs                   # value object: one sighting coming in
      GeoPoint.cs                    # value object: Latitude, Longitude
      PotholeStatus.cs  ClosedReason.cs  ObservationKind.cs  PotholeSource.cs
      DomainException.cs
    Estimation/
      PotholeEstimator.cs            # static class, pure math, no I/O
      PotholePrices.cs               # record: the prices it needs (filled from appsettings)
      CostEstimate.cs                # record: the result
  SmartCity.Pothole.Application/     # use cases. References Domain only.
    Abstractions/
      IPotholeRepository.cs          # the only data port
      IEventPublisher.cs             # the only messaging port
      AreaMatch.cs                   # record (int? DistrictId, int? NeighborhoodId)
    Detections/
      HandleDetection.cs             # use case: one AI detection in
      DetectionOptions.cs            # 3 m, 0.5, 30 days, depth thresholds (from appsettings)
    WorkOrders/
      ApplyWorkOrderUpdate.cs        # use case: ticket created / status changed
    DependencyInjection.cs           # AddPotholeApplication(configuration)
  SmartCity.Pothole.Infrastructure/  # EF Core, PostGIS, MassTransit publisher. References Application.
    Persistence/
      PotholeDbContext.cs
      Configurations/PotholeConfiguration.cs, ObservationConfiguration.cs, AdminAreaConfiguration.cs
      AdminArea.cs                   # plain lookup row (not a domain entity)
      PotholeRepository.cs
      AdminAreaSeeder.cs             # loads the Istanbul areas once
      SeedData/istanbul-districts.geojson, istanbul-neighborhoods.geojson
      Migrations/                    # new migration; drop the old one
    Messaging/
      MassTransitEventPublisher.cs
    DependencyInjection.cs           # AddPotholeInfrastructure(configuration)
  SmartCity.Pothole.Api/             # host. Consumers and gRPC are thin adapters.
    Consumers/
      RawDetectionReceivedConsumer.cs
      WorkOrderCreatedConsumer.cs
      WorkOrderStatusChangedConsumer.cs
    Grpc/
      RoadDamageQueryGrpcService.cs
    Program.cs                       # only wiring: AddPotholeApplication, AddPotholeInfrastructure, bus, gRPC, seeder
tests/SmartCity.Pothole.Domain.Tests/
```

### Layer rules (the compiler must enforce them through project references)
- Domain references nothing, except `SmartCity.Contracts` **for the shared enums only**
  (`SeverityLevel`, `DamageStage`, `DetectionType`). No events, no protos in Domain.
- Application references Domain. It may also use the Contracts events, because publishing
  `PotholeSaved` is part of the use case.
- Infrastructure references Application (and so Domain). Api references Application and Infrastructure.
- Nothing points outward: Domain never knows about EF, Application never knows about EF or MassTransit.

### Keep it simple
- Interfaces only where Infrastructure plugs in: `IPotholeRepository` and `IEventPublisher`.
  No interface for a class that has one implementation and no I/O.
- The repository is **specific to potholes**, with named methods for what the use cases need. No
  generic `IRepository<T>`. The methods:
  `ObservationExistsAsync(detectionId)`, `FindNearestOpenAsync(point, radiusM)`,
  `FindRecentFalsePositiveAsync(point, radiusM, since)`, `FindAreaAsync(point)` → `AreaMatch`,
  `GetByIdAsync(id)`, `Add(pothole)`, `SaveChangesAsync()`.
- A use case is one plain class with one public `Handle...Async` method. Register it as scoped.
- Use `TimeProvider` (built into .NET 8) for "now". No custom clock interface.
- Read side (gRPC queries) may query `PotholeDbContext` directly with `AsNoTracking()` and project to
  the proto messages. No repository for reads.
- One `DependencyInjection.cs` per layer is the only place that registers things.
- Use records for value objects, `private set` on entity properties, and a private constructor for EF.

## Cost estimate: Pothole has its own estimator
Decision: there is no separate Cost service in the target design, and **no shared calculator**.
Each service estimates its own repairs, because a pothole, a traffic sign and a building are repaired
in different ways. Only the shape of the result will be shared later, through the Contracts.
- `PotholeEstimator` is a **static class** in Domain:
  `public static CostEstimate Estimate(SeverityLevel severity, DamageStage stage, double? areaM2, double? depthCm, PotholePrices prices)`.
  Pure math, no I/O, no logging.
- `PotholePrices` (record) comes from the `appsettings` section `PotholePrices`, never from code.
  Application binds it with `IOptions<PotholePrices>` and passes it in. Defaults (the numbers the
  CostCalculation service uses today): total by severity Low 1000, Medium 1800, High 2500,
  Critical 4000 TRY; split materials 0.36, labor 0.48, equipment 0.16; currency "TRY".
- `CostEstimate` (record): `Materials`, `Labor`, `Equipment`, `Total`, `Currency`, `LaborHours`,
  `CrewSize`, `SuggestedPriority`, and one material line "Soğuk asfalt" with a quantity in m³.
  Copy the hours, crew size, priority and quantity rules from
  `CostCalculation/.../Consumers/PotholeSavedConsumer.cs` unchanged.
- For now `stage`, `areaM2` and `depthCm` are accepted but not used (`// SKELETON:` the real version
  prices by area and depth when the AI sends them; area stays empty until then).
- The `Pothole` calls the estimator itself: in `FromDetection`, and in `AddSighting` only when
  severity or stage changed. It stores the result on the pothole (`est_materials`, `est_labor`,
  `est_equipment`, `est_total`, `est_currency`).
- `PotholeSaved` has no estimate fields yet and the contracts are frozen, so **do not delete the
  CostCalculation service in this slice**: it keeps feeding Work Order until the contract PR.

## The domain (from the design, narrowed for this slice)

`Pothole` (aggregate root):
- Fields: `Id`, `Location` (GeoPoint), `DistrictId?`, `NeighborhoodId?`, `RoadName?`,
  `DamageType` (DetectionType: Pothole, RoadCrack, SurfaceDamage),
  `Status` (Open, Closed), `ClosedReason?` (Repaired, FalsePositive, Duplicate, ResolvedOtherwise),
  `ClosedAt?`, `DuplicateOfId?`, `CurrentSeverity` (SeverityLevel), `CurrentStage` (DamageStage),
  `AreaM2?`, `DepthCm?`, `Confidence`, `Source` (Ai, Officer, CitizenReport), `SightingsCount`,
  `Estimate` (owned value: materials, labor, equipment, total, currency),
  `WorkOrderId?`, `RepairStatus?` (plain string), `RepairStatusSeq` (long), `FirstSeenAt`, `LastSeenAt`,
  `RowVersion` (long, concurrency token), `Observations` (read-only list).
- `static Pothole FromDetection(Detection d, AreaMatch area, PotholePrices prices)` → Open,
  Source = Ai, SightingsCount = 1, first observation of kind `AiDetection`, estimate computed.
- `bool AddSighting(Detection d, PotholePrices prices)` → Open only (throws `DomainException` if Closed).
  Adds an `AiDetection` observation, updates last seen, size, depth, confidence and severity
  (latest reading), `SightingsCount++`.
  **Crack and pothole are one damage:** a crack and a pothole within the radius are the same record.
  `DamageType` and `CurrentStage` only move forward, never back: a crack that is later seen as a
  pothole becomes `Pothole`; a pothole later seen as a crack stays `Pothole`.
  Order for the type: RoadCrack / SurfaceDamage < Pothole. Order for the stage: the enum number.
  Recomputes the estimate only when severity or stage changed. Returns true when severity, stage
  or type changed (that is when we publish).
- `void AddSuppressedSighting(Detection d)` → the one thing allowed on a Closed pothole: an AI
  sighting near a pothole closed as FalsePositive in the last 30 days. Adds the observation only.
  Does not reopen, does not touch `SightingsCount` or the status.
- `void ApplyRepairStatus(Guid workOrderId, string status, long sequence)` → ignore if Closed;
  ignore if `sequence <= RepairStatusSeq`; else store the three values.
- `void Close(ClosedReason reason, Guid? duplicateOfId, DateTimeOffset at)` → if already Closed, do
  nothing. `Duplicate` needs `duplicateOfId` and it must not be its own id.
- Every method that changes state increases `RowVersion` by one.
- There is no method to reopen. Closed is final.

`Observation` (entity, never updated after insert): `Id`, `PotholeId`, `SourceDetectionId?`, `Kind`
(AiDetection, OfficerAdd, Correction, VerificationPass), `DamageType`, `Severity`, `Stage`, `AreaM2?`,
`DepthCm?`, `Confidence?`, `VerificationResult?` (string for now), `ImageUrl`, `Notes?`, `SeenAt`,
`CreatedBy`. Each sighting keeps its own type, so the timeline shows a crack turning into a pothole.

## Districts and neighborhoods (Istanbul)
- Table `admin_areas`: `id` (int), `osm_id` (text), `name` (text), `level` (District or Neighborhood,
  stored as string), `parent_id` (int, null for districts), `geometry` (`geometry(MultiPolygon,4326)`).
  GIST index on `geometry`.
- The two attached files go in `Infrastructure/Persistence/SeedData/` (copy to output):
  `istanbul-districts.geojson` (39 districts, OSM admin_level 6) and
  `istanbul-neighborhoods.geojson` (964 neighborhoods, OSM admin_level 8). Each feature has
  `osm_id`, `name`, `admin_level` and a Polygon or MultiPolygon. They come from OpenStreetMap.
- `AdminAreaSeeder` runs once at startup after migrations: if the table already has rows, do nothing.
  Otherwise read both files with `NetTopologySuite.IO.GeoJSON4STJ`, turn a Polygon into a
  MultiPolygon, set SRID 4326, insert, then set each neighborhood's parent with one SQL statement:
  the district that contains `ST_PointOnSurface(neighborhood.geometry)`. (Checked: every
  neighborhood falls inside exactly one district.)
- `FindAreaAsync(point)`: the district (level District) and the neighborhood (level Neighborhood)
  whose geometry intersects the point. If none is found (outside Istanbul or on the sea edge), the
  id stays **null**. Never use 0 or a fake id.
- Called only when a new pothole is created. The pothole stores `district_id` and `neighborhood_id`.

## Use case 1: `HandleDetection` (from `RawDetectionReceived`)
The consumer ignores messages whose `Type` is not road damage (`IsRoadDamage()`), builds a
`Detection` and calls the use case. The use case:
1. If an observation with this `DetectionId` already exists, stop (idempotent).
2. Find the **nearest Open pothole within 3 m** (`DetectionOptions.MatchRadiusMeters`, default 3;
   PostGIS `ST_DWithin` on `geography`, order by `ST_Distance`). Any road-damage type matches any
   other (crack and pothole are one damage). If found: `AddSighting`. If it returns true, publish
   `PotholeSaved` with `IsNewPothole = false`. Low-confidence detections may still be added here.
3. Else, if confidence is below 0.5 (`MinConfidenceForNew`), log and stop.
4. Else, if a pothole closed as `FalsePositive` in the last 30 days (`FalsePositiveSuppressionDays`)
   is within 3 m: `AddSuppressedSighting` on it. No event.
5. Else: `FindAreaAsync`, `Pothole.FromDetection`, add it, publish `PotholeSaved` with `IsNewPothole = true`.
6. Save once. The outbox commits the row and the event together.

Mapping today (existing fields only):
- Severity from `Metadata["estimated_depth_cm"]` with thresholds from config (keep the current
  numbers: < 4 Low, < 7 Medium, < 10 High, else Critical). `DepthCm` from the same key.
- `AreaM2` stays null (`// SKELETON:` the AI will measure it later).
- The AI does not send a stage yet. Guess it from the type: `Pothole` → `DamageStage.Pothole`,
  anything else → `DamageStage.Unspecified` (`// SKELETON:` the AI will send the stage).
- `PotholeSaved.District`: the contract enum only has old Konya names, so always send
  `District.Unspecified` (`// SKELETON:` after the contract PR, send the district id and name).
- Fill `PotholeSaved.DetectionType` with the pothole's `DamageType`, `DetectionCount` with
  `SightingsCount` and `ImageUrls` with the latest image. Copy `CorrelationId` and set
  `SourceDetectionId` (invariant 5).

## Use case 2: `ApplyWorkOrderUpdate` (from `WorkOrderCreated` and `WorkOrderStatusChanged`)
Only when `SourceEntityType` is RoadDamage. Load the pothole by `SourceEntityId`.
- `WorkOrderCreated` → `ApplyRepairStatus(workOrderId, "Pending", seq)`.
- `WorkOrderStatusChanged` → `ApplyRepairStatus(workOrderId, NewStatus, seq)`, then if `NewStatus` is
  `Completed`: `Close(Repaired)`.
- `seq`: the contract has no `ticket_sequence` yet. Use `OccurredAt.UtcTicks` for now
  (`// SKELETON:` switch to ticket_sequence after the contract PR).
- Pothole not found: log a warning and stop (no retry).

## Persistence
- Tables `potholes`, `observations` and `admin_areas` as on the data page (including `damage_type`,
  `district_id`, `neighborhood_id` and the `est_*` columns). Leave `recurring_spots` out of this slice.
- `row_version` is a concurrency token. A conflict throws, and MassTransit retries the message.
- Indexes: partial GIST on `location` where status is Open; partial GIST where closed_reason is
  FalsePositive; GIST on `admin_areas.geometry`; unique on `observations.source_detection_id` where
  not null; `(pothole_id, seen_at desc)` on observations; `(district_id, status)` on potholes;
  checks for Closed needs a reason and Duplicate needs `duplicate_of_id`.
- Enums stored as strings. Replace the old migration with a fresh `InitialCreate` (dev data can be
  dropped: `docker compose down -v`).
- Keep the outbox and inbox setup from BuildingBlocks (`AddSmartCityOutbox`, `AddSmartCityOutboxEntities`).

## gRPC (existing `RoadDamageQueryService`, no proto changes)
- `GetDamageById`, `ListDamages`: read from the new tables. The proto's `district` field is the old
  Konya enum: return `Unspecified`, and ignore the `districts` filter for now (`// SKELETON:`).
- `GetDamageTimeline`: implement it from `observations`, newest first.
- Fields the proto does not have yet (district and neighborhood names, damage type changes) go in
  the report, not in the proto.

## Tests (`tests/SmartCity.Pothole.Domain.Tests`, xUnit, no database)
At least:
- new detection makes an Open pothole with one observation and a filled estimate;
- a sighting with a higher severity returns true and a same-severity one returns false;
- a crack, then a pothole sighting on it → type and stage become Pothole, returns true;
- a pothole, then a crack sighting on it → stays Pothole;
- a closed pothole rejects `AddSighting`; `AddSuppressedSighting` does not reopen;
- `Close` twice is a no-op; `Duplicate` without an id throws;
- `ApplyRepairStatus` ignores an older sequence and ignores everything after Closed;
- `PotholeEstimator`: each severity gives the expected total and split; it uses the prices passed
  in (change a price → the total changes); missing area still gives an estimate.

## Out of scope for this slice (do not build)
Sending the estimate inside `PotholeSaved` and deleting the CostCalculation service (both wait for the
contract PR), officer commands (add, correct, merge), `DetectionDismissed`, cancel reasons, verification
passes, `recurring_spots` and its job, the lock on grid-square edges, map clustering. Other services'
docs and code: only the Konya → Istanbul change from Step 0, nothing else.

## Done when
1. `dotnet build SmartCity.sln` and `dotnet test SmartCity.sln` pass.
2. `docker compose up -d`, run the services, run
   `dotnet run --project tools/VehicleSimulator -- --vehicles 2 --interval-ms 2000`.
3. `admin_areas` has 39 districts and 964 neighborhoods, and every neighborhood has a parent.
4. The same spot seen several times gives **one** row in `potholes`, several rows in `observations`,
   and only one work order in Work Order.
5. A pothole from the Kadıköy loop has `district_id` pointing at Kadıköy and a `neighborhood_id`.
6. `GetDamageById` and `GetDamageTimeline` return that pothole and its sightings, and the pothole row
   has its `est_*` columns filled.
7. The Konya grep from Step 0 finds nothing outside the Contracts and the `District` enum uses.
8. Report back: what you built, the `// SKELETON:` list, and "Contract changes needed".

## Contract changes needed (already known; add anything you find)
- `PotholeSaved`: add the estimate fields and the close reason. After that, remove the CostCalculation service.
- `District` enum: has only Konya districts. Replace it with district / neighborhood ids and names
  (Istanbul has 39 districts and 964 neighborhoods, too many for an enum). Also in `queries.proto`
  and `mock-events.json`.
- `WorkOrderStatusChanged`: add `ticket_sequence`, the cancel reason and the duplicate id.
- `WorkOrderStatus`: add `Cancelled`.
- New events: `DetectionDismissed`, `VerificationRequested` / `VerificationEnded`, `RepairVerificationObserved`.
- `RawDetectionReceived`: add the damage stage and the area when the AI can send them.
