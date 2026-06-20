namespace OpenRAG.Application.Abstractions.Processing;

public sealed record DocumentPreprocessingRequest(
    Guid TenantId,
    Guid DocumentId,
    Guid VersionId,
    string OriginalObjectKey,
    string FileName,
    string MimeType,
    string CorrelationId
);

public sealed record DocumentPreprocessingResult(
    string MarkdownObjectKey,
    string JsonObjectKey,
    string MarkdownSha256,
    string JsonSha256
);

public interface IDocumentPreprocessor
{
    Task<DocumentPreprocessingResult> PreprocessAsync(
        DocumentPreprocessingRequest request,
        CancellationToken cancellationToken = default);
}
