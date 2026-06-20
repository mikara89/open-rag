namespace OpenRAG.Application.Abstractions.Storage;

public sealed record StoredObjectResult(
    string Bucket,
    string ObjectKey,
    string ContentType,
    long SizeBytes,
    string? ETag,
    string? Sha256
);

public interface IFileStorage
{
    Task<StoredObjectResult> SaveAsync(
        Stream content,
        string objectKey,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(
        string objectKey,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string objectKey,
        CancellationToken cancellationToken = default);
}
