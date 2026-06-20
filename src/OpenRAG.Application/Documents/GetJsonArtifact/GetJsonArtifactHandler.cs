using Mediator;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Abstractions.Storage;
using OpenRAG.Application.Common;

namespace OpenRAG.Application.Documents.GetJsonArtifact;

public sealed class GetJsonArtifactHandler : IRequestHandler<GetJsonArtifactQuery, GetJsonArtifactResponse>
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IFileStorage _fileStorage;
    private readonly ICurrentTenant _currentTenant;

    public GetJsonArtifactHandler(
        IDocumentRepository documentRepository,
        IFileStorage fileStorage,
        ICurrentTenant currentTenant)
    {
        _documentRepository = documentRepository;
        _fileStorage = fileStorage;
        _currentTenant = currentTenant;
    }

    public async ValueTask<GetJsonArtifactResponse> Handle(
        GetJsonArtifactQuery query,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _currentTenant.TenantId;

        var version = await _documentRepository.GetVersionAsync(
            tenantId, query.DocumentId, query.VersionId, cancellationToken);

        if (version is null)
            throw new AppException($"Version '{query.VersionId}' not found for document '{query.DocumentId}'.");

        if (string.IsNullOrWhiteSpace(version.DoclingJsonObjectKey))
            throw new AppException("JSON artifact not available for this version.");

        await using var stream = await _fileStorage.OpenReadAsync(
            version.DoclingJsonObjectKey, cancellationToken);

        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(cancellationToken);

        return new GetJsonArtifactResponse(content, "application/json");
    }
}
