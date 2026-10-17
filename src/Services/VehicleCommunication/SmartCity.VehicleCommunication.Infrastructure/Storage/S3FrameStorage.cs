using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using SmartCity.BuildingBlocks;
using SmartCity.VehicleCommunication.Application.Abstractions;

namespace SmartCity.VehicleCommunication.Infrastructure.Storage;

/// <summary>
/// Saves frames to S3-compatible storage (SeaweedFS locally, AWS S3 in production) with Polly (V13):
/// <list type="number">
/// <item>Circuit breaker (outer): after enough failed uploads it opens, and every upload fails at once
/// without trying, until it closes again.</item>
/// <item>Retry: 3 quick retries inside the same upload (200, 400, 800 ms).</item>
/// <item>Timeout (inner): one storage call that hangs counts as failed.</item>
/// </list>
/// It has its own S3 client with the SDK's own retries turned off, so Polly is the only place that retries.
/// </summary>
public sealed class S3FrameStorage : IFrameStorage, IDisposable
{
    private readonly IAmazonS3 _s3;
    private readonly FrameStorageOptions _options;
    private readonly string _publicBaseUrl;
    private readonly CircuitBreakerManualControl _breaker = new();
    private readonly ResiliencePipeline _pipeline;
    private readonly ILogger<S3FrameStorage> _logger;

    public S3FrameStorage(IOptions<S3Options> s3Options, IOptions<FrameStorageOptions> options, ILogger<S3FrameStorage> logger)
    {
        _options = options.Value;
        _publicBaseUrl = s3Options.Value.PublicBaseUrl.TrimEnd('/');
        _logger = logger;
        _s3 = CreateClient(s3Options.Value);

        var notCancelled = new PredicateBuilder().Handle<Exception>(e => e is not OperationCanceledException);
        _pipeline = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = _options.BreakerFailureRatio,
                MinimumThroughput = _options.BreakerMinimumThroughput,
                SamplingDuration = TimeSpan.FromSeconds(_options.BreakerSamplingSeconds),
                BreakDuration = TimeSpan.FromSeconds(_options.BreakerBreakSeconds),
                ManualControl = _breaker,
                ShouldHandle = notCancelled,
                OnOpened = _ =>
                {
                    _logger.LogError("Frame storage circuit breaker opened: uploads are refused for {Seconds} s",
                        _options.BreakerBreakSeconds);
                    return ValueTask.CompletedTask;
                },
                OnClosed = _ =>
                {
                    _logger.LogInformation("Frame storage circuit breaker closed: uploads go through again");
                    return ValueTask.CompletedTask;
                }
            })
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = _options.RetryCount,
                Delay = TimeSpan.FromMilliseconds(_options.RetryFirstDelayMs),
                BackoffType = DelayBackoffType.Exponential,     // 200, 400, 800 ms
                UseJitter = false,
                ShouldHandle = notCancelled
            })
            .AddTimeout(TimeSpan.FromSeconds(_options.AttemptTimeoutSeconds))
            .Build();
    }

    public async Task<string> SaveAsync(string key, byte[] image, string contentType, CancellationToken ct)
    {
        try
        {
            await _pipeline.ExecuteAsync(async token =>
            {
                // A new stream for every attempt: a failed attempt may have read part of the old one.
                using var stream = new MemoryStream(image, writable: false);
                await _s3.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = _options.Bucket,
                    Key = key,
                    InputStream = stream,
                    ContentType = contentType
                }, token);
            }, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // S3 errors after the retries, timeouts, and BrokenCircuitException when the breaker is open.
            throw new StorageUnavailableException($"Could not save {key}", e);
        }

        return $"{_publicBaseUrl}/{_options.Bucket}/{key}";
    }

    public async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.AttemptTimeoutSeconds));
            await _s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _options.Bucket, MaxKeys = 1 }, timeout.Token);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            _logger.LogDebug("Frame storage still unavailable: {Error}", e.Message);
            return false;
        }

        // It works: let uploads through now instead of waiting for the break time to end.
        await _breaker.CloseAsync(ct);
        return true;
    }

    public void Dispose() => _s3.Dispose();

    // Same settings as BuildingBlocks' AddSmartCityBlobStorage, but with MaxErrorRetry = 0.
    private static AmazonS3Client CreateClient(S3Options options)
    {
        var config = new AmazonS3Config
        {
            ForcePathStyle = options.ForcePathStyle,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            MaxErrorRetry = 0
        };
        if (string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }
        else
        {
            config.ServiceURL = options.ServiceUrl;
            config.AuthenticationRegion = options.Region;
        }

        return string.IsNullOrWhiteSpace(options.AccessKey)
            ? new AmazonS3Client(config)
            : new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config);
    }
}
