using Microsoft.Extensions.Logging;
using SmartCity.Pothole.Application.Abstractions;

namespace SmartCity.Pothole.Application.WorkOrders;

using SmartCity.Pothole.Domain.Potholes;

/// <summary>
/// Use case P7: the officer dismissed the detection in Work Order. The pothole closes as FalsePositive,
/// and from then on the 30-day rule in <see cref="Detections.HandleDetection"/> works by itself.
/// </summary>
public sealed class DismissDetection
{
    private readonly IPotholeRepository _repository;
    private readonly ILogger<DismissDetection> _logger;

    public DismissDetection(IPotholeRepository repository, ILogger<DismissDetection> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task HandleAsync(Guid potholeId, DateTimeOffset dismissedAt, CancellationToken ct)
    {
        var pothole = await _repository.GetByIdAsync(potholeId, ct);
        if (pothole is null)
        {
            _logger.LogWarning("Pothole {PotholeId} not found for a dismissed detection, message ignored", potholeId);
            return;
        }
        if (pothole.Status == PotholeStatus.Closed)
        {
            _logger.LogInformation("Pothole {PotholeId} is already closed, dismissal ignored", potholeId);
            return;
        }

        pothole.Close(ClosedReason.FalsePositive, duplicateOfId: null, dismissedAt);
        await _repository.SaveChangesAsync(ct);
        _logger.LogInformation("Pothole {PotholeId} closed as FalsePositive (detection dismissed)", potholeId);
    }
}
