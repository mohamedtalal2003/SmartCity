using Microsoft.Extensions.Logging;
using SmartCity.Pothole.Application.Abstractions;

namespace SmartCity.Pothole.Application.Verifications;

using SmartCity.Pothole.Domain.Potholes;

/// <summary>
/// Use case: the AI checked a repaired pothole on a vehicle pass (Pothole's part of P10). The result is
/// saved as an observation with its photo. Completing the ticket is Work Order's decision, not Pothole's.
/// </summary>
public sealed class RecordVerificationPass
{
    private readonly IPotholeRepository _repository;
    private readonly ILogger<RecordVerificationPass> _logger;

    public RecordVerificationPass(IPotholeRepository repository, ILogger<RecordVerificationPass> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task HandleAsync(Guid potholeId, VerificationPass pass, CancellationToken ct)
    {
        // The same frame for the same pothole is saved once.
        if (await _repository.VerificationExistsAsync(potholeId, pass.FrameId, ct))
        {
            _logger.LogInformation("Verification frame {FrameId} was already saved on pothole {PotholeId}, skipping",
                pass.FrameId, potholeId);
            return;
        }

        var pothole = await _repository.GetByIdAsync(potholeId, ct);
        if (pothole is null)
        {
            _logger.LogWarning("Pothole {PotholeId} not found for verification frame {FrameId}, message ignored",
                potholeId, pass.FrameId);
            return;
        }
        if (pothole.Status == PotholeStatus.Closed && pothole.ClosedReason != ClosedReason.Repaired)
        {
            _logger.LogInformation("Pothole {PotholeId} is closed as {ClosedReason}, verification frame {FrameId} ignored",
                potholeId, pothole.ClosedReason, pass.FrameId);
            return;
        }

        pothole.AddVerificationPass(pass);
        await _repository.SaveChangesAsync(ct);
        _logger.LogInformation("Verification pass {Result} saved on pothole {PotholeId} (frame {FrameId})",
            pass.Result, potholeId, pass.FrameId);
    }
}
