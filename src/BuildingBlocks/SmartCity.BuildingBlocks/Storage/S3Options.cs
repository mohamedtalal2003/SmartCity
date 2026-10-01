namespace SmartCity.BuildingBlocks;

/// <summary>The <c>S3:*</c> config section. Same code for SeaweedFS locally and AWS S3 in production.</summary>
public sealed class S3Options
{
    /// <summary>Custom endpoint (e.g. http://localhost:9000). Leave empty for AWS S3, which then uses <see cref="Region"/>.</summary>
    public string? ServiceUrl { get; set; }
    public bool ForcePathStyle { get; set; }
    public string Region { get; set; } = "us-east-1";
    /// <summary>Leave empty in production to use the default AWS credential chain (IAM role etc.).</summary>
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    /// <summary>Base of the image URLs put on events, e.g. http://localhost:9000 → {PublicBaseUrl}/{bucket}/{key}.</summary>
    public string PublicBaseUrl { get; set; } = "";
}
