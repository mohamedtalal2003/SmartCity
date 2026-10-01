using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Util;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SmartCity.BuildingBlocks;

public static class BlobStorageExtensions
{
    /// <summary>Registers a singleton <see cref="IAmazonS3"/> from the <c>S3:*</c> config section.</summary>
    public static WebApplicationBuilder AddSmartCityBlobStorage(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<S3Options>().Bind(builder.Configuration.GetSection("S3"));

        builder.Services.AddSingleton<IAmazonS3>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<S3Options>>().Value;
            var config = new AmazonS3Config
            {
                ForcePathStyle = options.ForcePathStyle,
                // Only send/validate checksums when S3 requires them; S3-compatible stores vary here.
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
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
        });

        return builder;
    }

    /// <summary>
    /// Development only: creates any of <paramref name="buckets"/> that are missing, retrying while storage is
    /// unreachable. Never touches existing buckets or lifecycle/expiry rules; those come from
    /// infra/s3/init-buckets.sh locally and from infra config in production.
    /// </summary>
    public static async Task EnsureBucketsAsync(this WebApplication app, params string[] buckets)
    {
        if (!app.Environment.IsDevelopment())
            return;

        var s3 = app.Services.GetRequiredService<IAmazonS3>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(BlobStorageExtensions));
        const int maxAttempts = 30;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                foreach (var bucket in buckets)
                {
                    if (await AmazonS3Util.DoesS3BucketExistV2Async(s3, bucket))
                        continue;

                    try
                    {
                        await s3.PutBucketAsync(bucket);
                        logger.LogInformation("Created missing bucket {Bucket}", bucket);
                    }
                    catch (AmazonS3Exception e) when (e.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
                    {
                        // Created concurrently (another instance or s3-init): nothing to do.
                    }
                }
                return;
            }
            catch (Exception e) when (attempt < maxAttempts && e is not OperationCanceledException)
            {
                logger.LogWarning("Blob storage not reachable yet (attempt {Attempt}/{MaxAttempts}): {Error}",
                    attempt, maxAttempts, e.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), app.Lifetime.ApplicationStopping);
            }
        }
    }
}
