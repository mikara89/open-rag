using Mediator;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Application.Common;

namespace OpenRAG.Application.Documents.GetMarkdownArtifact;

public sealed class GetMarkdownArtifactHandler : IRequestHandler<GetMarkdownArtifactQuery, GetMarkdownArtifactResponse>
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

    public async ValueTask<GetMarkdownArtifactResponse> Handle(
        GetMarkdownArtifactQuery query,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _currentTenant.TenantId;

        if (query.DocumentId == Guid.Empty || query.VersionId == Guid.Empty)
            throw new RequestValidationException("Document and version identifiers must be non-empty.");

        var version = await _documentRepository.GetVersionAsync(
            tenantId, query.DocumentId, query.VersionId, cancellationToken);

        if (version is null)
            throw new ResourceNotFoundException();

        IsolationGuard.Equal(version.TenantId, tenantId, nameof(version.TenantId));
        IsolationGuard.Equal(version.DocumentId, query.DocumentId, nameof(version.DocumentId));
        IsolationGuard.Equal(version.Id, query.VersionId, nameof(version.Id));

        if (string.IsNullOrWhiteSpace(version.DoclingMarkdownObjectKey)
            || string.Equals(version.DoclingMarkdownObjectKey, "pending", StringComparison.Ordinal))
            throw new ResourceNotFoundException();

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

        return new GetMarkdownArtifactResponse(content, "text/markdown");
    }
}
