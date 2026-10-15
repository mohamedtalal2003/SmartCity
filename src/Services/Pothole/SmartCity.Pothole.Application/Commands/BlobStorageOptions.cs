namespace SmartCity.Pothole.Application.Commands;

/// <summary>Where our blob storage serves files, from the <c>S3</c> section of appsettings.</summary>
public sealed class BlobStorageOptions
{
    public const string SectionName = "S3";

    /// <summary>For example <c>http://localhost:9000</c>. A file is at <c>{PublicBaseUrl}/{bucket}/{key}</c>.</summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>True when the URL points to a file in our own blob storage (commands.proto: MUST be a blob-storage URL).</summary>
    public bool IsOurUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && Uri.IsWellFormedUriString(url, UriKind.Absolute)
        && url.StartsWith(PublicBaseUrl.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
}
