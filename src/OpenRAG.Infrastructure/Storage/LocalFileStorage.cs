using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenRAG.Application.Abstractions.Storage;

namespace OpenRAG.Infrastructure.Storage;

public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _rootPath;
    private readonly ILogger<LocalFileStorage> _logger;

    public LocalFileStorage(IOptions<LocalFileStorageOptions> options, ILogger<LocalFileStorage> logger)
    {
        _rootPath = Path.GetFullPath(options.Value.LocalRootPath);
        _logger = logger;
    }

    public async Task<StoredObjectResult> SaveAsync(
        Stream content,
        string objectKey,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeObjectKey(objectKey);
        var fullPath = GetFullPath(normalizedKey);

        var directory = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write file and close stream before computing hash
        long sizeBytes;
        {
            await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
            await content.CopyToAsync(fileStream, cancellationToken);
            await fileStream.FlushAsync(cancellationToken);
            sizeBytes = fileStream.Length;
        }

        var sha256 = ComputeSha256(fullPath);

        _logger.LogInformation(
            "Saved file: {ObjectKey}, Size: {SizeBytes}, Sha256: {Sha256}",
            normalizedKey, sizeBytes, sha256);

        return new StoredObjectResult(
            Bucket: "local",
            ObjectKey: normalizedKey,
            ContentType: contentType,
            SizeBytes: sizeBytes,
            ETag: null,
            Sha256: sha256);
    }

    public Task<Stream> OpenReadAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeObjectKey(objectKey);
        var fullPath = GetFullPath(normalizedKey);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"File not found: {normalizedKey}", fullPath);
        }

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult<Stream>(stream);
    }

    public Task DeleteAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeObjectKey(objectKey);
        var fullPath = GetFullPath(normalizedKey);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    private string NormalizeObjectKey(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new ArgumentException("Object key cannot be empty.", nameof(objectKey));
        }

        // Reject absolute paths
        if (Path.IsPathFullyQualified(objectKey))
        {
            throw new ArgumentException("Object key must be a relative path.", nameof(objectKey));
        }

        // Normalize separators and remove traversal segments
        var normalized = objectKey.Replace('\\', '/').TrimStart('/');

        // Reject path traversal
        var segments = normalized.Split('/');
        if (segments.Any(s => s == ".."))
        {
            throw new ArgumentException("Object key must not contain path traversal.", nameof(objectKey));
        }

        return normalized;
    }

    private string GetFullPath(string normalizedKey)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, normalizedKey));

        // Ensure the resolved path is still under root
        if (!fullPath.StartsWith(_rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && fullPath != _rootPath)
        {
            throw new ArgumentException("Object key resolves outside the storage root.", nameof(normalizedKey));
        }

        return fullPath;
    }

    private static string ComputeSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexStringLower(hash);
    }
}

public sealed class LocalFileStorageOptions
{
    public const string SectionName = "Storage";

    public string Provider { get; set; } = "Local";
    public string LocalRootPath { get; set; } = ".openrag-storage";
}
