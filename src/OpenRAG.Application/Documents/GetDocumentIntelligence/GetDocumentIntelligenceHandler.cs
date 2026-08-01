using System.Text.Json;
using Mediator;
using OpenRAG.Application.Abstractions.AI;
using OpenRAG.Application.Abstractions.Persistence;
using OpenRAG.Application.Abstractions.Security;
using OpenRAG.Application.Common;
using OpenRAG.Application.Common.Results;

namespace OpenRAG.Application.Documents.GetDocumentIntelligence;

public sealed class GetDocumentIntelligenceHandler
    : IRequestHandler<GetDocumentIntelligenceQuery, Result<DocumentIntelligenceResponse>>
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentIntelligenceRepository _intelligenceRepository;
    private readonly ICurrentTenant _currentTenant;

    public GetDocumentIntelligenceHandler(
        IDocumentRepository documentRepository,
        IDocumentIntelligenceRepository intelligenceRepository,
        ICurrentTenant currentTenant)
    {
        _documentRepository = documentRepository;
        _intelligenceRepository = intelligenceRepository;
        _currentTenant = currentTenant;
    }

    public async ValueTask<Result<DocumentIntelligenceResponse>> Handle(
        GetDocumentIntelligenceQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.DocumentId == Guid.Empty || query.VersionId == Guid.Empty)
        {
            var error = query.DocumentId == Guid.Empty
                ? ApplicationErrors.InvalidRequest(
                    "request.document_id_required", "DocumentId cannot be empty.", "documentId")
                : ApplicationErrors.InvalidRequest(
                    "request.version_id_required", "VersionId cannot be empty.", "versionId");
            return Result<DocumentIntelligenceResponse>.Failure(error);
        }

        var tenantId = _currentTenant.TenantId;

        // Verify document exists and belongs to tenant
        var document = await _documentRepository.GetByIdWithVersionsAsync(
            tenantId, query.DocumentId, cancellationToken);

        if (document is null)
            return Result<DocumentIntelligenceResponse>.Failure(ApplicationErrors.ResourceNotFound());

        IsolationGuard.Equal(document.TenantId, tenantId, nameof(document.TenantId));
        IsolationGuard.Equal(document.Id, query.DocumentId, nameof(document.Id));

        var version = document.Versions.FirstOrDefault(item => item.Id == query.VersionId);
        if (version is null)
            return Result<DocumentIntelligenceResponse>.Failure(ApplicationErrors.ResourceNotFound());

        IsolationGuard.Equal(version.TenantId, tenantId, nameof(version.TenantId));
        IsolationGuard.Equal(version.DocumentId, query.DocumentId, nameof(version.DocumentId));
        IsolationGuard.Equal(version.Id, query.VersionId, nameof(version.Id));

        var intel = await _intelligenceRepository.GetByVersionAsync(
            tenantId, query.DocumentId, query.VersionId, cancellationToken);

        if (intel is null)
            return Result<DocumentIntelligenceResponse>.Failure(ApplicationErrors.ResourceNotFound());

        IsolationGuard.Equal(intel.TenantId, tenantId, nameof(intel.TenantId));
        IsolationGuard.Equal(intel.DocumentId, query.DocumentId, nameof(intel.DocumentId));
        IsolationGuard.Equal(intel.VersionId, query.VersionId, nameof(intel.VersionId));

        return Result<DocumentIntelligenceResponse>.Success(new DocumentIntelligenceResponse(
            Classification: intel.Classification,
            Summary: intel.Summary,
            Keywords: DeserializeList(intel.KeywordsJson),
            Entities: DeserializeEntities(intel.EntitiesJson),
            Metadata: DeserializeDictionary(intel.ExtractedMetadataJson),
            Provider: intel.Provider,
            Model: intel.Model,
            CreatedAt: intel.CreatedAt,
            UpdatedAt: intel.UpdatedAt));
    }

    private static IReadOnlyList<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();

        try
        {
            var result = JsonSerializer.Deserialize<List<string>>(json);
            return result?.AsReadOnly() ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<IntelligenceEntity> DeserializeEntities(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<IntelligenceEntity>();

        try
        {
            var result = JsonSerializer.Deserialize<List<IntelligenceEntity>>(json);
            return result?.AsReadOnly() ?? (IReadOnlyList<IntelligenceEntity>)Array.Empty<IntelligenceEntity>();
        }
        catch
        {
            return Array.Empty<IntelligenceEntity>();
        }
    }

    private static IReadOnlyDictionary<string, string> DeserializeDictionary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }
}

public sealed record DocumentIntelligenceResponse(
    string? Classification,
    string? Summary,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<IntelligenceEntity> Entities,
    IReadOnlyDictionary<string, string> Metadata,
    string Provider,
    string Model,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
