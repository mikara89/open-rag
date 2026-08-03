using Mediator;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Application.Common;
using OpenRAG.Application.Common.Results;

namespace OpenRAG.Application.Documents.GetJsonArtifact;

public sealed class GetJsonArtifactHandler
    : IRequestHandler<GetJsonArtifactQuery, Result<GetJsonArtifactResponse>>
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IFileStorage _fileStorage;
    private readonly IDocumentObjectKeyPolicy _objectKeyPolicy;
    private readonly ICurrentTenant _currentTenant;

    public GetJsonArtifactHandler(
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

    public async ValueTask<Result<GetJsonArtifactResponse>> Handle(
        GetJsonArtifactQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.DocumentId == Guid.Empty || query.VersionId == Guid.Empty)
        {
            var target = query.DocumentId == Guid.Empty ? "documentId" : "versionId";
            var code = query.DocumentId == Guid.Empty
                ? "request.document_id_required"
                : "request.version_id_required";
            return Result<GetJsonArtifactResponse>.Failure(
                ApplicationErrors.InvalidRequest(
                    code,
                    $"{(target == "documentId" ? "DocumentId" : "VersionId")} cannot be empty.",
                    target));
        }

        var tenantId = _currentTenant.TenantId;

        var version = await _documentRepository.GetVersionAsync(
            tenantId, query.DocumentId, query.VersionId, cancellationToken);

        if (version is null)
            return Result<GetJsonArtifactResponse>.Failure(ApplicationErrors.ResourceNotFound());

        IsolationGuard.Equal(version.TenantId, tenantId, nameof(version.TenantId));
        IsolationGuard.Equal(version.DocumentId, query.DocumentId, nameof(version.DocumentId));
        IsolationGuard.Equal(version.Id, query.VersionId, nameof(version.Id));

        if (string.IsNullOrWhiteSpace(version.DoclingJsonObjectKey)
            || string.Equals(version.DoclingJsonObjectKey, "pending", StringComparison.Ordinal))
            return Result<GetJsonArtifactResponse>.Failure(ApplicationErrors.ResourceNotFound());

        _objectKeyPolicy.EnsureOwned(
            version.DoclingJsonObjectKey,
            tenantId,
            query.DocumentId,
            query.VersionId,
            DocumentObjectKind.Json);

        await using var stream = await _fileStorage.OpenReadAsync(
            version.DoclingJsonObjectKey, cancellationToken);

        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(cancellationToken);

        return Result<GetJsonArtifactResponse>.Success(
            new GetJsonArtifactResponse(content, "application/json"));
    }
}
