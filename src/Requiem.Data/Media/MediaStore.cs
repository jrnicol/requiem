using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Requiem.Data.Media;

/// <summary>
/// Uploaded images (character portraits), served under <c>/media/</c>: from an S3 bucket behind CloudFront on
/// AWS, or from a local folder the app serves itself in development and tests.
/// </summary>
public interface IMediaStore
{
    /// <summary>Stores an image under a new, unguessable name and returns its address (/media/...).</summary>
    Task<string> SaveAsync(string folder, ReadOnlyMemory<byte> image, string contentType, CancellationToken ct);

    /// <summary>Removes an image saved here; addresses from anywhere else are ignored.</summary>
    Task DeleteAsync(string? url, CancellationToken ct);
}

public sealed class MediaOptions
{
    public const string Section = "Media";
    public const string UrlPrefix = "/media/";

    /// <summary>The S3 bucket; when unset, images go to <see cref="LocalPath"/>.</summary>
    public string? Bucket { get; set; }

    /// <summary>The local folder (development and tests). Relative paths are under the content root.</summary>
    public string LocalPath { get; set; } = "App_Data/media";
}

public static class MediaRegistration
{
    public static IServiceCollection AddRequiemMedia(this IServiceCollection services, IConfiguration configuration, string contentRoot)
    {
        var options = configuration.GetSection(MediaOptions.Section).Get<MediaOptions>() ?? new MediaOptions();
        if (options.Bucket is { Length: > 0 } bucket)
        {
            services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client());
            services.AddSingleton<IMediaStore>(sp => new S3MediaStore(sp.GetRequiredService<IAmazonS3>(), bucket));
        }
        else
        {
            services.AddSingleton<IMediaStore>(new LocalMediaStore(Path.GetFullPath(Path.Combine(contentRoot, options.LocalPath))));
        }
        return services;
    }

    /// <summary>The local folder the app serves under /media/, or null when images live in S3.</summary>
    public static string? LocalFolder(IServiceProvider services) =>
        (services.GetRequiredService<IMediaStore>() as LocalMediaStore)?.Root;
}

internal static class MediaKeys
{
    public static string New(string folder, string contentType) =>
        $"{folder.Trim('/')}/{Guid.NewGuid():N}{contentType switch { "image/png" => ".png", "image/webp" => ".webp", _ => ".jpg" }}";

    /// <summary>The key inside the store for one of its own addresses, or null.</summary>
    public static string? FromUrl(string? url) =>
        url is not null && url.StartsWith(MediaOptions.UrlPrefix, StringComparison.Ordinal) && !url.Contains("..", StringComparison.Ordinal)
            ? url[MediaOptions.UrlPrefix.Length..]
            : null;
}

/// <remarks>
/// CloudFront passes the whole path through, so /media/portraits/x.jpg is the object media/portraits/x.jpg.
/// </remarks>
internal sealed class S3MediaStore(IAmazonS3 s3, string bucket) : IMediaStore
{
    private const string Prefix = "media/";

    public async Task<string> SaveAsync(string folder, ReadOnlyMemory<byte> image, string contentType, CancellationToken ct)
    {
        var key = MediaKeys.New(folder, contentType);
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = Prefix + key,
            InputStream = new MemoryStream(image.ToArray()),
            ContentType = contentType,
            // Every upload gets a new name, so its content never changes.
            Headers = { CacheControl = "public, max-age=31536000, immutable" },
        }, ct);
        return MediaOptions.UrlPrefix + key;
    }

    public async Task DeleteAsync(string? url, CancellationToken ct)
    {
        if (MediaKeys.FromUrl(url) is { } key) await s3.DeleteObjectAsync(bucket, Prefix + key, ct);
    }
}

internal sealed class LocalMediaStore(string root) : IMediaStore
{
    public string Root => root;

    public async Task<string> SaveAsync(string folder, ReadOnlyMemory<byte> image, string contentType, CancellationToken ct)
    {
        var key = MediaKeys.New(folder, contentType);
        var path = Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, image.ToArray(), ct);
        return MediaOptions.UrlPrefix + key;
    }

    public Task DeleteAsync(string? url, CancellationToken ct)
    {
        if (MediaKeys.FromUrl(url) is { } key)
        {
            var path = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
            if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path)) File.Delete(path);
        }
        return Task.CompletedTask;
    }
}
