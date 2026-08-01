using Mediator;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Application.Common;
using OpenRAG.Application.Common.Results;

namespace OpenRAG.Application.Documents.GetMarkdownArtifact;

public sealed class GetMarkdownArtifactHandler
    : IRequestHandler<GetMarkdownArtifactQuery, Result<GetMarkdownArtifactResponse>>
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IFileStorage _fileStorage;
    private readonly IDocumentObjectKeyPolicy _objectKeyPolicy;
    private readonly ICurrentTenant _currentTenant;

    public GetMarkdownArtifactHandler(
        IDocumentRepository documentRepository,
        IFileStorage fileStorage,
        IDocumentObjectKeyPolicy objectKeyPolicy,
        ICurrentTenant currentTenant)
    {
        _documentRepository = documentRepository;
        _fileStorage = fileStorage;
        _objectKeyPolicy = objectKeyPolicy;
        _currentTenant = currentTenant;
    }

    public async ValueTask<Result<GetMarkdownArtifactResponse>> Handle(
        GetMarkdownArtifactQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.DocumentId == Guid.Empty || query.VersionId == Guid.Empty)
        {
            var target = query.DocumentId == Guid.Empty ? "documentId" : "versionId";
            var code = query.DocumentId == Guid.Empty
                ? "request.document_id_required"
                : "request.version_id_required";
            return Result<GetMarkdownArtifactResponse>.Failure(
                ApplicationErrors.InvalidRequest(
                    code,
                    $"{(target == "documentId" ? "DocumentId" : "VersionId")} cannot be empty.",
                    target));
        }

        var tenantId = _currentTenant.TenantId;

        var version = await _documentRepository.GetVersionAsync(
            tenantId, query.DocumentId, query.VersionId, cancellationToken);

        if (version is null)
            return Result<GetMarkdownArtifactResponse>.Failure(ApplicationErrors.ResourceNotFound());

        IsolationGuard.Equal(version.TenantId, tenantId, nameof(version.TenantId));
        IsolationGuard.Equal(version.DocumentId, query.DocumentId, nameof(version.DocumentId));
        IsolationGuard.Equal(version.Id, query.VersionId, nameof(version.Id));

        if (string.IsNullOrWhiteSpace(version.DoclingMarkdownObjectKey)
            || string.Equals(version.DoclingMarkdownObjectKey, "pending", StringComparison.Ordinal))
            return Result<GetMarkdownArtifactResponse>.Failure(ApplicationErrors.ResourceNotFound());

        _objectKeyPolicy.EnsureOwned(
            version.DoclingMarkdownObjectKey,
            tenantId,
            query.DocumentId,
            query.VersionId,
            DocumentObjectKind.Markdown);

        await using var stream = await _fileStorage.OpenReadAsync(
            version.DoclingMarkdownObjectKey, cancellationToken);

        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(cancellationToken);

        return Result<GetMarkdownArtifactResponse>.Success(
            new GetMarkdownArtifactResponse(content, "text/markdown"));
    }
}
