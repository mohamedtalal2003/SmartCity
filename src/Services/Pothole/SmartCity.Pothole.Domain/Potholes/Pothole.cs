using SmartCity.Contracts.Enums;
using SmartCity.Pothole.Domain.Estimation;

namespace SmartCity.Pothole.Domain.Potholes;

/// <summary>
/// The pothole (aggregate root). One row per pothole with its current state; every sighting is an
/// <see cref="Observation"/> saved with it in the same transaction.
///
/// A crack and a pothole in the same place are one damage record. Its type and stage only move
/// forward (crack → pothole), never back, unless an officer corrects them. Closed is final: there is
/// no method to reopen.
/// Every method that changes state increases <see cref="RowVersion"/> by one, so two writers on the
/// same pothole at the same moment cannot both win.
///
/// Note for other projects: <c>SmartCity.Pothole</c> is also a namespace, so a file in
/// <c>SmartCity.Pothole.*</c> writes <c>using SmartCity.Pothole.Domain.Potholes;</c> after its
/// <c>namespace</c> line. Otherwise the compiler finds the namespace before this class.
/// </summary>
public sealed class Pothole
{
    // Observations are not loaded when we run a command. This list only holds the rows added during
    // the current call; EF inserts them on save.
    private readonly List<Observation> _observations = new();

    private Pothole() { } // for EF

    public Guid Id { get; private set; }
    public GeoPoint Location { get; private set; } = default!;
    public int? DistrictId { get; private set; }
    public int? NeighborhoodId { get; private set; }
    public string? RoadName { get; private set; }
    public DetectionType DamageType { get; private set; }

    public PotholeStatus Status { get; private set; }
    public ClosedReason? ClosedReason { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public Guid? DuplicateOfId { get; private set; }

    public SeverityLevel CurrentSeverity { get; private set; }
    public DamageStage CurrentStage { get; private set; }
    public double? AreaM2 { get; private set; }
    public double? DepthCm { get; private set; }
    public double Confidence { get; private set; }          // of the latest detection
    public PotholeSource Source { get; private set; }
    public int SightingsCount { get; private set; }         // AI detections only
    public CostEstimate Estimate { get; private set; } = default!;

    // Display-only copy of the Work Order ticket. No rule in Pothole depends on it.
    public Guid? WorkOrderId { get; private set; }
    public string? RepairStatus { get; private set; }
    public long RepairStatusSeq { get; private set; }

    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public long RowVersion { get; private set; }

    public IReadOnlyList<Observation> Observations => _observations;

    /// <summary>A new Open pothole from an AI detection that had no open pothole nearby.</summary>
    public static Pothole FromDetection(Detection d, int? districtId, int? neighborhoodId, PotholePrices prices)
    {
        if (!d.DamageType.IsRoadDamage())
            throw new DomainException($"{d.DamageType} is not road damage.");

        var pothole = new Pothole
        {
            Id = Guid.NewGuid(),
            Location = d.Location,
            DistrictId = districtId,
            NeighborhoodId = neighborhoodId,
            DamageType = d.DamageType,
            Status = PotholeStatus.Open,
            CurrentSeverity = d.Severity,
            CurrentStage = d.Stage,
            AreaM2 = d.AreaM2,
            DepthCm = d.DepthCm,
            Confidence = d.Confidence,
            Source = PotholeSource.Ai,
            SightingsCount = 1,
            Estimate = PotholeEstimator.Estimate(d.Severity, d.Stage, d.AreaM2, d.DepthCm, prices),
            FirstSeenAt = d.SeenAt,
            LastSeenAt = d.SeenAt
        };

        pothole._observations.Add(Observation.FromDetection(pothole.Id, d));
        return pothole;
    }

    /// <summary>
    /// A new Open pothole reported by a person. No confidence check and no 30-day rule: a person saw it.
    /// <see cref="SightingsCount"/> stays 0 because it counts AI detections only.
    /// </summary>
    public static Pothole AddedByOfficer(OfficerReport r, int? districtId, int? neighborhoodId, PotholePrices prices)
    {
        if (r.Source == PotholeSource.Ai)
            throw new DomainException("An officer report must have the source Officer or CitizenReport.");
        CheckSize(r.AreaM2, r.DepthCm);

        var pothole = new Pothole
        {
            Id = Guid.NewGuid(),
            Location = r.Location,
            DistrictId = districtId,
            NeighborhoodId = neighborhoodId,
            // The officer command adds a pothole, so the type and stage are Pothole.
            DamageType = DetectionType.Pothole,
            Status = PotholeStatus.Open,
            CurrentSeverity = r.Severity,
            CurrentStage = DamageStage.Pothole,
            AreaM2 = r.AreaM2,
            DepthCm = r.DepthCm,
            // SKELETON: Confidence is the AI confidence of the latest detection; a person's report has none,
            // so it is 1 (fully sure). The design leaves this open.
            Confidence = 1.0,
            Source = r.Source,
            SightingsCount = 0,
            Estimate = PotholeEstimator.Estimate(r.Severity, DamageStage.Pothole, r.AreaM2, r.DepthCm, prices),
            FirstSeenAt = r.At,
            LastSeenAt = r.At
        };

        pothole._observations.Add(Observation.FromOfficerReport(pothole.Id, r));
        return pothole;
    }

    /// <summary>
    /// A person reported this open pothole again. Only the observation (with its photo) is saved;
    /// nothing else changes, so no event goes out.
    /// </summary>
    public void AddReport(OfficerReport r)
    {
        if (Status == PotholeStatus.Closed)
            throw new DomainException($"Pothole {Id} is closed. A closed pothole does not take new reports.");

        _observations.Add(Observation.FromOfficerReport(Id, r));
        RowVersion++;
    }

    /// <summary>
    /// An officer fixes the data. Unlike the AI, an officer may lower the severity.
    /// Rejected when the pothole is closed, the reason is empty, or nothing would change.
    /// </summary>
    public void Correct(Correction c, PotholePrices prices)
    {
        if (Status == PotholeStatus.Closed)
            throw new DomainException($"Pothole {Id} is closed. A closed pothole cannot be corrected.");
        if (string.IsNullOrWhiteSpace(c.Reason))
            throw new DomainException("A correction needs a reason.");
        CheckSize(c.AreaM2, c.DepthCm);

        var severity = c.Severity ?? CurrentSeverity;
        var stage = c.Stage ?? CurrentStage;
        var areaM2 = c.AreaM2 ?? AreaM2;
        var depthCm = c.DepthCm ?? DepthCm;
        if (severity == CurrentSeverity && stage == CurrentStage && areaM2 == AreaM2 && depthCm == DepthCm)
            throw new DomainException("The correction changes nothing.");

        CurrentSeverity = severity;
        CurrentStage = stage;
        AreaM2 = areaM2;
        DepthCm = depthCm;
        Estimate = PotholeEstimator.Estimate(CurrentSeverity, CurrentStage, AreaM2, DepthCm, prices);

        _observations.Add(Observation.FromCorrection(this, c));
        RowVersion++;
    }

    /// <summary>
    /// The AI saw this open pothole again. Returns true when the severity, the stage or the type
    /// changed, which is when Pothole publishes <c>PotholeSaved</c>.
    /// </summary>
    public bool AddSighting(Detection d, PotholePrices prices)
    {
        if (Status == PotholeStatus.Closed)
            throw new DomainException($"Pothole {Id} is closed. A closed pothole does not take new sightings.");

        var oldSeverity = CurrentSeverity;
        var oldStage = CurrentStage;
        var oldType = DamageType;

        _observations.Add(Observation.FromDetection(Id, d));
        SightingsCount++;
        if (d.SeenAt > LastSeenAt)
            LastSeenAt = d.SeenAt;

        // The AI never lowers the severity: keep the higher of the old and the new one.
        // (Only an officer correction may lower it, see Correct.)
        if (d.Severity > CurrentSeverity)
            CurrentSeverity = d.Severity;

        // The latest reading wins. A size or depth the AI did not measure this time keeps the old value.
        AreaM2 = d.AreaM2 ?? AreaM2;
        DepthCm = d.DepthCm ?? DepthCm;
        Confidence = d.Confidence;

        // Type and stage only move forward: a crack seen later as a pothole becomes a pothole,
        // a pothole seen later as a crack stays a pothole.
        if (TypeOrder(d.DamageType) > TypeOrder(DamageType))
            DamageType = d.DamageType;
        if (d.Stage > CurrentStage)
            CurrentStage = d.Stage;

        var severityOrStageChanged = CurrentSeverity != oldSeverity || CurrentStage != oldStage;
        if (severityOrStageChanged)
            Estimate = PotholeEstimator.Estimate(CurrentSeverity, CurrentStage, AreaM2, DepthCm, prices);

        RowVersion++;
        return severityOrStageChanged || DamageType != oldType;
    }

    /// <summary>
    /// The one thing allowed on a closed pothole: an AI sighting next to a pothole closed as
    /// FalsePositive in the last 30 days. Only the observation is saved; the pothole stays closed.
    /// </summary>
    public void AddSuppressedSighting(Detection d)
    {
        if (Status != PotholeStatus.Closed || ClosedReason != Potholes.ClosedReason.FalsePositive)
            throw new DomainException($"Pothole {Id} is not closed as FalsePositive.");

        _observations.Add(Observation.FromDetection(Id, d));
        RowVersion++;
    }

    /// <summary>Copy the ticket status for display. Older messages and messages after closing are ignored.</summary>
    public void ApplyRepairStatus(Guid workOrderId, string status, long sequence)
    {
        if (Status == PotholeStatus.Closed)
            return;
        if (sequence <= RepairStatusSeq)
            return;

        WorkOrderId = workOrderId;
        RepairStatus = status;
        RepairStatusSeq = sequence;
        RowVersion++;
    }

    /// <summary>Close the pothole for good. Closing an already closed pothole does nothing.</summary>
    public void Close(ClosedReason reason, Guid? duplicateOfId, DateTimeOffset at)
    {
        if (Status == PotholeStatus.Closed)
            return;

        if (reason == Potholes.ClosedReason.Duplicate)
        {
            if (duplicateOfId is null)
                throw new DomainException("A pothole closed as Duplicate needs the id of the other pothole.");
            if (duplicateOfId == Id)
                throw new DomainException("A pothole cannot be a duplicate of itself.");
        }

        Status = PotholeStatus.Closed;
        ClosedReason = reason;
        ClosedAt = at;
        DuplicateOfId = reason == Potholes.ClosedReason.Duplicate ? duplicateOfId : null;
        RowVersion++;
    }

    // A size, when given, is a real measurement (the table checks area_m2 > 0 and depth_cm > 0).
    private static void CheckSize(double? areaM2, double? depthCm)
    {
        if (areaM2 <= 0)
            throw new DomainException("The area must be more than 0 m².");
        if (depthCm <= 0)
            throw new DomainException("The depth must be more than 0 cm.");
    }

    // RoadCrack and SurfaceDamage are below Pothole.
    private static int TypeOrder(DetectionType type) => type == DetectionType.Pothole ? 1 : 0;
}
