namespace SmartCity.VehicleCommunication.Application.Abstractions;

/// <summary>
/// Where the frame images are kept (S3-compatible blob storage). The vehicles never talk to it; only this
/// service does (decision 1).
/// </summary>
public interface IFrameStorage
{
    /// <summary>
    /// Saves the image under <paramref name="key"/> and returns its URL. Saving the same key again overwrites
    /// it, so a retry never makes a second copy. Throws <see cref="StorageUnavailableException"/> when the
    /// storage does not work (after the retries) or the circuit breaker is open.
    /// </summary>
    Task<string> SaveAsync(string key, byte[] image, string contentType, CancellationToken ct);

    /// <summary>
    /// Checks the storage directly. When it works, uploads are let through again at once (the circuit
    /// breaker is closed).
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken ct);
}

/// <summary>The frame storage does not work right now. The vehicle keeps the frame and tries again later.</summary>
public sealed class StorageUnavailableException : Exception
{
    public StorageUnavailableException(string message, Exception inner) : base(message, inner) { }
}
